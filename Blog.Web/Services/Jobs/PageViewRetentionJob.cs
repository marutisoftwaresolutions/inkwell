using Blog.Core.Interfaces;

namespace Blog.Web.Services.Jobs;

/// <summary>
/// Hourly prune of raw <c>PageViews</c> for owners who set <c>PageViewRetentionDays</c>. The default, 0, keeps
/// every view, as before the setting existed. Only days the page-view-rollup job has already summarised are ever
/// removed, so Admin → Analytics keeps its full history from <c>PageViewDaily</c>; what goes is the per-view
/// detail (address hash, user agent, exact time).
/// Each run stops after <see cref="Budget"/>, because the scheduler runs jobs one after another: a large first
/// prune is spread over several hours instead of holding up webhooks, newsletters and backups.
/// </summary>
public sealed class PageViewRetentionJob : IScheduledJob
{
    /// <summary>0 keeps everything. The floor covers the dashboard's 30-day figures, which read raw views.</summary>
    public const int KeepEverything = 0, MinDays = 31, MaxDays = 3650;

    /// <summary>Time one run may spend deleting, across all owners.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);

    private readonly IPageViewRepository _views;
    private readonly ISettingRepository _settings;

    public PageViewRetentionJob(IPageViewRepository views, ISettingRepository settings)
    {
        _views = views;
        _settings = settings;
    }

    public string Name => "page-view-retention";
    public TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var owners = await _views.GetOwnerIdsAsync();
        var stopAt = DateTime.UtcNow + Budget;
        int removed = 0, pruned = 0;
        foreach (var owner in owners)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var days = ClampDays((await _settings.GetSettingsAsync(owner)).PageViewRetentionDays);
            if (days == KeepEverything) continue;
            var left = stopAt - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) break;
            removed += await _views.PruneAsync(owner, DateTime.UtcNow.Date.AddDays(-days), left);
            pruned++;
        }
        return pruned == 0
            ? "Page-view retention is off for every site; nothing removed."
            : $"Removed {removed} summarised page view(s) across {pruned} site(s).";
    }

    /// <summary>0 or less keeps everything; anything else is held between 31 and 3650 days.</summary>
    public static int ClampDays(int days) => days <= 0 ? KeepEverything : Math.Clamp(days, MinDays, MaxDays);
}
