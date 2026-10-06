using System.Security.Claims;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Blog.Web.Services;

/// <summary>
/// Issues the Desk sign-in cookie for a user whose identity is fully proven (password, and the second
/// factor when they use one). One place, so the account and two-factor controllers cannot drift apart.
/// </summary>
public sealed class UserSignInService
{
    private readonly IRoleRepository _roles;
    public UserSignInService(IRoleRepository roles) => _roles = roles;

    public async Task SignInAsync(HttpContext http, User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName ?? user.Email),
            new(ClaimTypes.Role, user.Role ?? "Author"),
        };

        // RBAC permissions from the database, as claims
        foreach (var permission in await _roles.GetPermissionNamesForUserAsync(user.Id))
            claims.Add(new Claim("Permission", permission));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });
    }
}
