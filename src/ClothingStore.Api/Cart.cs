using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Api;

public sealed record QuantityRequest([Range(1, 1_000_000)] int Quantity);
public sealed record CartItem(
    int VariantId, string ProductName, string Sku, string Size, string Color,
    int Quantity, decimal UnitPrice, bool Active);
public sealed record CartView(IReadOnlyList<CartItem> Items, decimal EstimatedTotal);

public static class CartQueries
{
    public const int LineLimit = 100;

    public static IQueryable<CartLine> OfBuyer(this StoreDb db, int userId) =>
        db.CartLines.Where(line => line.UserId == userId);

    public static async Task<CartView> View(StoreDb db, int userId, CancellationToken ct)
    {
        var items = await db.OfBuyer(userId).OrderBy(l => l.VariantId).Select(l => new CartItem(
            l.VariantId, l.Variant.Product.Name, l.Variant.Sku, l.Variant.Size.Label, l.Variant.Color,
            l.Quantity, l.Variant.Product.Price, l.Variant.Active && l.Variant.Product.Active
                && l.Variant.Product.Manufacturer.Active && l.Variant.Product.SizeGrid.Active)).ToListAsync(ct);
        return new CartView(items, items.Sum(item => item.Quantity * item.UnitPrice));
    }

    // The buyer row exists even when the cart is empty, so edits and checkout use the same lock.
    public static Task Lock(StoreDb db, int userId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM "AspNetUsers" WHERE "Id" = {userId} FOR UPDATE
            """, ct);
}

[ApiController]
[Authorize(Roles = AccessRoles.Customer)]
[Route("api/cart")]
public sealed class CartController(StoreDb db) : ControllerBase
{
    [HttpGet]
    public Task<CartView> Get(CancellationToken ct) => CartQueries.View(db, User.UserId(), ct);

    [HttpPut("items/{variantId:int}")]
    public async Task<ActionResult<CartView>> Put(int variantId, QuantityRequest request, CancellationToken ct)
    {
        var userId = User.UserId();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CartQueries.Lock(db, userId, ct);

        var variant = await db.Variants.Where(v => v.Id == variantId).Select(v => new
        {
            Active = v.Active && v.Product.Active && v.Product.Manufacturer.Active && v.Product.SizeGrid.Active,
            Available = v.Stocks.Any(s => s.OnHand > s.Reserved)
        }).SingleOrDefaultAsync(ct);
        if (variant is null || !variant.Active)
            return NotFound();
        if (!variant.Available)
            throw StoreException.Conflict("Вариант сейчас недоступен");

        var line = await db.CartLines.FindAsync([userId, variantId], ct);
        if (line is null)
        {
            if (await db.OfBuyer(userId).CountAsync(ct) >= CartQueries.LineLimit)
                throw StoreException.Conflict("Достигнут предел позиций в корзине");
            db.CartLines.Add(new CartLine { UserId = userId, VariantId = variantId, Quantity = request.Quantity });
        }
        else
        {
            line.Quantity = request.Quantity;
        }

        await db.SaveChangesAsync(ct);
        var cart = await CartQueries.View(db, userId, ct);
        await tx.CommitAsync(ct);
        return cart;
    }

    [HttpDelete("items/{variantId:int}")]
    public async Task<IActionResult> Delete(int variantId, CancellationToken ct)
    {
        var userId = User.UserId();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CartQueries.Lock(db, userId, ct);
        await db.OfBuyer(userId).Where(l => l.VariantId == variantId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }
}
