"""Run end-to-end checks against a local API backed by a disposable PostgreSQL database."""

import argparse
import concurrent.futures
import datetime as dt
import http.cookiejar
import json
import os
import threading
import urllib.error
import urllib.parse
import urllib.request
import uuid


class Client:
    def __init__(self, base_url):
        self.base_url = base_url.rstrip("/")
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar())
        )
        self.csrf = None

    def call(self, method, path, data=None, expected=200, checkout_key=None, send_csrf=True):
        headers = {"Accept": "application/json"}
        body = json.dumps(data).encode() if data is not None else None
        if body is not None:
            headers["Content-Type"] = "application/json"
        if method not in {"GET", "HEAD"} and send_csrf:
            if self.csrf is None:
                self.csrf = self.call("GET", "/api/auth/csrf")["token"]
            headers["X-CSRF-TOKEN"] = self.csrf
        if checkout_key is not None:
            headers["Idempotency-Key"] = str(checkout_key)
        request = urllib.request.Request(self.base_url + path, body, headers, method=method)
        try:
            with self.opener.open(request, timeout=20) as response:
                status, raw = response.status, response.read()
        except urllib.error.HTTPError as error:
            status, raw = error.code, error.read()
        if status != expected:
            raise AssertionError(f"{method} {path} returned {status}, expected {expected}: {raw[:2000]}")
        return json.loads(raw) if raw else None

    def login(self, email, password, expected=200):
        self.csrf = None
        result = self.call("POST", "/api/auth/login", credentials(email, password), expected=expected)
        self.csrf = None
        return result


def credentials(email, password):
    return {"email": email, "password": password}


def inventory(client, variant, location):
    rows = client.call("GET", f"/api/management/inventory?variantId={variant}&locationId={location}")
    return (rows[0]["onHand"], rows[0]["reserved"], rows[0]["available"]) if rows else (0, 0, 0)


def run(base_url, admin_email, admin_password):
    admin = Client(base_url)
    admin.call("GET", "/health")
    openapi = admin.call("GET", "/openapi/v1.json")
    assert "/api/orders/checkout" in openapi["paths"]
    admin.call("GET", "/api/admin/accounts", expected=401)
    assert admin.login(admin_email, admin_password)["roles"] == ["Admin"]

    suffix = uuid.uuid4().hex[:8]
    demo_password = "DemoBuyer123"
    manager_email = f"manager-{suffix}@example.test"
    manager_id = admin.call("POST", "/api/auth/register", credentials(manager_email, demo_password), 201)["id"]
    buyer_email = f"buyer-{suffix}@example.test"
    other_email = f"other-{suffix}@example.test"
    buyer_id = admin.call("POST", "/api/auth/register", credentials(buyer_email, demo_password), 201)["id"]
    admin.call("POST", "/api/auth/register", credentials(other_email, demo_password), 201)
    admin.call("PUT", f"/api/admin/accounts/{manager_id}/access", {"role": "manager", "disabled": False})
    staff, staff_retry, buyer, other = [Client(base_url) for _ in range(4)]
    assert staff.login(manager_email, demo_password)["roles"] == ["Manager"]
    staff_retry.login(manager_email, demo_password)
    assert buyer.login(buyer_email, demo_password)["roles"] == ["Customer"]
    assert other.login(other_email, demo_password)["roles"] == ["Customer"]
    buyer.call("GET", "/api/admin/accounts", expected=403)
    staff.call("GET", "/api/admin/accounts", expected=403)
    assert buyer.call("PUT", "/api/auth/me/contact", {"fullName": "First Buyer", "phone": "+12345678901"})[
        "fullName"
    ] == "First Buyer"
    buyer.call("PUT", "/api/auth/me/contact", {"fullName": "First Buyer", "phone": "+12345678901"},
               expected=400, send_csrf=False)
    print("OK accounts and authorization")

    period_start = dt.datetime.now(dt.timezone.utc) - dt.timedelta(seconds=1)
    catalog = "/api/management/catalog"
    category = staff.call("POST", f"{catalog}/categories", {"name": f"T-shirts {suffix}"}, 201)["id"]
    manufacturer = staff.call("POST", f"{catalog}/manufacturers",
                              {"name": f"Maker {suffix}", "country": "Russia"}, 201)["id"]
    grid = staff.call("POST", f"{catalog}/size-grids", {"name": f"Adult {suffix}"}, 201)["id"]
    size = staff.call("POST", f"{catalog}/size-grids/{grid}/sizes",
                      {"label": "M", "measurements": "Chest 96 cm"}, 201)["id"]
    foreign_grid = staff.call("POST", f"{catalog}/size-grids", {"name": f"Other {suffix}"}, 201)["id"]
    foreign_size = staff.call("POST", f"{catalog}/size-grids/{foreign_grid}/sizes",
                              {"label": "M", "measurements": "Chest 100 cm"}, 201)["id"]
    product_request = {
        "name": f"Shirt {suffix}", "description": "Cotton", "price": 1490,
        "categoryId": category, "manufacturerId": manufacturer, "sizeGridId": grid,
    }
    product = staff.call("POST", f"{catalog}/products", product_request, 201)["id"]
    variants = f"{catalog}/products/{product}/variants"
    staff.call("POST", variants, {"sku": f"{suffix}-BAD", "color": "black", "gridSizeId": foreign_size}, 400)
    sku = f"{suffix}-M-BLK"
    variant = staff.call("POST", variants, {"sku": sku, "color": "black", "gridSizeId": size}, 201)["id"]
    variant2 = staff.call("POST", variants, {"sku": f"{suffix}-M-WHT", "color": "white", "gridSizeId": size}, 201)["id"]
    matches = buyer.call("GET", f"/api/catalog/products?search={sku}&size=M&color=black")
    assert [entry["id"] for entry in matches] == [product]
    assert buyer.call("GET", f"/api/catalog/products?search={sku}&size=M&color=white") == []
    print("OK product catalog and size grid")

    stock_api = "/api/management/inventory"
    points = staff.call("GET", "/api/catalog/locations")
    warehouses = [point["id"] for point in points if point["kind"] == "warehouse"]
    warehouse = min(warehouses) if warehouses else staff.call("POST", f"{stock_api}/locations",
        {"kind": "warehouse", "name": f"Warehouse {suffix}", "address": "Warehouse address"}, 201)["id"]
    store = staff.call("POST", f"{stock_api}/locations",
                       {"kind": "store", "name": f"Store {suffix}", "address": "Store address"}, 201)["id"]

    def receipt(item, quantity):
        return staff.call("POST", f"{stock_api}/receipts",
                          {"variantId": item, "warehouseId": warehouse, "quantity": quantity}, 201)

    def transfer(item, quantity, source, destination, expected=201):
        return staff.call("POST", f"{stock_api}/transfers",
                          {"variantId": item, "sourceId": source,
                           "destinationId": destination, "quantity": quantity}, expected)

    receipt(variant, 3)
    receipt(variant2, 1)
    transfer(variant, 1, warehouse, store)
    assert inventory(staff, variant, warehouse) == (2, 0, 2)
    assert inventory(staff, variant, store) == (1, 0, 1)

    cart_item = f"/api/cart/items/{variant}"
    cart_item2 = f"/api/cart/items/{variant2}"
    pickup = {"fulfillment": "pickup", "pickupLocationId": store, "fullName": "Buyer", "phone": "+12345678901"}
    checkout = "/api/orders/checkout"
    buyer.call("PUT", cart_item, {"quantity": 0}, 400)
    buyer.call("PUT", cart_item, {"quantity": 1})
    buyer.call("PUT", cart_item2, {"quantity": 1})
    rejected = buyer.call("POST", checkout, pickup, 409, uuid.uuid4())
    assert rejected["status"] == 409
    assert inventory(staff, variant, store) == (1, 0, 1)
    assert len(buyer.call("GET", "/api/cart")["items"]) == 2
    assert buyer.call("GET", "/api/orders") == []
    buyer.call("DELETE", cart_item2, expected=204)
    other.call("PUT", cart_item, {"quantity": 1})

    checkout_ready = threading.Barrier(2, timeout=10)

    def try_checkout(client, key):
        checkout_ready.wait()
        try:
            return client.call("POST", checkout, pickup, 201, key)
        except AssertionError as error:
            if "returned 409," in str(error):
                return None
            raise

    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        attempts = [(client, uuid.uuid4()) for client in (buyer, other)]
        futures = [pool.submit(try_checkout, client, key) for client, key in attempts]
        results = [future.result() for future in futures]

    assert sum(result is not None for result in results) == 1
    winner_index = next(i for i, result in enumerate(results) if result)
    winner, winning_key = attempts[winner_index]
    loser, _ = attempts[1 - winner_index]
    order = results[winner_index]
    order_id = order["id"]
    assert winner.call("POST", checkout, pickup, 200, winning_key)["id"] == order_id
    assert winner.call("GET", "/api/cart")["items"] == []
    assert len(loser.call("GET", "/api/cart")["items"]) == 1
    assert order["status"] == "placed" and order["total"] == 1490
    assert inventory(staff, variant, store) == (1, 1, 0)
    transfer(variant, 1, store, warehouse, 409)
    assert inventory(staff, variant, warehouse) == (2, 0, 2)
    loser.call("GET", f"/api/orders/{order_id}", expected=404)
    loser.call("POST", f"/api/orders/{order_id}/cancel", expected=404)
    assert winner.call("POST", f"/api/orders/{order_id}/cancel")["status"] == "cancelled"
    assert inventory(staff, variant, store) == (1, 0, 1)
    print("OK rollback, concurrent checkout, ownership and cancellation")

    staff.call("PUT", f"{catalog}/products/{product}", {**product_request, "price": 1790})
    pickup_order = loser.call("POST", checkout, pickup, 201, uuid.uuid4())
    assert pickup_order["items"][0]["unitPrice"] == 1790
    staff_order = f"/api/management/orders/{pickup_order['id']}"
    staff.call("POST", f"{staff_order}/complete", expected=409)
    for stage in ("confirm", "prepare", "handover"):
        staff.call("POST", f"{staff_order}/{stage}")
    loser.call("POST", f"/api/orders/{pickup_order['id']}/cancel", expected=409)

    complete_ready = threading.Barrier(2, timeout=10)

    def complete(session):
        session.csrf = session.call("GET", "/api/auth/csrf")["token"]
        complete_ready.wait()
        return session.call("POST", f"{staff_order}/complete")

    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        completed = list(pool.map(complete, (staff, staff_retry)))
    assert all(order["status"] == "completed" for order in completed)
    assert inventory(staff, variant, store) == (0, 0, 0)
    assert winner.call("GET", f"/api/orders/{order_id}")["items"][0]["unitPrice"] == 1490

    winner.call("PUT", cart_item, {"quantity": 1})
    delivery = {key: value for key, value in pickup.items() if key != "pickupLocationId"}
    delivery["fulfillment"] = "delivery"
    winner.call("POST", checkout, delivery, 400, uuid.uuid4())
    delivery["address"] = "Delivery address"
    shipped = winner.call("POST", checkout, delivery, 201, uuid.uuid4())
    assert shipped["location"]["id"] == warehouse
    for stage in ("confirm", "prepare", "handover", "complete"):
        staff.call("POST", f"/api/management/orders/{shipped['id']}/{stage}")
    assert inventory(staff, variant, warehouse) == (1, 0, 1)

    period = urllib.parse.urlencode({"from": period_start.isoformat(), "to": dt.datetime.now(dt.timezone.utc).isoformat()})
    summary = staff.call("GET", f"/api/management/reports/sales?{period}&locationId={store}")
    assert summary == {"orders": 1, "revenue": 1790, "averageOrder": 1790}
    comparison = admin.call("GET", f"/api/admin/reports/sales?{period}")
    rows = {row["location"]["id"]: row["sales"] for row in comparison["locations"]}
    assert rows[store]["orders"] == 1
    assert rows[warehouse]["orders"] == 1
    print("OK completion, frozen prices and sales reports")

    admin.call("PUT", f"/api/admin/accounts/{buyer_id}/access", {"role": "customer", "disabled": True})
    buyer.call("GET", "/api/cart", expected=401)
    buyer.login(buyer_email, demo_password, expected=401)
    assert len(staff.call("GET", f"{stock_api}/movements")) >= 3
    print("OK account blocking and inventory history")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://127.0.0.1:5000")
    args = parser.parse_args()
    run(args.url, os.environ["Bootstrap__AdminEmail"], os.environ["Bootstrap__AdminPassword"])
