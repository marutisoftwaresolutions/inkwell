using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface ISearchPerformanceRepository
{
    /// <summary>
    /// Replaces every row for the owner on <paramref name="date"/> with <paramref name="rows"/>, in one
    /// transaction. Search Console revises recent days for about 72 hours, so a day is always re-pulled
    /// whole rather than merged.
    /// </summary>
    Task ReplaceDayAsync(Guid ownerId, DateTime date, IReadOnlyList<SearchPerformanceRow> rows);

    /// <summary>Per-page daily totals (queries summed, position impression-weighted) in the inclusive range.</summary>
    Task<IReadOnlyList<PageSearchDay>> GetPageDaysAsync(Guid ownerId, DateTime fromDate, DateTime toDate);

    /// <summary>Top queries for one page in the inclusive range, by clicks then impressions.</summary>
    Task<IReadOnlyList<SearchQueryStat>> GetPageQueriesAsync(Guid ownerId, string page, DateTime fromDate, DateTime toDate, int take = 10);

    /// <summary>Newest date stored for the owner, or null when nothing has been pulled yet.</summary>
    Task<DateTime?> GetLatestDateAsync(Guid ownerId);

    /// <summary>Deletes rows dated before the cut-off; returns the count removed.</summary>
    Task<int> PruneAsync(Guid ownerId, DateTime olderThanDate);

    /// <summary>
    /// Deletes every row the owner holds — used when Search Console is disconnected, so data pulled
    /// under a credential the operator has removed does not linger. Returns the count removed.
    /// </summary>
    Task<int> DeleteForOwnerAsync(Guid ownerId);
}
