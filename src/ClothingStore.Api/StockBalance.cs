using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Api;

public sealed class StockBalance(StoreDb db)
{
    private IQueryable<Stock> At(int locationId, int variantId) =>
        db.Stocks.Where(x => x.LocationId == locationId && x.VariantId == variantId);

    public async Task Reserve(int locationId, int variantId, int quantity, CancellationToken ct)
    {
        var changed = await At(locationId, variantId).Where(x => x.OnHand - x.Reserved >= quantity)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Reserved, x => x.Reserved + quantity), ct);
        if (changed == 0)
            throw StoreException.Conflict($"В выбранной точке недоступно требуемое количество варианта {variantId}");
    }

    public async Task ResolveReservation(int locationId, int variantId, int quantity, bool sold, CancellationToken ct)
    {
        var changed = await At(locationId, variantId).Where(x => x.Reserved >= quantity)
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.Reserved, x => x.Reserved - quantity)
                .SetProperty(x => x.OnHand, x => x.OnHand - (sold ? quantity : 0)), ct);
        if (changed == 0)
            throw StoreException.Conflict($"Резерв варианта {variantId} недоступен");
    }

    public async Task RemoveAvailable(int locationId, int variantId, int quantity, CancellationToken ct)
    {
        var changed = await At(locationId, variantId).Where(x => x.OnHand - x.Reserved >= quantity)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.OnHand, x => x.OnHand - quantity), ct);
        if (changed == 0)
            throw StoreException.Conflict($"Недостаточно свободного остатка варианта {variantId} для перемещения");
    }

    public Task Receive(int locationId, int variantId, int quantity, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Stocks" ("VariantId", "LocationId", "OnHand", "Reserved")
            VALUES ({variantId}, {locationId}, {quantity}, 0)
            ON CONFLICT ("VariantId", "LocationId")
            DO UPDATE SET "OnHand" = "Stocks"."OnHand" + EXCLUDED."OnHand"
            """, ct);
}
