using System.Security.Claims;
using Blog.Web.Services;

namespace Blog.Web.Middleware;

/// <summary>
/// Sends a signed-in user whose role requires two-factor sign-in, and who has not set it up, to the
/// setup page before any Desk page (ADR-0021). It covers every way in at once: a password sign-in, a
/// new registration, and sessions that were already open when an Admin turned the requirement on.
/// Runs after authorization; reads a one-minute cache, so it costs nothing on most requests.
/// </summary>
public sealed class TwoFactorEnforcementMiddleware
{
    private readonly RequestDelegate _next;
    public TwoFactorEnforcementMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, TwoFactorService twoFactor)
    {
        if (ctx.User.Identity?.IsAuthenticated == true
            && ctx.Request.Path.StartsWithSegments("/admin")
            && Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && await twoFactor.MustEnrolAsync(userId, ctx.User.FindFirstValue(ClaimTypes.Role)))
        {
            const string setup = "/account/two-factor/setup?required=true";
            if (ctx.Request.Headers.ContainsKey("HX-Request"))
            {
                ctx.Response.Headers["HX-Redirect"] = setup;
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                return;
            }
            ctx.Response.Redirect(setup);
            return;
        }
        await _next(ctx);
    }
}
