using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Api;

public sealed partial class StoreDb(DbContextOptions<StoreDb> options)
    : IdentityDbContext<StoreUser, IdentityRole<int>, int>(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<IdentityRole<int>>().HasData(
            AccessRoles.All.Select((role, i) => new IdentityRole<int>
            {
                Id = i + 1,
                Name = role,
                NormalizedName = role.ToUpperInvariant(),
                ConcurrencyStamp = role
            }));
        model.Entity<StoreUser>().HasIndex(user => user.NormalizedEmail).IsUnique();
        ConfigureStore(model);
    }

    partial void ConfigureStore(ModelBuilder model);
}
