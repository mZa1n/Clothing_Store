using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Api;

public sealed class StoreDb(DbContextOptions<StoreDb> options) : DbContext(options);
