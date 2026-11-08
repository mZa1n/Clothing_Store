using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Api;

public sealed record CheckoutRequest(
    [Required, EnumDataType(typeof(Fulfillment))] Fulfillment? Fulfillment,
    [Required, MaxLength(120)] string FullName,
    [Required, Phone, MaxLength(30)] string Phone,
    [MaxLength(240)] string? Address,
    [Range(1, int.MaxValue)] int? PickupLocationId);

public sealed record OrderSummary(
    long Id, OrderStatus Status, Fulfillment Fulfillment, string Location,
    decimal Total, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
public sealed record LocationView(int Id, string Name, string Address, LocationKind Kind);
public sealed record OrderItem(
    int VariantId, string ProductName, string Sku, string Size, string Color, int Quantity, decimal UnitPrice);
public sealed record OrderView(
    long Id, OrderStatus Status, Fulfillment Fulfillment, LocationView Location, string FullName,
    string Phone, string? Address, decimal Total, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, IReadOnlyList<OrderItem> Items);

public sealed class OrderService(StoreDb db, StockBalance balances, TimeProvider time)
{
    public async Task<(long Id, bool Created)> Checkout(
        int userId, Guid key, CheckoutRequest request, CancellationToken ct)
    {
        var fulfillment = request.Fulfillment!.Value;
        var name = request.FullName.Trim();
        var phone = request.Phone.Trim();
        var address = request.Address?.Trim();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CartQueries.Lock(db, userId, ct);

        var previous = await db.Orders.AsNoTracking()
            .SingleOrDefaultAsync(o => o.UserId == userId && o.CheckoutKey == key, ct);
        if (previous is not null)
        {
            if (previous.Fulfillment != fulfillment || previous.FullName != name || previous.Phone != phone
                || previous.Address != address
                || fulfillment == Fulfillment.Pickup && previous.LocationId != request.PickupLocationId)
                throw StoreException.Conflict("Этот ключ уже использован для другого оформления");
            return (previous.Id, false);
        }

        var kind = fulfillment == Fulfillment.Pickup ? LocationKind.Store : LocationKind.Warehouse;
        var locations = db.Locations.Where(l => l.Kind == kind);
        if (request.PickupLocationId is int pickupId)
            locations = locations.Where(l => l.Id == pickupId);
        var location = await locations.OrderBy(l => l.Id).FirstOrDefaultAsync(ct);
        if (location is null)
            throw StoreException.Conflict(fulfillment == Fulfillment.Pickup
                ? "Выбранный магазин самовывоза недоступен" : "Склад для доставки не настроен");

        var cart = await CartQueries.View(db, userId, ct);
        if (cart.Items.Count == 0)
            throw StoreException.Conflict("Корзина пуста");

        var order = new Order
        {
            UserId = userId,
            CheckoutKey = key,
            FullName = name,
            Phone = phone,
            Address = address,
            LocationId = location.Id,
            Fulfillment = fulfillment,
            Status = OrderStatus.Placed,
            CreatedAt = time.GetUtcNow(),
            Total = cart.EstimatedTotal
        };

        // Cart items are ordered by variant ID so multi-line orders acquire stock locks consistently.
        foreach (var item in cart.Items)
        {
            if (!item.Active)
                throw StoreException.Conflict($"Вариант {item.Sku} снят с продажи");
            await balances.Reserve(location.Id, item.VariantId, item.Quantity, ct);
            order.Lines.Add(new OrderLine
            {
                VariantId = item.VariantId, ProductName = item.ProductName, Sku = item.Sku,
                Size = item.Size, Color = item.Color, Quantity = item.Quantity, UnitPrice = item.UnitPrice
            });
        }

        db.Orders.Add(order);
        await db.OfBuyer(userId).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (order.Id, true);
    }

    public async Task Advance(long id, OrderStatus target, int? buyerId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Orders.FromSqlInterpolated($"""
            SELECT * FROM "Orders" WHERE "Id" = {id}
            AND ({buyerId}::integer IS NULL OR "UserId" = {buyerId}) FOR UPDATE
            """).ToListAsync(ct);
        var order = rows.SingleOrDefault() ?? throw StoreException.NotFound("Заказ не найден");
        if (order.Status == target)
            return;
        if (!CanTransition(order.Status, target))
            throw StoreException.Conflict("Заказ нельзя перевести в выбранное состояние");

        if (target is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            var sold = target == OrderStatus.Completed;
            var lines = await db.Set<OrderLine>().Where(l => l.OrderId == id).OrderBy(l => l.VariantId)
                .Select(l => new { l.VariantId, l.Quantity }).ToListAsync(ct);
            foreach (var line in lines)
                await balances.ResolveReservation(order.LocationId, line.VariantId, line.Quantity, sold, ct);

            if (sold)
                db.Sales.Add(new Sale { OrderId = id, CompletedAt = time.GetUtcNow() });
        }

        order.Status = target;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static bool CanTransition(OrderStatus current, OrderStatus target) => (current, target) switch
    {
        (OrderStatus.Placed, OrderStatus.Confirmed) => true,
        (OrderStatus.Confirmed, OrderStatus.Prepared) => true,
        (OrderStatus.Prepared, OrderStatus.HandedOver) => true,
        (OrderStatus.HandedOver, OrderStatus.Completed) => true,
        (OrderStatus.Placed or OrderStatus.Confirmed or OrderStatus.Prepared, OrderStatus.Cancelled) => true,
        _ => false
    };

    public Task<OrderView?> Detail(long id, int? buyerId, CancellationToken ct) =>
        db.Orders.Where(o => o.Id == id && (!buyerId.HasValue || o.UserId == buyerId.Value))
            .Select(o => new OrderView(o.Id, o.Status, o.Fulfillment,
                new LocationView(o.Location.Id, o.Location.Name, o.Location.Address, o.Location.Kind),
                o.FullName, o.Phone, o.Address, o.Total, o.CreatedAt,
                o.Sale == null ? null : o.Sale.CompletedAt,
                o.Lines.OrderBy(l => l.VariantId).Select(l => new OrderItem(l.VariantId, l.ProductName,
                    l.Sku, l.Size, l.Color, l.Quantity, l.UnitPrice)).ToList()))
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<OrderSummary>> List(
        int? buyerId, OrderStatus? status, PageQuery page, CancellationToken ct)
    {
        var query = db.Orders.AsQueryable();
        if (buyerId.HasValue) query = query.Where(o => o.UserId == buyerId.Value);
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        return await query.OrderByDescending(o => o.Id).Skip(page.Offset).Take(page.PageSize)
            .Select(o => new OrderSummary(o.Id, o.Status, o.Fulfillment, o.Location.Name, o.Total, o.CreatedAt,
                o.Sale == null ? null : o.Sale.CompletedAt)).ToListAsync(ct);
    }
}

[ApiController]
[Authorize(Roles = AccessRoles.Customer)]
[Route("api/orders")]
public sealed class OrdersController(OrderService orders) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<OrderSummary>> List([FromQuery] PageQuery page, CancellationToken ct) =>
        orders.List(User.UserId(), null, page, ct);

    [HttpGet("{id:long}")]
    public async Task<ActionResult<OrderView>> Get(long id, CancellationToken ct) =>
        await orders.Detail(id, User.UserId(), ct) is { } order ? Ok(order) : NotFound();

    [HttpPost("checkout")]
    public async Task<ActionResult<OrderView>> Checkout(
        [FromHeader(Name = "Idempotency-Key")] Guid? key, CheckoutRequest request, CancellationToken ct)
    {
        if (key is null || key == Guid.Empty)
            ModelState.AddModelError("Idempotency-Key", "Передайте UUID операции");
        if (request.Fulfillment == Fulfillment.Delivery)
        {
            if (string.IsNullOrWhiteSpace(request.Address))
                ModelState.AddModelError(nameof(request.Address), "Укажите адрес доставки");
            if (request.PickupLocationId.HasValue)
                ModelState.AddModelError(nameof(request.PickupLocationId), "Для доставки используется склад");
        }
        if (request.Fulfillment == Fulfillment.Pickup)
        {
            if (!request.PickupLocationId.HasValue)
                ModelState.AddModelError(nameof(request.PickupLocationId), "Укажите магазин самовывоза");
            if (request.Address is not null)
                ModelState.AddModelError(nameof(request.Address), "Для самовывоза адрес не требуется");
        }
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var (id, created) = await orders.Checkout(User.UserId(), key!.Value, request, ct);
        var order = await orders.Detail(id, User.UserId(), ct);
        return created ? CreatedAtAction(nameof(Get), new { id }, order) : Ok(order);
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<ActionResult<OrderView>> Cancel(long id, CancellationToken ct)
    {
        await orders.Advance(id, OrderStatus.Cancelled, User.UserId(), ct);
        return Ok(await orders.Detail(id, User.UserId(), ct));
    }
}

[ApiController]
[Authorize(Roles = AccessRoles.Staff)]
[Route("api/management/orders")]
public sealed class ManageOrdersController(OrderService orders) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<OrderSummary>> List(
        [FromQuery, EnumDataType(typeof(OrderStatus))] OrderStatus? status, [FromQuery] PageQuery page,
        CancellationToken ct) => orders.List(null, status, page, ct);

    [HttpGet("{id:long}")]
    public async Task<ActionResult<OrderView>> Get(long id, CancellationToken ct) =>
        await orders.Detail(id, null, ct) is { } order ? Ok(order) : NotFound();

    [HttpPost("{id:long}/confirm")]
    public Task<ActionResult<OrderView>> Confirm(long id, CancellationToken ct) => Change(id, OrderStatus.Confirmed, ct);

    [HttpPost("{id:long}/prepare")]
    public Task<ActionResult<OrderView>> Prepare(long id, CancellationToken ct) => Change(id, OrderStatus.Prepared, ct);

    [HttpPost("{id:long}/handover")]
    public Task<ActionResult<OrderView>> HandOver(long id, CancellationToken ct) => Change(id, OrderStatus.HandedOver, ct);

    [HttpPost("{id:long}/complete")]
    public Task<ActionResult<OrderView>> Complete(long id, CancellationToken ct) => Change(id, OrderStatus.Completed, ct);

    private async Task<ActionResult<OrderView>> Change(long id, OrderStatus target, CancellationToken ct)
    {
        await orders.Advance(id, target, null, ct);
        return Ok(await orders.Detail(id, null, ct));
    }
}
