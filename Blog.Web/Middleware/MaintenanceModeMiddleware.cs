using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;

namespace Blog.Web.Middleware;

/// <summary>
/// Maintenance mode (Settings → General). While it is on, a reader's request for the site is answered
/// with the holding page, status 503 and Retry-After, and nothing further runs; signed-in staff, the
/// Desk, sign-in, the API and the other addresses in <see cref="MaintenanceMode.Covers"/> carry on.
/// Sits after authentication and tenant resolution because it needs both. Fails open: any problem
/// reading the setting serves the site.
/// </summary>
public sealed class MaintenanceModeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly MaintenanceGate _gate;

    public MaintenanceModeMiddleware(RequestDelegate next, MaintenanceGate gate)
    {
        _next = next;
        _gate = gate;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated == true || !MaintenanceMode.Covers(ctx.Request.Path.Value))
        {
            await _next(ctx);
            return;
        }

        var state = MaintenanceGate.Off;
        try
        {
            var tenant = ctx.RequestServices.GetService<ITenantContext>();
            state = await _gate.GetAsync(tenant is { IsCloudMode: true, IsResolved: true } ? tenant.UserId : Guid.Empty);
        }
        catch { /* fail open */ }

        if (!state.On)
        {
            await _next(ctx);
            return;
        }

        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.RetryAfter = MaintenanceMode.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers["X-Robots-Tag"] = "noindex";
        if (!HttpMethods.IsHead(ctx.Request.Method))
            await ctx.Response.WriteAsync(MaintenanceMode.Page(state.SiteName, state.Message, state.Language, TextDirection.For(state.Language)));
    }
}
