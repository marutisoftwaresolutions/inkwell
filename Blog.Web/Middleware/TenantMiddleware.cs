using Blog.Core.Interfaces;
using Blog.Web.Services;

namespace Blog.Web.Middleware;

/// <summary>
/// Middleware that resolves the current tenant from the request.
/// 
/// Self-hosted mode: resolves to the first admin user, no URL rewriting.
/// Cloud mode: extracts /{username} from the URL path, resolves the user,
///             and rewrites the path to strip the slug so downstream routing works.
/// </summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _isCloudMode;

    // Paths that are never tenant slugs (static files, system paths)
    private static readonly string[] _reservedSlugs = new[]
    {
        "admin", "account", "setup", "error", "api",
        "feed", "sitemap.xml", "uploads", "lib", "css", "js",
        "favicon.ico", "robots.txt", "llms.txt", "llms-full.txt",
        "author", "category", "tag", "series", "search", "sitemap-static.xml",
        "_framework", "_content"
    };

    public TenantMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var mode = configuration.GetValue<string>("DeploymentMode") ?? "SelfHosted";
        _isCloudMode = mode.Equals("Cloud", StringComparison.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var tenantContext = context.RequestServices.GetRequiredService<TenantContext>();
        tenantContext.IsCloudMode = _isCloudMode;

        if (!_isCloudMode)
        {
            await ResolveSelfHostedTenant(context, tenantContext);
        }
        else
        {
            ResolveCloudTenant(context, tenantContext);
        }

        // Store tenant context in HttpContext.Items for easy access
        context.Items["TenantContext"] = tenantContext;

        await _next(context);
    }

    private async Task ResolveSelfHostedTenant(HttpContext context, TenantContext tenantContext)
    {
        // In self-hosted mode, if user is authenticated, use their ID
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                tenantContext.UserId = userId;
                tenantContext.IsResolved = true;
                return;
            }
        }

        // For public pages in self-hosted mode, get the first admin user
        var userRepo = context.RequestServices.GetRequiredService<IUserRepository>();
        var adminUser = await userRepo.GetFirstAdminAsync();
        if (adminUser != null)
        {
            tenantContext.UserId = adminUser.Id;
            tenantContext.Slug = adminUser.Slug ?? string.Empty;
            tenantContext.IsResolved = true;
        }
    }

    private void ResolveCloudTenant(HttpContext context, TenantContext tenantContext)
    {
        var path = context.Request.Path.Value ?? "/";

        // Skip static file paths
        if (path.StartsWith("/uploads/") || path.StartsWith("/lib/") || 
            path.StartsWith("/css/") || path.StartsWith("/js/"))
        {
            return; // No tenant resolution needed for static files
        }

        // Extract first path segment as potential tenant slug
        var segments = path.TrimStart('/').Split('/', 2);
        var potentialSlug = segments[0].ToLowerInvariant();

        // Skip if empty or reserved
        if (string.IsNullOrEmpty(potentialSlug) || 
            Array.Exists(_reservedSlugs, s => s.Equals(potentialSlug, StringComparison.OrdinalIgnoreCase)))
        {
            return; // System path, no tenant rewriting
        }

        // Try to resolve the slug to a user
        var userRepo = context.RequestServices.GetRequiredService<IUserRepository>();
        var user = userRepo.GetBySlugAsync(potentialSlug).GetAwaiter().GetResult();

        if (user == null)
        {
            return; // Not a valid tenant slug — will 404 naturally
        }

        // Resolve the tenant
        tenantContext.UserId = user.Id;
        tenantContext.Slug = user.Slug ?? potentialSlug;
        tenantContext.IsResolved = true;

        // Rewrite the path to strip the tenant slug
        // e.g., /john/admin/posts → /admin/posts
        // e.g., /john/my-post → /my-post  
        // e.g., /john → /
        var remainingPath = segments.Length > 1 ? "/" + segments[1] : "/";
        context.Request.Path = remainingPath;

        // Store original path for URL generation
        context.Items["OriginalPath"] = path;
        context.Items["TenantSlug"] = tenantContext.Slug;
    }
}

/// <summary>Extension method for clean middleware registration.</summary>
public static class TenantMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TenantMiddleware>();
    }
}
