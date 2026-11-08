using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Api;

public enum AccountRole { Customer, Manager, Admin }

public sealed record AccountAccessRequest(
    [Required, EnumDataType(typeof(AccountRole))] AccountRole? Role,
    [Required] bool? Disabled);

public sealed record AdminAccountView(
    int Id, string Email, string? FullName, bool Disabled, IReadOnlyList<string> Roles);

[ApiController]
[Authorize(Roles = AccessRoles.Admin)]
[Route("api/admin/accounts")]
public sealed class AdminAccountsController(StoreDb db, UserManager<StoreUser> users) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<AdminAccountView>> Get([FromQuery] PageQuery page, CancellationToken ct)
    {
        var accounts = await db.Users.OrderBy(x => x.Id).Skip(page.Offset).Take(page.PageSize)
            .Select(u => new { u.Id, u.Email, u.FullName, u.Disabled }).ToListAsync(ct);
        var ids = accounts.Select(x => x.Id).ToArray();
        var roles = await (from assignment in db.UserRoles
                           join role in db.Roles on assignment.RoleId equals role.Id
                           where ids.Contains(assignment.UserId)
                           select new { assignment.UserId, Name = role.Name! }).ToListAsync(ct);
        var byUser = roles.ToLookup(x => x.UserId, x => x.Name);
        return accounts.Select(x => new AdminAccountView(
            x.Id, x.Email!, x.FullName, x.Disabled, byUser[x.Id].ToList())).ToList();
    }

    [HttpPut("{id:int}/access")]
    public async Task<ActionResult<AdminAccountView>> ChangeAccess(
        int id, AccountAccessRequest request, CancellationToken ct)
    {
        var actorId = User.UserId();
        if (id == actorId)
            throw StoreException.Conflict("Администратор не может изменить собственный доступ");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var adminRoleName = AccessRoles.Admin.ToUpperInvariant();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM "AspNetRoles" WHERE "NormalizedName" = {adminRoleName} FOR UPDATE
            """, ct);

        var actorIsAdmin = await (from user in db.Users
                                  join assignment in db.UserRoles on user.Id equals assignment.UserId
                                  join role in db.Roles on assignment.RoleId equals role.Id
                                  where user.Id == actorId && !user.Disabled && role.Name == AccessRoles.Admin
                                  select user.Id).AnyAsync(ct);
        if (!actorIsAdmin) return Forbid();

        var userToEdit = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (userToEdit is null) return NotFound();

        var roleName = request.Role!.Value.ToString();
        var previous = await users.GetRolesAsync(userToEdit);
        if (previous.Count != 1 || previous[0] != roleName)
        {
            if (previous.Count != 0)
                Verify(await users.RemoveFromRolesAsync(userToEdit, previous));
            Verify(await users.AddToRoleAsync(userToEdit, roleName));
        }

        userToEdit.Disabled = request.Disabled!.Value;
        Verify(await users.UpdateSecurityStampAsync(userToEdit));
        await tx.CommitAsync(ct);
        return new AdminAccountView(
            id, userToEdit.Email!, userToEdit.FullName, userToEdit.Disabled, [roleName]);
    }

    private static void Verify(IdentityResult result)
    {
        if (!result.Succeeded)
            throw StoreException.Conflict("Не удалось изменить доступ. Обновите данные и повторите запрос");
    }
}
