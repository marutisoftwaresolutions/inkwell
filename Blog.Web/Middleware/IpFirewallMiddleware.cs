using Blog.Web.Services.Security;

namespace Blog.Web.Middleware;

/// <summary>
/// Front door of the request pipeline. Refuses addresses the IP firewall has blocked with a bare
/// 403 — no view rendering, no database work, no error-log entry — and, on the way out, hands the
/// completed request to the threat scorer so hostile addresses block themselves.
///
/// Registered before status-code pages and static files so a flood costs as little as possible.
/// </summary>
public sealed class IpFirewallMiddleware
{
    private readonly RequestDelegate _next;

    public IpFirewallMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IpFirewallService firewall)
    {
        var ip = firewall.ResolveClientIp(context);

        var block = await firewall.GetActiveBlockAsync(ip);
        if (block is not null)
        {
            firewall.NoteDenied(ip);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            // Tell crawlers not to keep this in an index if one ever reaches it.
            context.Response.Headers["X-Robots-Tag"] = "noindex";
            await context.Response.WriteAsync("403 Forbidden — your IP address has been blocked due to suspicious activity.");
            return;
        }

        await _next(context);

        await firewall.RegisterRequestAsync(context, ip, context.Response.StatusCode);
    }
}

public static class IpFirewallMiddlewareExtensions
{
    public static IApplicationBuilder UseIpFirewall(this IApplicationBuilder app)
        => app.UseMiddleware<IpFirewallMiddleware>();
}
