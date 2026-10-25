using Blog.Core.Interfaces;

namespace Blog.Web.Services.Jobs;

/// <summary>
/// Hourly summary of finished days of <c>CrawlerVisits</c> into <c>CrawlerVisitDaily</c> (ADR-0037), so
/// Admin → AI Crawlers reads a few thousand summary rows instead of millions of visits. A day is summarised
/// once it has been over for an hour, which leaves time for the buffered visit writer to flush its last
/// batch. The first run on an existing site backfills, oldest day first, a month of days per run.
/// Days not yet summarised are still read from the raw table, so the report is complete either way.
/// </summary>
public sealed class CrawlerVisitRollupJob : IScheduledJob
{
    /// <summary>Days with visits summarised per run; a busy day takes well under a second.</summary>
    public const int MaxDaysPerRun = 31;

    private readonly ICrawlerVisitRepository _visits;

    public CrawlerVisitRollupJob(ICrawlerVisitRepository visits) => _visits = visits;

    public string Name => "crawler-visit-rollup";
    public TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var through = LastFinishedDay(DateTime.UtcNow);
        var (days, rows) = await _visits.RollUpAsync(through, MaxDaysPerRun);
        return days == 0
            ? $"Nothing new to summarise through {through:yyyy-MM-dd}."
            : $"Summarised {days} day(s) of crawler visits into {rows} row(s), through {through:yyyy-MM-dd} at most.";
    }

    /// <summary>The last UTC day that ended at least an hour before <paramref name="utcNow"/>.</summary>
    public static DateTime LastFinishedDay(DateTime utcNow) => utcNow.AddHours(-1).Date.AddDays(-1);
}
