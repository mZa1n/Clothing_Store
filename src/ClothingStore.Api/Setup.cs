using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Api;

public static class Setup
{
    public static async Task ConfigureDatabase(WebApplication app, bool createAdmin)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDb>();
        await db.Database.MigrateAsync();

        if (!createAdmin)
            return;

        var email = app.Configuration["Bootstrap:AdminEmail"]?.Trim();
        var password = app.Configuration["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Задайте Bootstrap__AdminEmail и Bootstrap__AdminPassword.");

        var users = scope.ServiceProvider.GetRequiredService<UserManager<StoreUser>>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (await users.FindByEmailAsync(email) is { } existing)
        {
            if (!existing.Disabled && await users.IsInRoleAsync(existing, AccessRoles.Admin))
                return;
            throw new InvalidOperationException("Эта учётная запись уже существует. Выберите другой email.");
        }

        var user = new StoreUser { Email = email, UserName = email };
        Verify(await users.CreateAsync(user, password));
        Verify(await users.AddToRoleAsync(user, AccessRoles.Admin));
        await transaction.CommitAsync();
    }

    private static void Verify(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
