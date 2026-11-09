using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Api;

public static class AccessRoles
{
    public const string Customer = "Customer";
    public const string Manager = "Manager";
    public const string Admin = "Admin";
    public const string Staff = Manager + "," + Admin;

    public static readonly string[] All = [Customer, Manager, Admin];
}

public sealed class StoreUser : IdentityUser<int>
{
    [MaxLength(120)] public string? FullName { get; set; }
    public bool Disabled { get; set; }
}

public sealed record Credentials(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MaxLength(128)] string Password);

public sealed record ContactRequest(
    [Required, MaxLength(120)] string FullName,
    [Required, Phone, MaxLength(30)] string Phone);

public sealed record AccountView(int Id, string Email, string? FullName, string? Phone, IList<string> Roles);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    StoreDb db,
    UserManager<StoreUser> users,
    SignInManager<StoreUser> signIn,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult Csrf() => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("register")]
    public async Task<IActionResult> Register(Credentials request, CancellationToken ct)
    {
        var email = request.Email.Trim();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = new StoreUser { Email = email, UserName = email };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            return IdentityErrors(created);

        var assigned = await users.AddToRoleAsync(user, AccessRoles.Customer);
        if (!assigned.Succeeded)
            return IdentityErrors(assigned);

        await tx.CommitAsync(ct);
        return CreatedAtAction(nameof(Me), new { id = user.Id, user.Email });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(Credentials request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || user.Disabled)
            return Problem(statusCode: 401, title: "Неверные учётные данные или доступ заблокирован");

        var result = await signIn.PasswordSignInAsync(user, request.Password, false, lockoutOnFailure: true);
        return result.Succeeded
            ? Ok(await ViewOf(user))
            : Problem(statusCode: 401, title: "Неверные учётные данные или доступ заблокирован");
    }

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me() =>
        await users.GetUserAsync(User) is { } user ? Ok(await ViewOf(user)) : Unauthorized();

    [Authorize(Roles = AccessRoles.Customer), HttpPut("me/contact")]
    public async Task<IActionResult> UpdateContact(ContactRequest request)
    {
        var user = (await users.GetUserAsync(User))!;
        user.FullName = request.FullName.Trim();
        user.PhoneNumber = request.Phone.Trim();
        var saved = await users.UpdateAsync(user);
        return saved.Succeeded ? Ok(await ViewOf(user)) : IdentityErrors(saved);
    }

    private async Task<AccountView> ViewOf(StoreUser user) =>
        new(user.Id, user.Email!, user.FullName, user.PhoneNumber, await users.GetRolesAsync(user));

    private IActionResult IdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            var field = error.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "email";
            ModelState.AddModelError(field, error.Description);
        }
        return ValidationProblem(ModelState);
    }
}

public static class AccountSetup
{
    public static int UserId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static void AddAccounts(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<StoreUser, IdentityRole<int>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
        }).AddEntityFrameworkStores<StoreDb>();
        builder.Services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.Zero);
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "clothing_store";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
    }
}
