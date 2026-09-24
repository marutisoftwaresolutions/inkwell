using Blog.Core.Interfaces;

namespace Blog.Web.Services.Jobs;

/// <summary>
/// Nightly prune of <c>wwwroot/og-cache</c>: cached Open Graph cards older than
/// <see cref="OgImageCache.MaxAge"/> are deleted so the next request regenerates them with the current
/// title, and the folder stops growing without bound. Nothing else depends on a card being present —
/// <c>/og-image/{slug}.png</c> rebuilds any card it cannot find.
/// </summary>
public sealed class OgCachePruneJob : IScheduledJob
{
    private readonly IWebHostEnvironment _env;
    public OgCachePruneJob(IWebHostEnvironment env) => _env = env;

    public string Name => "og-cache-prune";
    public TimeSpan Interval => TimeSpan.FromHours(24);

    public Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var removed = OgImageCache.Prune(_env.WebRootPath, DateTime.UtcNow, OgImageCache.MaxAge);
        return Task.FromResult($"Removed {removed} cached OG card(s) older than {OgImageCache.MaxAge.TotalDays:0} days.");
    }
}
