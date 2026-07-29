using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IErrorLogRepository
{
    /// <summary>Upsert by Fingerprint: inserts a new group or increments an existing group's count
    /// and refreshes its last-seen time / latest details. Returns true if this was a brand-new error
    /// signature (first occurrence) — used to fire a one-time notification, not spam on repeats.</summary>
    Task<bool> RecordAsync(ErrorLog entry);

    /// <summary>Paged listing of error groups. Ordered most-frequent/most-recent first.</summary>
    Task<(IReadOnlyList<ErrorLog> Items, int Total)> GetPagedAsync(
        int page, int pageSize, int? statusCode = null, string? search = null);

    Task<ErrorStats> GetStatsAsync();
}
