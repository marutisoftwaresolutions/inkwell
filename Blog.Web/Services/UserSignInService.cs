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

    /// <summary>
    /// Who a signed-in person is, for the Desk cookie and for API tokens alike (ADR-0033): id, email, name, role and
    /// the RBAC permissions from the database. One builder, so the two sign-ins cannot drift apart.
    /// </summary>
    public static List<Claim> Claims(User user, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName),
            new(ClaimTypes.Role, user.Role ?? "Author"),
        };
        claims.AddRange(permissions.Select(p => new Claim("Permission", p)));
        return claims;
    }

    public async Task SignInAsync(HttpContext http, User user)
    {
        var claims = Claims(user, await _roles.GetPermissionNamesForUserAsync(user.Id));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });
    }
}
