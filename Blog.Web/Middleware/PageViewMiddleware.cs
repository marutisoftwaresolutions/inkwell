using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Blog.Web.Middleware;

public class PageViewMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PageViewMiddleware> _logger;
    private readonly IServiceProvider _rootServices;

    // Shared HTTP client for fallback geo lookup — timeout keeps it from stalling tracking
    private static readonly HttpClient _geoHttp = new() { Timeout = TimeSpan.FromSeconds(4) };

    // Private IP prefixes — never look up these externally
    private static readonly string[] _privatePrefixes =
        ["127.", "::1", "10.", "192.168.", "172.16.", "172.17.", "172.18.", "172.19.",
         "172.20.", "172.21.", "172.22.", "172.23.", "172.24.", "172.25.", "172.26.",
         "172.27.", "172.28.", "172.29.", "172.30.", "172.31.", "0:0:0:0:0:0:0:1", "fe80"];

    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".js", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico",
        ".woff", ".woff2", ".ttf", ".eot", ".webp", ".avif", ".pdf", ".zip"
    };

    // Single-segment paths that are not post pages — utility, auth, and admin roots
    private static readonly HashSet<string> SkippedExactPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/feed", "/sitemap.xml", "/robots.txt",
        "/admin", "/account", "/newsletter", "/avatar", "/og-image", "/landing", "/error"
    };

    public PageViewMiddleware(RequestDelegate next, ILogger<PageViewMiddleware> logger, IServiceProvider rootServices)
    {
        _next = next;
        _logger = logger;
        _rootServices = rootServices;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        // Only track successful GET requests to public pages
        if (!ShouldTrack(context)) return;

        // Capture all values synchronously before fire-and-forget — HttpContext features
        // (pooled buffers, request stream) may be recycled once the request pipeline returns.
        var path      = context.Request.Path.Value ?? "/";
        var referrer  = context.Request.Headers.Referer.ToString();
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var ip        = context.Connection.RemoteIpAddress?.ToString() ?? "";
        var (source, medium) = VisitSourceClassifier.Classify(
            string.IsNullOrEmpty(referrer) ? null : referrer,
            context.Request.Query);
        var campaign = context.Request.Query.TryGetValue("utm_campaign", out var c)  ? c.ToString()  : null;
        var term     = context.Request.Query.TryGetValue("utm_term",     out var t)  ? t.ToString()  : null;
        var content  = context.Request.Query.TryGetValue("utm_content",  out var ct) ? ct.ToString() : null;

        _ = TrackAsync(path, referrer, userAgent, ip, source, medium, campaign, term, content);
    }

    private static bool ShouldTrack(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)) return false;
        if (context.Response.StatusCode >= 400) return false;

        var path = context.Request.Path.Value ?? "/";

        // Allow only home page (/) and single-segment post slugs (/{slug}).
        // Any path with a second slash is a category, tag, admin, or other internal page.
        if (path != "/" && path.IndexOf('/', 1) >= 0)
            return false;

        // Skip static assets
        var ext = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext) && SkippedExtensions.Contains(ext)) return false;

        // Skip utility endpoints that share the single-segment pattern but aren't posts
        if (SkippedExactPaths.Contains(path)) return false;

        // Skip authenticated users — admin/editor visits must not inflate counts.
        // UseAuthentication runs before this middleware, so context.User is fully populated.
        if (context.User.Identity?.IsAuthenticated == true) return false;

        // Skip bot/crawler traffic
        var ua = context.Request.Headers.UserAgent.ToString();
        if (IsBot(ua)) return false;

        return true;
    }

    private async Task TrackAsync(string path, string referrer, string userAgent, string ip,
        string? source, string? medium, string? campaign, string? term, string? content)
    {
        try
        {
            // Use the root service provider — request-scoped IServiceProvider is disposed
            // by the time this fire-and-forget task executes.
            using var scope = _rootServices.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IPageViewRepository>();

            // Resolve OwnerId — self-hosted is always Guid.Empty
            var tenantCtx = scope.ServiceProvider.GetService<ITenantContext>();
            var ownerId = (tenantCtx?.IsResolved == true) ? tenantCtx.UserId : Guid.Empty;

            var (country, region) = await ResolveGeoAsync(ip);

            var view = new PageView
            {
                OwnerId   = ownerId,
                Path      = path,
                Referrer  = string.IsNullOrEmpty(referrer) ? null : referrer.Length > 1000 ? referrer[..1000] : referrer,
                Source    = source,
                Medium    = medium,
                Campaign  = campaign,
                Term      = term,
                Content   = content,
                IpHash    = HashIp(ip),
                UserAgent = userAgent.Length > 500 ? userAgent[..500] : userAgent,
                Country   = country,
                Region    = region,
                CreatedAt = DateTime.UtcNow
            };

            await repo.RecordAsync(view);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PageView tracking failed silently.");
        }
    }

    private async Task<(string? Country, string? Region)> ResolveGeoAsync(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return (null, null);

        // Private/loopback IPs have no geo — don't waste an HTTP call
        if (_privatePrefixes.Any(p => ip.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return (null, null);

        // Fallback: ip-api.com (free, no key, 45 req/min limit)
        var cache = _rootServices.GetService<IMemoryCache>();
        var cacheKey = $"geo:{ip}";
        if (cache != null && cache.TryGetValue(cacheKey, out (string?, string?) cached))
            return cached;

        try
        {
            var json = await _geoHttp.GetStringAsync(
                $"http://ip-api.com/json/{ip}?fields=status,countryCode,regionName");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var statusProp) && statusProp.GetString() == "success")
            {
                var country = root.TryGetProperty("countryCode", out var cc) ? cc.GetString() : null;
                var region  = root.TryGetProperty("regionName",  out var rn) ? rn.GetString()  : null;
                var result  = (country, region);
                cache?.Set(cacheKey, result, TimeSpan.FromHours(24));
                return result;
            }
        }
        catch { }

        return (null, null);
    }

    private static string HashIp(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip + "pv-salt"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // Token-based signatures that identify non-human traffic in the User-Agent string.
    // Checked with Contains() — order does not matter.
    private static readonly string[] _botTokens =
    [
        // Generic crawler signals
        "bot", "crawler", "spider", "scraper", "scan", "check", "monitor",
        // CLI / scripting tools
        "curl", "wget", "python", "ruby", "java", "go-http", "axios", "node-fetch",
        "libwww", "okhttp", "requests", "httpx", "aiohttp", "scrapy",
        // Headless / automation
        "headless", "puppeteer", "playwright", "selenium", "phantomjs", "nightmare",
        // Audit / perf tools
        "lighthouse", "pagespeed", "gtmetrix", "pingdom", "uptimerobot", "statuscake",
        "newrelic", "datadog", "dynatrace",
        // SEO / link tools
        "prerender", "semrush", "ahrefs", "moz.com", "majestic", "screaming frog",
        // Feed readers / aggregators
        "feedfetcher", "feedburner", "feedly", "rss", "podcast",
        // AI / LLM crawlers
        "gptbot", "claudebot", "anthropic", "perplexity", "bytespider",
        "amazonbot", "applebot", "facebookexternalhit",
    ];

    private static bool IsBot(string userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return true;
        var ua = userAgent.ToLowerInvariant();
        foreach (var token in _botTokens)
            if (ua.Contains(token)) return true;
        return false;
    }
}

public static class PageViewMiddlewareExtensions
{
    public static IApplicationBuilder UsePageViewTracking(this IApplicationBuilder app)
        => app.UseMiddleware<PageViewMiddleware>();
}
