using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface ICrawlerVisitRepository
{
    /// <summary>Records one identified crawler request. Best-effort; never throws into the request.</summary>
    Task RecordAsync(CrawlerVisit visit);

    /// <summary>Writes a batch of buffered visits in one transaction; returns the rows written.</summary>
    Task<int> RecordManyAsync(IReadOnlyCollection<CrawlerVisit> visits);

    /// <summary>
    /// The whole AI Crawlers report for the last <paramref name="days"/> days in one stored-procedure call:
    /// per-crawler totals (busiest first), the pages AI crawlers fetch most, requests that carried a query
    /// string, and the daily AI trend (oldest first). Pages, requests and the trend leave out visits that
    /// failed the operator's identity check. <paramref name="ownerId"/> null reads every owner (self-hosted).
    /// </summary>
    Task<CrawlerReport> GetReportAsync(Guid? ownerId, int days, int take = 20);

    /// <summary>
    /// Summarises finished days of visits into the daily table the report reads, continuing after the last
    /// summarised day, up to <paramref name="throughDay"/> and at most <paramref name="maxDays"/> days with visits.
    /// Returns the days and summary rows written; (0, 0) when another roll-up is already running.
    /// </summary>
    Task<(int Days, int Rows)> RollUpAsync(DateTime throughDay, int maxDays);

    /// <summary>Every owner that has at least one recorded visit — the retention job prunes per owner.</summary>
    Task<IReadOnlyList<Guid>> GetOwnerIdsAsync();

    /// <summary>Deletes the owner's visits recorded before the cut-off, and their daily summary rows for days
    /// before it; returns the visits removed.</summary>
    Task<int> PruneAsync(Guid ownerId, DateTime olderThanUtc);
}
