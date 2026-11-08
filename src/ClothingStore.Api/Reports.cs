using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Api;

public sealed class ReportPeriod : IValidatableObject
{
    [Required] public DateTimeOffset? From { get; init; }
    [Required] public DateTimeOffset? To { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From >= To)
            yield return new ValidationResult("Конец периода должен быть позже начала", [nameof(To)]);
    }

    public IQueryable<Sale> Filter(StoreDb db)
    {
        var start = From!.Value.ToUniversalTime();
        var end = To!.Value.ToUniversalTime();
        return db.Sales.Where(s => s.CompletedAt >= start && s.CompletedAt < end);
    }
}

public sealed record SalesSummary(long Orders, decimal Revenue, decimal AverageOrder)
{
    public static SalesSummary FromTotals(long orders, decimal revenue) =>
        new(orders, revenue, orders == 0 ? 0 : decimal.Round(revenue / orders, 2, MidpointRounding.AwayFromZero));
}

public sealed record SalesByLocation(LocationView Location, SalesSummary Sales);
public sealed record SalesComparison(SalesSummary Total, IReadOnlyList<SalesByLocation> Locations);

[ApiController]
[Authorize(Roles = AccessRoles.Staff)]
[Route("api/management/reports")]
public sealed class StaffReportsController(StoreDb db) : ControllerBase
{
    [HttpGet("sales")]
    public async Task<ActionResult<SalesSummary>> Summary(
        [FromQuery] ReportPeriod period, [FromQuery, Range(1, int.MaxValue)] int? locationId, CancellationToken ct)
    {
        var sales = period.Filter(db);
        if (locationId.HasValue)
        {
            if (!await db.Locations.AnyAsync(x => x.Id == locationId.Value, ct)) return NotFound();
            sales = sales.Where(s => s.Order.LocationId == locationId.Value);
        }

        var totals = await sales.GroupBy(s => 1)
            .Select(g => new { Orders = g.LongCount(), Revenue = g.Sum(s => s.Order.Total) })
            .SingleOrDefaultAsync(ct);
        return SalesSummary.FromTotals(totals?.Orders ?? 0, totals?.Revenue ?? 0);
    }
}

[ApiController]
[Authorize(Roles = AccessRoles.Admin)]
[Route("api/admin/reports")]
public sealed class AdminReportsController(StoreDb db) : ControllerBase
{
    [HttpGet("sales")]
    public async Task<SalesComparison> Compare([FromQuery] ReportPeriod period, CancellationToken ct)
    {
        var locations = await db.Locations.OrderBy(l => l.Kind).ThenBy(l => l.Id)
            .Select(l => new LocationView(l.Id, l.Name, l.Address, l.Kind)).ToListAsync(ct);
        var totals = await period.Filter(db).GroupBy(s => s.Order.LocationId)
            .Select(g => new { LocationId = g.Key, Orders = g.LongCount(), Revenue = g.Sum(s => s.Order.Total) })
            .ToDictionaryAsync(x => x.LocationId, ct);
        var rows = locations.Select(l =>
        {
            var total = totals.GetValueOrDefault(l.Id);
            return new SalesByLocation(l, SalesSummary.FromTotals(total?.Orders ?? 0, total?.Revenue ?? 0));
        }).ToList();

        return new SalesComparison(
            SalesSummary.FromTotals(rows.Sum(x => x.Sales.Orders), rows.Sum(x => x.Sales.Revenue)), rows);
    }
}
