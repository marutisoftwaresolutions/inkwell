using Blog.Core.Interfaces;

namespace Blog.Web.Services.Jobs;

/// <summary>
/// Hourly summary of finished days of <c>PageViews</c> into <c>PageViewDaily</c> (ADR-0037), so Admin → Analytics
/// reads one row per page, source, referrer, campaign, country and region per day instead of every view. A day is
/// summarised once it has been over for an hour, which leaves time for the buffered writer's last flush. The first
/// run on an existing site backfills, oldest day first, a month of days per run. Days not yet summarised are still
/// read from the raw table, so the report is complete either way.
/// </summary>
public sealed class PageViewRollupJob : IScheduledJob
{
    /// <summary>Days with views summarised per run.</summary>
    public const int MaxDaysPerRun = 31;

    private readonly IPageViewRepository _views;

    public PageViewRollupJob(IPageViewRepository views) => _views = views;

    public string Name => "page-view-rollup";
    public TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var through = CrawlerVisitRollupJob.LastFinishedDay(DateTime.UtcNow);
        var (days, rows) = await _views.RollUpAsync(through, MaxDaysPerRun);
        return days == 0
            ? $"Nothing new to summarise through {through:yyyy-MM-dd}."
            : $"Summarised {days} day(s) of page views into {rows} row(s), through {through:yyyy-MM-dd} at most.";
    }
}
