using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Api;

public sealed record LocationRequest(
    [Required, EnumDataType(typeof(LocationKind))] LocationKind? Kind,
    [Required, MaxLength(120)] string Name,
    [Required, MaxLength(240)] string Address);

public sealed record ReceiptRequest(
    [Range(1, int.MaxValue)] int VariantId,
    [Range(1, int.MaxValue)] int WarehouseId,
    [Range(1, 1_000_000)] int Quantity);

public sealed record TransferRequest(
    [Range(1, int.MaxValue)] int VariantId,
    [Range(1, int.MaxValue)] int SourceId,
    [Range(1, int.MaxValue)] int DestinationId,
    [Range(1, 1_000_000)] int Quantity);

public sealed class InventoryQuery
{
    public int? VariantId { get; init; }
    public int? LocationId { get; init; }
    public int? ManufacturerId { get; init; }
    public int? CategoryId { get; init; }
    [MaxLength(60)] public string? Color { get; init; }
    [MaxLength(24)] public string? Size { get; init; }
}

public sealed record StockView(
    int VariantId, int LocationId, string Product, string Sku, string Size, string Color,
    string Location, LocationKind LocationKind, int OnHand, int Reserved, int Available);

[ApiController]
[Authorize(Roles = AccessRoles.Staff)]
[Route("api/management/inventory")]
public sealed class InventoryController(StoreDb db, StockBalance balances, TimeProvider time) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<StockView>> Get(
        [FromQuery] InventoryQuery filter, [FromQuery] PageQuery page, CancellationToken ct)
    {
        var stocks = db.Stocks.AsQueryable();
        if (filter.VariantId is int variantId) stocks = stocks.Where(s => s.VariantId == variantId);
        if (filter.LocationId is int locationId) stocks = stocks.Where(s => s.LocationId == locationId);
        if (filter.ManufacturerId is int manufacturerId)
            stocks = stocks.Where(s => s.Variant.Product.ManufacturerId == manufacturerId);
        if (filter.CategoryId is int categoryId)
            stocks = stocks.Where(s => s.Variant.Product.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(filter.Color))
        {
            var color = filter.Color.Trim().ToLowerInvariant();
            stocks = stocks.Where(s => s.Variant.Color == color);
        }
        if (!string.IsNullOrWhiteSpace(filter.Size))
        {
            var size = filter.Size.Trim().ToUpperInvariant();
            stocks = stocks.Where(s => s.Variant.Size.Label == size);
        }

        return await stocks.OrderBy(s => s.Variant.Product.Name).ThenBy(s => s.VariantId).ThenBy(s => s.LocationId)
            .Skip(page.Offset).Take(page.PageSize)
            .Select(s => new StockView(s.VariantId, s.LocationId, s.Variant.Product.Name, s.Variant.Sku,
                s.Variant.Size.Label, s.Variant.Color, s.Location.Name, s.Location.Kind,
                s.OnHand, s.Reserved, s.OnHand - s.Reserved)).ToListAsync(ct);
    }

    [HttpGet("movements")]
    public async Task<IReadOnlyList<StockMovement>> Movements([FromQuery] PageQuery page, CancellationToken ct) =>
        await db.Movements.OrderByDescending(x => x.Id).Skip(page.Offset).Take(page.PageSize).ToListAsync(ct);

    [HttpPost("locations")]
    public async Task<ActionResult<StockLocation>> AddLocation(LocationRequest request, CancellationToken ct)
    {
        var location = new StockLocation
        {
            Kind = request.Kind!.Value,
            Name = request.Name.Trim(),
            Address = request.Address.Trim()
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync(ct);
        return StatusCode(201, location);
    }

    [HttpPost("receipts")]
    public async Task<ActionResult<StockMovement>> Receive(ReceiptRequest request, CancellationToken ct)
    {
        if (!await db.Locations.AnyAsync(x => x.Id == request.WarehouseId && x.Kind == LocationKind.Warehouse, ct))
            ModelState.AddModelError(nameof(request.WarehouseId), "Укажите существующий склад");
        if (!await db.Variants.AnyAsync(x => x.Id == request.VariantId, ct))
            ModelState.AddModelError(nameof(request.VariantId), "Вариант не найден");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await balances.Receive(request.WarehouseId, request.VariantId, request.Quantity, ct);
        var movement = await RecordMovement(
            MovementKind.Receipt, request.VariantId, request.Quantity, null, request.WarehouseId, ct);
        await tx.CommitAsync(ct);
        return StatusCode(201, movement);
    }

    [HttpPost("transfers")]
    public async Task<ActionResult<StockMovement>> Transfer(TransferRequest request, CancellationToken ct)
    {
        var points = await db.Locations.Where(x => x.Id == request.SourceId || x.Id == request.DestinationId)
            .ToDictionaryAsync(x => x.Id, x => x.Kind, ct);
        if (!points.TryGetValue(request.SourceId, out var source))
            ModelState.AddModelError(nameof(request.SourceId), "Источник не найден");
        if (!points.TryGetValue(request.DestinationId, out var destination))
            ModelState.AddModelError(nameof(request.DestinationId), "Точка назначения не найдена");
        if (ModelState.IsValid && source == destination)
            ModelState.AddModelError(nameof(request.DestinationId), "Перемещение возможно между складом и магазином");
        if (!await db.Variants.AnyAsync(x => x.Id == request.VariantId, ct))
            ModelState.AddModelError(nameof(request.VariantId), "Вариант не найден");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // For transfers of the same variant, acquire both row locks in location order.
        if (request.SourceId < request.DestinationId)
        {
            await balances.RemoveAvailable(request.SourceId, request.VariantId, request.Quantity, ct);
            await balances.Receive(request.DestinationId, request.VariantId, request.Quantity, ct);
        }
        else
        {
            await balances.Receive(request.DestinationId, request.VariantId, request.Quantity, ct);
            await balances.RemoveAvailable(request.SourceId, request.VariantId, request.Quantity, ct);
        }
        var movement = await RecordMovement(
            MovementKind.Transfer, request.VariantId, request.Quantity, request.SourceId, request.DestinationId, ct);
        await tx.CommitAsync(ct);
        return StatusCode(201, movement);
    }

    private async Task<StockMovement> RecordMovement(
        MovementKind kind, int variantId, int quantity, int? sourceId, int destinationId, CancellationToken ct)
    {
        var movement = new StockMovement
        {
            Kind = kind, VariantId = variantId, Quantity = quantity, SourceId = sourceId,
            DestinationId = destinationId, UserId = User.UserId(), CreatedAt = time.GetUtcNow()
        };
        db.Movements.Add(movement);
        await db.SaveChangesAsync(ct);
        return movement;
    }
}
