using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Diagnostics;

namespace Blog.Web.Middleware;

/// <summary>
/// Records requests from identified crawlers (search engines, AI answer engines) to
/// <c>CrawlerVisits</c> — the only way to see whether those engines actually read the site. Human
/// page views are no longer counted here: they arrive from the page's own beacon
/// (<see cref="PageViewBeaconService"/>), so clients that never run JavaScript are not counted.
///
/// The request path does no I/O: rows go to <see cref="TrafficRecorder"/>, which buffers them and
/// writes in batches, so a flood of requests can never become a flood of database inserts.
/// </summary>
public class CrawlerVisitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CrawlerVisitMiddleware> _logger;
    private readonly TrafficRecorder _recorder;
    private readonly Blog.Web.Services.Security.IpFirewallService _firewall;

    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".js", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico",
        ".woff", ".woff2", ".ttf", ".eot", ".webp", ".avif", ".pdf", ".zip"
    };

    public CrawlerVisitMiddleware(RequestDelegate next, ILogger<CrawlerVisitMiddleware> logger, TrafficRecorder recorder,
        Blog.Web.Services.Security.IpFirewallService firewall)
    {
        _next = next;
        _logger = logger;
        _recorder = recorder;
        _firewall = firewall;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // A 404 or 500 is rendered by re-running the pipeline for /error/{code}. That second pass is
        // the same request: recording it again double-counted crawler 404s and filed 500s under
        // /error/500 instead of the URL that failed. Only the original pass is recorded.
        if (IsErrorReExecution(context))
        {
            await _next(context);
            return;
        }

        try
        {
            await _next(context);
        }
        catch
        {
            // The exception handler will answer 500 via a re-execution we deliberately skip, so this
            // is the only place the failing URL is still known.
            TrackCrawler(context, StatusCodes.Status500InternalServerError);
            throw;
        }

        TrackCrawler(context, context.Response.StatusCode);
    }

    public static bool IsErrorReExecution(HttpContext context) =>
        context.Features.Get<IStatusCodeReExecuteFeature>() is not null
        || context.Features.Get<IExceptionHandlerFeature>() is not null;

    /// <summary>
    /// Self-swallowing: crawler visibility must never slow or break a page, and a request the
    /// identifier cannot name is simply not recorded rather than guessed at.
    /// </summary>
    private void TrackCrawler(HttpContext context, int status)
    {
        try
        {
            if (!HttpMethods.IsGet(context.Request.Method)) return;
            if (context.Items.ContainsKey(HeadRequestMiddleware.OriginalMethodWasHeadKey)) return;

            var ua = context.Request.Headers.UserAgent.ToString();
            var identity = CrawlerIdentifier.Identify(ua);
            if (identity is null) return;

            var path = context.Request.Path.Value ?? "/";
            if (SkippedExtensions.Contains(Path.GetExtension(path))) return;   // assets, not content

            _recorder.RecordCrawlerVisit(new CrawlerVisit
            {
                OwnerId    = TrafficRecorder.OwnerOf(context),
                Crawler    = identity.Name,
                Operator   = identity.Operator,
                IsAi       = identity.IsAi,
                Path       = path.Length > 1024 ? path[..1024] : path,
                StatusCode = status,
                UserAgent  = ua.Length > 512 ? ua[..512] : ua,
                VisitedAt  = DateTime.UtcNow
            }, _firewall.ResolveClientIp(context));   // the real client behind a trusted proxy, as the firewall sees it
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Crawler tracking skipped (non-fatal).");
        }
    }
}

public static class CrawlerVisitMiddlewareExtensions
{
    public static IApplicationBuilder UseCrawlerVisitTracking(this IApplicationBuilder app)
        => app.UseMiddleware<CrawlerVisitMiddleware>();
}
