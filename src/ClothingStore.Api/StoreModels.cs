using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Api;

public enum LocationKind { Warehouse, Store }
public enum Fulfillment { Delivery, Pickup }
public enum OrderStatus { Placed, Confirmed, Prepared, HandedOver, Completed, Cancelled }
public enum MovementKind { Receipt, Transfer }

public sealed class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

public sealed class Brand
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

public sealed class Manufacturer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Country { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class SizeGrid
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; } = true;
    public List<GridSize> Sizes { get; set; } = [];
}

public sealed class GridSize
{
    public int Id { get; set; }
    public int SizeGridId { get; set; }
    public required string Label { get; set; }
    public required string Measurements { get; set; }
}

public sealed class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public int ManufacturerId { get; set; }
    public Manufacturer Manufacturer { get; set; } = null!;
    public int SizeGridId { get; set; }
    public SizeGrid SizeGrid { get; set; } = null!;
    public bool Active { get; set; } = true;
    public List<Variant> Variants { get; set; } = [];
}

public sealed class Variant
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int SizeGridId { get; set; }
    public int GridSizeId { get; set; }
    public GridSize Size { get; set; } = null!;
    public required string Sku { get; set; }
    public required string Color { get; set; }
    public bool Active { get; set; } = true;
    public List<Stock> Stocks { get; set; } = [];
}

public sealed class StockLocation
{
    public int Id { get; set; }
    public LocationKind Kind { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
}

public sealed class Stock
{
    public int VariantId { get; set; }
    public Variant Variant { get; set; } = null!;
    public int LocationId { get; set; }
    public StockLocation Location { get; set; } = null!;
    public int OnHand { get; set; }
    public int Reserved { get; set; }
}

public sealed class CartLine
{
    public int UserId { get; set; }
    public int VariantId { get; set; }
    public Variant Variant { get; set; } = null!;
    public int Quantity { get; set; }
}

public sealed class Order
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public Guid CheckoutKey { get; set; }
    public Fulfillment Fulfillment { get; set; }
    public int LocationId { get; set; }
    public StockLocation Location { get; set; } = null!;
    public required string FullName { get; set; }
    public required string Phone { get; set; }
    public string? Address { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
    public Sale? Sale { get; set; }
}

public sealed class OrderLine
{
    public long OrderId { get; set; }
    public int VariantId { get; set; }
    public required string ProductName { get; set; }
    public required string Sku { get; set; }
    public required string Size { get; set; }
    public required string Color { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class Sale
{
    public long OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public DateTimeOffset CompletedAt { get; set; }
}

public sealed class StockMovement
{
    public long Id { get; set; }
    public MovementKind Kind { get; set; }
    public int VariantId { get; set; }
    public int? SourceId { get; set; }
    public int DestinationId { get; set; }
    public int Quantity { get; set; }
    public int UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed partial class StoreDb
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Manufacturer> Manufacturers => Set<Manufacturer>();
    public DbSet<SizeGrid> SizeGrids => Set<SizeGrid>();
    public DbSet<GridSize> GridSizes => Set<GridSize>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Variant> Variants => Set<Variant>();
    public DbSet<StockLocation> Locations => Set<StockLocation>();
    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<CartLine> CartLines => Set<CartLine>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<StockMovement> Movements => Set<StockMovement>();

    partial void ConfigureStore(ModelBuilder model)
    {
        model.Entity<Category>().HasIndex(x => x.Name).IsUnique();
        model.Entity<Brand>().HasIndex(x => x.Name).IsUnique();
        model.Entity<Manufacturer>().HasIndex(x => x.Name).IsUnique();
        model.Entity<SizeGrid>().HasIndex(x => x.Name).IsUnique();

        model.Entity<GridSize>().HasIndex(x => new { x.SizeGridId, x.Label }).IsUnique();
        model.Entity<GridSize>().HasAlternateKey(x => new { x.Id, x.SizeGridId });
        model.Entity<Product>().HasAlternateKey(x => new { x.Id, x.SizeGridId });
        model.Entity<Product>().Property(x => x.Price).HasPrecision(12, 2);
        model.Entity<Product>().ToTable(t => t.HasCheckConstraint("product_price", """
            "Price" > 0
            """));
        model.Entity<Variant>().HasIndex(x => x.Sku).IsUnique();
        model.Entity<Variant>().HasIndex(x => new { x.ProductId, x.GridSizeId, x.Color }).IsUnique();
        model.Entity<Variant>().HasOne(x => x.Product).WithMany(x => x.Variants)
            .HasForeignKey(x => new { x.ProductId, x.SizeGridId })
            .HasPrincipalKey(x => new { x.Id, x.SizeGridId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Variant>().HasOne(x => x.Size).WithMany()
            .HasForeignKey(x => new { x.GridSizeId, x.SizeGridId })
            .HasPrincipalKey(x => new { x.Id, x.SizeGridId }).OnDelete(DeleteBehavior.Restrict);

        model.Entity<StockLocation>().Property(x => x.Kind).HasConversion<string>();
        model.Entity<StockLocation>().HasIndex(x => new { x.Kind, x.Name }).IsUnique();
        model.Entity<Stock>().HasKey(x => new { x.VariantId, x.LocationId });
        model.Entity<Stock>().ToTable(t => t.HasCheckConstraint("stock_bounds", """
            "OnHand" >= 0 AND "Reserved" >= 0 AND "Reserved" <= "OnHand"
            """));

        model.Entity<CartLine>().HasKey(x => new { x.UserId, x.VariantId });
        model.Entity<CartLine>().HasOne<StoreUser>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<CartLine>().ToTable(t => t.HasCheckConstraint("cart_positive", """
            "Quantity" > 0
            """));

        model.Entity<Order>().HasOne<StoreUser>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<Order>().HasIndex(x => new { x.UserId, x.CheckoutKey }).IsUnique();
        model.Entity<Order>().HasIndex(x => new { x.UserId, x.Id });
        model.Entity<Order>().HasIndex(x => new { x.Status, x.Id });
        model.Entity<Order>().Property(x => x.Total).HasPrecision(20, 2);
        model.Entity<Order>().Property(x => x.Status).HasConversion<string>();
        model.Entity<Order>().Property(x => x.Fulfillment).HasConversion<string>();
        model.Entity<Order>().ToTable(t => t.HasCheckConstraint("order_delivery_address", """
            ("Fulfillment" = 'Delivery' AND "Address" IS NOT NULL) OR
            ("Fulfillment" = 'Pickup' AND "Address" IS NULL)
            """));

        model.Entity<OrderLine>().HasKey(x => new { x.OrderId, x.VariantId });
        model.Entity<OrderLine>().HasOne<Variant>().WithMany().HasForeignKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrderLine>().Property(x => x.UnitPrice).HasPrecision(12, 2);
        model.Entity<OrderLine>().ToTable(t => t.HasCheckConstraint("order_line_positive", """
            "Quantity" > 0 AND "UnitPrice" > 0
            """));

        model.Entity<Sale>().HasKey(x => x.OrderId);
        model.Entity<Sale>().HasOne(x => x.Order).WithOne(x => x.Sale)
            .HasForeignKey<Sale>(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Sale>().HasIndex(x => x.CompletedAt);

        model.Entity<StockMovement>().Property(x => x.Kind).HasConversion<string>();
        model.Entity<StockMovement>().HasOne<StoreUser>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<StockMovement>().HasOne<Variant>().WithMany().HasForeignKey(x => x.VariantId);
        model.Entity<StockMovement>().HasOne<StockLocation>().WithMany().HasForeignKey(x => x.SourceId);
        model.Entity<StockMovement>().HasOne<StockLocation>().WithMany().HasForeignKey(x => x.DestinationId);
        model.Entity<StockMovement>().ToTable(t => t.HasCheckConstraint("movement_valid", """
            "Quantity" > 0 AND (
              ("Kind" = 'Receipt' AND "SourceId" IS NULL) OR
              ("Kind" = 'Transfer' AND "SourceId" IS NOT NULL AND "SourceId" <> "DestinationId")
            )
            """));
    }
}
