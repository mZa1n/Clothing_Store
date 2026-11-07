using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Api;

public sealed class PageQuery
{
    [Range(1, 100000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    public int Offset => (Page - 1) * PageSize;
}

public sealed class CatalogQuery
{
    [MaxLength(120)] public string? Search { get; init; }
    public int? CategoryId { get; init; }
    public int? ManufacturerId { get; init; }
    [MaxLength(24)] public string? Size { get; init; }
    [MaxLength(60)] public string? Color { get; init; }
}

public sealed record ProductSummary(
    int Id, string Name, string? ImageUrl, decimal Price, string Category,
    string Manufacturer, string? Brand, bool Active);

public static class CatalogQueries
{
    public static IQueryable<Product> Published(this IQueryable<Product> products) =>
        products.Where(p => p.Active && p.Manufacturer.Active && p.SizeGrid.Active);

    public static IQueryable<ProductSummary> Summaries(this IQueryable<Product> products) =>
        products.Select(p => new ProductSummary(
            p.Id, p.Name, p.ImageUrl, p.Price, p.Category.Name, p.Manufacturer.Name,
            p.Brand == null ? null : p.Brand.Name, p.Active));

    public static async Task<object?> Detail(StoreDb db, int id, bool staff, CancellationToken ct)
    {
        var products = staff ? db.Products.AsQueryable() : db.Products.Published();
        return await products.Where(p => p.Id == id).Select(p => new
        {
            p.Id, p.Name, p.Description, p.ImageUrl, p.Price, p.Active,
            category = new { p.Category.Id, p.Category.Name },
            manufacturer = new { p.Manufacturer.Id, p.Manufacturer.Name, p.Manufacturer.Country },
            brand = p.Brand == null ? null : new { p.Brand.Id, p.Brand.Name },
            sizeGrid = new
            {
                p.SizeGrid.Id, p.SizeGrid.Name,
                sizes = p.SizeGrid.Sizes.OrderBy(s => s.Id).Select(s => new { s.Id, s.Label, s.Measurements })
            },
            variants = p.Variants.Where(v => staff || v.Active).OrderBy(v => v.Id).Select(v => new
            {
                v.Id, v.Sku, v.Color, v.Active, v.GridSizeId,
                size = v.Size.Label,
                availableAt = v.Stocks.Where(s => s.OnHand > s.Reserved).OrderBy(s => s.LocationId)
                    .Select(s => s.LocationId)
            })
        }).SingleOrDefaultAsync(ct);
    }
}

[ApiController]
[Route("api/catalog")]
public sealed class CatalogController(StoreDb db) : ControllerBase
{
    [HttpGet("products")]
    public async Task<IReadOnlyList<ProductSummary>> Products(
        [FromQuery] CatalogQuery filter, [FromQuery] PageQuery page, CancellationToken ct)
    {
        var products = db.Products.Published();
        if (filter.CategoryId is int categoryId)
            products = products.Where(p => p.CategoryId == categoryId);
        if (filter.ManufacturerId is int manufacturerId)
            products = products.Where(p => p.ManufacturerId == manufacturerId);

        var search = filter.Search?.Trim();
        var pattern = search is { Length: > 0 }
            ? "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"
            : null;
        var size = filter.Size?.Trim().ToUpperInvariant();
        var color = filter.Color?.Trim().ToLowerInvariant();
        products = products.Where(p => p.Variants.Any(v =>
            v.Active && (size == null || v.Size.Label == size) && (color == null || v.Color == color)
            && (pattern == null || EF.Functions.ILike(p.Name, pattern, "\\")
                                || EF.Functions.ILike(v.Sku, pattern, "\\"))));

        return await products.OrderBy(p => p.Id).Skip(page.Offset).Take(page.PageSize).Summaries().ToListAsync(ct);
    }

    [HttpGet("products/{id:int}")]
    public async Task<IActionResult> Product(int id, CancellationToken ct) =>
        await CatalogQueries.Detail(db, id, staff: false, ct) is { } product ? Ok(product) : NotFound();

    [HttpGet("filters")]
    public async Task<IActionResult> Filters(CancellationToken ct) => Ok(new
    {
        categories = await db.Categories.OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct),
        manufacturers = await db.Manufacturers.Where(x => x.Active).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name }).ToListAsync(ct),
        sizes = await db.GridSizes.Where(x => db.SizeGrids.Any(g => g.Id == x.SizeGridId && g.Active))
            .Select(x => x.Label).Distinct().OrderBy(x => x).ToListAsync(ct),
        colors = await db.Variants.Where(v => v.Active && v.Product.Active
            && v.Product.Manufacturer.Active && v.Product.SizeGrid.Active)
            .Select(v => v.Color).Distinct().OrderBy(x => x).ToListAsync(ct)
    });

    [HttpGet("locations")]
    public async Task<IActionResult> Locations(CancellationToken ct) =>
        Ok(await db.Locations.OrderBy(x => x.Id).ToListAsync(ct));
}

public sealed record NameRequest([Required, MaxLength(120)] string Name);
public sealed record ManufacturerRequest(
    [Required, MaxLength(120)] string Name, [Required, MaxLength(80)] string Country, bool Active = true);
public sealed record GridRequest([Required, MaxLength(120)] string Name, bool Active = true);
public sealed record SizeRequest(
    [Required, MaxLength(24)] string Label, [Required, MaxLength(240)] string Measurements);

public sealed record ProductRequest(
    [Required, MaxLength(120)] string Name,
    [Required, MaxLength(4000)] string Description,
    [MaxLength(2048), Url] string? ImageUrl,
    [Range(typeof(decimal), "0.01", "9999999999.99", ParseLimitsInInvariantCulture = true)] decimal Price,
    [Range(1, int.MaxValue)] int CategoryId,
    [Range(1, int.MaxValue)] int ManufacturerId,
    [Range(1, int.MaxValue)] int SizeGridId,
    [Range(1, int.MaxValue)] int? BrandId = null,
    bool Active = true);

public sealed record VariantRequest(
    [Required, MaxLength(64)] string Sku,
    [Required, MaxLength(60)] string Color,
    [Range(1, int.MaxValue)] int GridSizeId,
    bool Active = true);

[ApiController]
[Authorize(Roles = AccessRoles.Staff)]
[Route("api/management/catalog")]
public sealed class ManageCatalogController(StoreDb db) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<IActionResult> Categories(CancellationToken ct) =>
        Ok(await db.Categories.OrderBy(x => x.Name).ToListAsync(ct));

    [HttpPost("categories")]
    public Task<IActionResult> AddCategory(NameRequest request, CancellationToken ct) =>
        Save(new Category { Name = request.Name.Trim() }, true, ct);

    [HttpPut("categories/{id:int}")]
    public async Task<IActionResult> EditCategory(int id, NameRequest request, CancellationToken ct)
    {
        var category = await db.Categories.FindAsync([id], ct);
        if (category is null) return NotFound();
        category.Name = request.Name.Trim();
        return await Save(category, false, ct);
    }

    [HttpGet("brands")]
    public async Task<IActionResult> Brands(CancellationToken ct) =>
        Ok(await db.Brands.OrderBy(x => x.Name).ToListAsync(ct));

    [HttpPost("brands")]
    public Task<IActionResult> AddBrand(NameRequest request, CancellationToken ct) =>
        Save(new Brand { Name = request.Name.Trim() }, true, ct);

    [HttpPut("brands/{id:int}")]
    public async Task<IActionResult> EditBrand(int id, NameRequest request, CancellationToken ct)
    {
        var brand = await db.Brands.FindAsync([id], ct);
        if (brand is null) return NotFound();
        brand.Name = request.Name.Trim();
        return await Save(brand, false, ct);
    }

    [HttpGet("manufacturers")]
    public async Task<IActionResult> Manufacturers(CancellationToken ct) =>
        Ok(await db.Manufacturers.OrderBy(x => x.Name).ToListAsync(ct));

    [HttpPost("manufacturers")]
    public Task<IActionResult> AddManufacturer(ManufacturerRequest request, CancellationToken ct) =>
        SaveManufacturer(new Manufacturer { Name = "", Country = "" }, request, true, ct);

    [HttpPut("manufacturers/{id:int}")]
    public async Task<IActionResult> EditManufacturer(int id, ManufacturerRequest request, CancellationToken ct) =>
        await db.Manufacturers.FindAsync([id], ct) is { } manufacturer
            ? await SaveManufacturer(manufacturer, request, false, ct) : NotFound();

    [HttpGet("size-grids")]
    public async Task<IActionResult> SizeGrids(CancellationToken ct) =>
        Ok(await db.SizeGrids.OrderBy(x => x.Name).Include(x => x.Sizes.OrderBy(s => s.Id)).ToListAsync(ct));

    [HttpPost("size-grids")]
    public Task<IActionResult> AddGrid(GridRequest request, CancellationToken ct) =>
        Save(new SizeGrid { Name = request.Name.Trim(), Active = request.Active }, true, ct);

    [HttpPut("size-grids/{id:int}")]
    public async Task<IActionResult> EditGrid(int id, GridRequest request, CancellationToken ct)
    {
        var grid = await db.SizeGrids.FindAsync([id], ct);
        if (grid is null) return NotFound();
        grid.Name = request.Name.Trim();
        grid.Active = request.Active;
        return await Save(grid, false, ct);
    }

    [HttpPost("size-grids/{gridId:int}/sizes")]
    public async Task<IActionResult> AddSize(int gridId, SizeRequest request, CancellationToken ct)
    {
        if (!await db.SizeGrids.AnyAsync(x => x.Id == gridId, ct)) return NotFound();
        return await Save(new GridSize
        {
            SizeGridId = gridId, Label = request.Label.Trim().ToUpperInvariant(),
            Measurements = request.Measurements.Trim()
        }, true, ct);
    }

    [HttpPut("size-grids/{gridId:int}/sizes/{id:int}")]
    public async Task<IActionResult> EditSize(int gridId, int id, SizeRequest request, CancellationToken ct)
    {
        var size = await db.GridSizes.SingleOrDefaultAsync(x => x.Id == id && x.SizeGridId == gridId, ct);
        if (size is null) return NotFound();
        size.Label = request.Label.Trim().ToUpperInvariant();
        size.Measurements = request.Measurements.Trim();
        return await Save(size, false, ct);
    }

    [HttpGet("products")]
    public async Task<IActionResult> Products([FromQuery] PageQuery page, CancellationToken ct) =>
        Ok(await db.Products.OrderBy(p => p.Id).Skip(page.Offset).Take(page.PageSize).Summaries().ToListAsync(ct));

    [HttpGet("products/{id:int}")]
    public async Task<IActionResult> Product(int id, CancellationToken ct) =>
        await CatalogQueries.Detail(db, id, staff: true, ct) is { } product ? Ok(product) : NotFound();

    [HttpPost("products")]
    public Task<IActionResult> AddProduct(ProductRequest request, CancellationToken ct) =>
        SaveProduct(new Product { Name = "", Description = "", SizeGridId = request.SizeGridId }, request, true, ct);

    [HttpPut("products/{id:int}")]
    public async Task<IActionResult> EditProduct(int id, ProductRequest request, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([id], ct);
        if (product is null) return NotFound();
        if (product.SizeGridId != request.SizeGridId)
            return Problem(statusCode: 409, title: "Размерная сетка существующей модели закреплена за её вариантами");
        return await SaveProduct(product, request, false, ct);
    }

    [HttpPost("products/{productId:int}/variants")]
    public async Task<IActionResult> AddVariant(int productId, VariantRequest request, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([productId], ct);
        if (product is null) return NotFound();
        var variant = new Variant { ProductId = product.Id, SizeGridId = product.SizeGridId, Sku = "", Color = "" };
        return await SaveVariant(variant, request, true, ct);
    }

    [HttpPut("products/{productId:int}/variants/{id:int}")]
    public async Task<IActionResult> EditVariant(int productId, int id, VariantRequest request, CancellationToken ct)
    {
        var variant = await db.Variants.SingleOrDefaultAsync(v => v.Id == id && v.ProductId == productId, ct);
        if (variant is null) return NotFound();
        if (request.GridSizeId != variant.GridSizeId
            && await db.Stocks.AnyAsync(s => s.VariantId == id, ct))
            return Problem(statusCode: 409, title: "Для учтённого товара создайте отдельный вариант другого размера");
        return await SaveVariant(variant, request, false, ct);
    }

    private Task<IActionResult> SaveManufacturer(
        Manufacturer manufacturer, ManufacturerRequest request, bool create, CancellationToken ct)
    {
        manufacturer.Name = request.Name.Trim();
        manufacturer.Country = request.Country.Trim();
        manufacturer.Active = request.Active;
        return Save(manufacturer, create, ct);
    }

    private async Task<IActionResult> SaveProduct(Product product, ProductRequest request, bool create, CancellationToken ct)
    {
        if (decimal.Round(request.Price, 2) != request.Price)
            ModelState.AddModelError(nameof(request.Price), "Укажите цену с точностью до копейки");
        if (!await db.Categories.AnyAsync(x => x.Id == request.CategoryId, ct))
            ModelState.AddModelError(nameof(request.CategoryId), "Категория не найдена");
        if (!await db.Manufacturers.AnyAsync(x => x.Id == request.ManufacturerId, ct))
            ModelState.AddModelError(nameof(request.ManufacturerId), "Производитель не найден");
        if (!await db.SizeGrids.AnyAsync(x => x.Id == request.SizeGridId, ct))
            ModelState.AddModelError(nameof(request.SizeGridId), "Размерная сетка не найдена");
        if (request.BrandId is int brandId && !await db.Brands.AnyAsync(x => x.Id == brandId, ct))
            ModelState.AddModelError(nameof(request.BrandId), "Бренд не найден");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        product.Name = request.Name.Trim();
        product.Description = request.Description.Trim();
        product.ImageUrl = request.ImageUrl?.Trim();
        product.Price = request.Price;
        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.ManufacturerId = request.ManufacturerId;
        product.Active = request.Active;
        if (create) db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        var detail = await CatalogQueries.Detail(db, product.Id, staff: true, ct);
        return create ? CreatedAtAction(nameof(Product), new { id = product.Id }, detail) : Ok(detail);
    }

    private async Task<IActionResult> SaveVariant(Variant variant, VariantRequest request, bool create, CancellationToken ct)
    {
        if (!await db.GridSizes.AnyAsync(s => s.Id == request.GridSizeId && s.SizeGridId == variant.SizeGridId, ct))
        {
            ModelState.AddModelError(nameof(request.GridSizeId), "Укажите размер из сетки модели");
            return ValidationProblem(ModelState);
        }

        variant.Sku = request.Sku.Trim().ToUpperInvariant();
        variant.Color = request.Color.Trim().ToLowerInvariant();
        variant.GridSizeId = request.GridSizeId;
        variant.Active = request.Active;
        if (create) db.Variants.Add(variant);
        await db.SaveChangesAsync(ct);
        var result = new { variant.Id, variant.Sku, variant.Color, variant.GridSizeId, variant.Active };
        return create ? StatusCode(201, result) : Ok(result);
    }

    private async Task<IActionResult> Save<TEntity>(TEntity entity, bool create, CancellationToken ct) where TEntity : class
    {
        if (create) db.Add(entity);
        await db.SaveChangesAsync(ct);
        return StatusCode(create ? 201 : 200, entity);
    }
}
