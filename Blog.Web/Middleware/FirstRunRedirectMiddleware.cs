using Blog.Core.Interfaces;

namespace Blog.Web.Middleware;

/// <summary>
/// Sends a brand-new self-hosted install from the home page to the setup wizard, so the first page a
/// new operator sees is not an empty blog. It checks only GET "/" and only until the site is installed:
/// once a settings row or a user exists the answer is remembered for the life of the process and the
/// middleware costs nothing. Cloud mode is left alone (its home page is the tenant directory), and any
/// failure falls through to the normal page (fail open).
/// </summary>
public sealed class FirstRunRedirectMiddleware
{
    private static volatile bool _installed;
    private readonly RequestDelegate _next;
    private readonly bool _selfHosted;

    public FirstRunRedirectMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _selfHosted = !string.Equals(config["DeploymentMode"], "Cloud", StringComparison.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!_installed && _selfHosted && HttpMethods.IsGet(ctx.Request.Method) && ctx.Request.Path == "/")
        {
            try
            {
                var settings = ctx.RequestServices.GetRequiredService<ISettingRepository>();
                var users = ctx.RequestServices.GetRequiredService<IUserRepository>();
                if (await settings.IsInstalledAsync() || await users.AnyUsersExistAsync()) _installed = true;
                else
                {
                    ctx.Response.Redirect("/setup");   // temporary: the home page is the blog once setup is done
                    return;
                }
            }
            catch
            {
                // Database unreachable or schema missing: show the normal page and its own error handling.
            }
        }
        await _next(ctx);
    }
}
