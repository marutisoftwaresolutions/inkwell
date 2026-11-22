using System.Security.Claims;
using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace Blog.Web.Services;

/// <summary>
/// Checks that the account behind a Desk sign-in cookie can still sign in. A cookie lasts up to seven days; without
/// this, a deleted or disabled user kept the Desk until it expired, and a session for an account missing from the
/// database (after a restore or a database swap) reached screens that write under that id and failed with a 500
/// (the <c>/admin/theme</c> foreign-key error). Authentication fails closed (Security rule): a missing or disabled
/// account is signed out. The answer is cached for a minute per user, so a busy Desk costs one read a minute, and
/// <see cref="Forget"/> clears it the moment an admin disables or deletes someone.
/// </summary>
public static class DeskSessionValidator
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);
    private static string Key(Guid userId) => "desk-session:" + userId.ToString("N");

    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        if (!Guid.TryParse(ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var cache = ctx.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
        // What is cached is the account's cut-off, not a yes: each cookie is still compared with it, so a session
        // ended by "Sign out everywhere" or a password change is refused even while another one is valid.
        if (cache.TryGetValue(Key(userId), out DateTime cachedCutOff))
        {
            if (!IssuedBefore(ctx, cachedCutOff)) return;
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        bool canSignIn;
        DateTime cutOff = DateTime.MinValue;
        try
        {
            var user = await ctx.HttpContext.RequestServices.GetRequiredService<IUserRepository>().GetByIdAsync(userId);
            canSignIn = user is { IsActive: true };
            cutOff = user?.SessionsValidFrom ?? DateTime.MinValue;
        }
        catch
        {
            // The account could not be checked (database unreachable): refuse this request, keep the cookie, so the
            // operator is not signed out by an outage.
            ctx.RejectPrincipal();
            return;
        }

        if (canSignIn)
        {
            cache.Set(Key(userId), cutOff, CacheFor);
            if (!IssuedBefore(ctx, cutOff)) return;
        }
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>True when this cookie was issued before the account's cut-off (compared to the second, as the ticket stores it).</summary>
    private static bool IssuedBefore(CookieValidatePrincipalContext ctx, DateTime cutOffUtc) =>
        cutOffUtc != DateTime.MinValue && (ctx.Properties.IssuedUtc is not { } issued || issued.UtcDateTime < cutOffUtc);

    /// <summary>The moment to store as a cut-off: now, to the whole second, so the session signed in right after it is kept.</summary>
    public static DateTime CutOffNow() { var n = DateTime.UtcNow; return new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, n.Second, DateTimeKind.Utc); }

    /// <summary>Makes the next request re-check this user: called when an account is disabled or deleted.</summary>
    public static void Forget(IMemoryCache cache, Guid userId) => cache.Remove(Key(userId));
}
