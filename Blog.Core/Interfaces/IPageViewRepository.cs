using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IPageViewRepository
{
    /// <summary>Writes a batch of buffered views in one transaction; returns the rows written.</summary>
    Task<int> RecordManyAsync(IReadOnlyCollection<PageView> views);
    Task<int> GetTotalAsync(Guid ownerId, DateTime from, DateTime to);
    Task<IReadOnlyList<(DateTime Date, int Count)>> GetDailyAsync(Guid ownerId, DateTime from, DateTime to);

    /// <summary>
    /// The whole Analytics report for [from, to) in one stored-procedure call: total, daily series, sources,
    /// and the top <paramref name="top"/> pages, referrers, countries and regions (campaigns are not capped).
    /// </summary>
    Task<AnalyticsReport> GetReportAsync(Guid ownerId, DateTime from, DateTime to, int top = 20);

    /// <summary>
    /// Summarises finished days of views into the daily table the report reads, continuing after the last
    /// summarised day, up to <paramref name="throughDay"/> and at most <paramref name="maxDays"/> days with views.
    /// Returns the days and summary rows written; (0, 0) when another roll-up is already running.
    /// </summary>
    Task<(int Days, int Rows)> RollUpAsync(DateTime throughDay, int maxDays);

    /// <summary>Every owner that has at least one recorded view — the retention job prunes per owner.</summary>
    Task<IReadOnlyList<Guid>> GetOwnerIdsAsync();

    /// <summary>
    /// Deletes the owner's raw views recorded before the cut-off, but never from a day not yet summarised, so
    /// Analytics loses nothing. Deletes in small batches and stops once <paramref name="budget"/> has passed, so a
    /// large first prune is spread over several runs; returns the views removed.
    /// </summary>
    Task<int> PruneAsync(Guid ownerId, DateTime olderThanUtc, TimeSpan budget);

    Task<IReadOnlyList<PageView>> GetRecentAsync(Guid ownerId, int limit = 200);
}
