using Blog.Core.Services.Export.Push;

namespace Blog.Core.Interfaces;

/// <summary>
/// Push runs and their per-item outcomes (ADR-0029). Install-wide, like the export screen (not offered in
/// cloud mode). Credentials are never stored here.
/// </summary>
public interface IExportPushRepository
{
    /// <summary>Creates a running push with one pending item per post and page, in send order.</summary>
    Task CreateAsync(ExportPush push, IReadOnlyList<NewPushItem> items);

    Task<ExportPush?> GetAsync(Guid id);
    Task<IReadOnlyList<ExportPush>> ListRecentAsync(int take);
    /// <summary>Pushes the job should look at: running ones (paused ones wait for the operator).</summary>
    Task<IReadOnlyList<ExportPush>> GetRunningAsync();
    /// <summary>The running or paused push, if any; only one push runs at a time.</summary>
    Task<ExportPush?> GetActiveAsync();

    Task<ExportPushCounts> CountAsync(Guid pushId);

    /// <summary>Pending posts and pages that are due, in send order.</summary>
    Task<IReadOnlyList<ExportPushItem>> GetDueAsync(Guid pushId, DateTime nowUtc, int take);

    /// <summary>The image or term item for a key, created as pending when it does not exist yet.</summary>
    Task<ExportPushItem> GetOrAddAsync(Guid pushId, PushItemKind kind, string sourceKey, string? title);

    /// <summary>The item for a key, or null (used for a page's parent).</summary>
    Task<ExportPushItem?> FindAsync(Guid pushId, PushItemKind kind, string sourceKey);

    Task MarkItemAsync(long itemId, PushItemStatus status, int attempts, DateTime nextAttemptAtUtc,
        string? remoteId, string? remoteUrl, string? error, DateTime nowUtc);

    Task SetStatusAsync(Guid pushId, PushStatus status, string? message, DateTime? resumeAtUtc, DateTime nowUtc, DateTime? finishedAtUtc);

    /// <summary>Marks every pending post and page of a push as cancelled; returns how many.</summary>
    Task<int> CancelPendingAsync(Guid pushId, DateTime nowUtc);

    /// <summary>Posts and pages of a push for the status screen, optionally only one outcome.</summary>
    Task<IReadOnlyList<ExportPushItem>> ListContentItemsAsync(Guid pushId, PushItemStatus? status);

    /// <summary>One page of a push's posts and pages, matched on title, error and remote address, newest-sequence first.</summary>
    Task<IReadOnlyList<ExportPushItem>> SearchContentItemsAsync(Guid pushId, PushItemStatus? status, string? query, int page, int pageSize);
    Task<int> CountContentItemsAsync(Guid pushId, PushItemStatus? status, string? query);

    /// <summary>Removes finished or cancelled pushes (and their items) older than a date, a bounded batch at a time.</summary>
    Task<int> PruneFinishedAsync(DateTime olderThanUtc);
}
