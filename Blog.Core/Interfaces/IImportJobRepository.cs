using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IImportJobRepository
{
    Task<Guid> CreateJobAsync(ImportJob job);
    Task<ImportJob?> GetJobAsync(Guid id, Guid ownerId);
    Task<List<ImportJob>> GetJobsAsync(Guid ownerId);
    Task UpdateJobAsync(ImportJob job);
    Task DeleteJobAsync(Guid id, Guid ownerId);

    /// <summary>Bulk-inserts parsed items (in one transaction).</summary>
    Task AddItemsAsync(IEnumerable<ImportItem> items);

    /// <summary>Next N unprocessed (Pending) items, ordered by Ordinal then Id (dependency order).</summary>
    Task<List<ImportItem>> GetPendingBatchAsync(Guid jobId, int take);

    Task UpdateItemAsync(ImportItem item);

    Task<List<ImportItem>> GetItemsAsync(Guid jobId, ImportItemStatus? status = null);

    /// <summary>TargetId of an already-imported item of the given type+source key (for cross-references:
    /// image remap, post→category/tag mapping, comment→post mapping). Null if not yet imported.</summary>
    Task<Guid?> FindImportedTargetAsync(Guid jobId, ImportItemType type, string sourceId);

    /// <summary>Counts of items in the job grouped by type (for the preview), only counting a status if given.</summary>
    Task<Dictionary<ImportItemType, int>> GetTypeCountsAsync(Guid jobId, ImportItemStatus? status = null);

    /// <summary>Recomputes and persists the job's Total/Imported/Failed/Skipped from its items.</summary>
    Task RecomputeJobCountsAsync(Guid jobId);
}
