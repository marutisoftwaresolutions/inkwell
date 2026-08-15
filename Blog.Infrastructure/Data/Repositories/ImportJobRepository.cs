using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class ImportJobRepository : IImportJobRepository
{
    private readonly DapperContext _ctx;
    public ImportJobRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<Guid> CreateJobAsync(ImportJob job)
    {
        using var conn = _ctx.CreateConnection();
        if (job.Id == Guid.Empty) job.Id = Guid.NewGuid();
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO ImportJobs (Id, OwnerId, Source, FileName, FilePath, Status, OptionsJson)
            OUTPUT INSERTED.Id
            VALUES (@Id, @OwnerId, @Source, @FileName, @FilePath, @Status, @OptionsJson)",
            new
            {
                job.Id, job.OwnerId, Source = job.Source.ToString(), job.FileName, job.FilePath,
                Status = job.Status.ToString(), job.OptionsJson
            });
    }

    public async Task<ImportJob?> GetJobAsync(Guid id, Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        var sql = "SELECT * FROM ImportJobs WHERE Id = @Id"
            + (ownerId == Guid.Empty ? "" : " AND OwnerId = @OwnerId");
        return await conn.QueryFirstOrDefaultAsync<ImportJob>(sql, new { Id = id, OwnerId = ownerId });
    }

    public async Task<List<ImportJob>> GetJobsAsync(Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        var sql = "SELECT * FROM ImportJobs "
            + (ownerId == Guid.Empty ? "" : "WHERE OwnerId = @OwnerId ")
            + "ORDER BY CreatedAt DESC";
        return (await conn.QueryAsync<ImportJob>(sql, new { OwnerId = ownerId })).ToList();
    }

    public async Task UpdateJobAsync(ImportJob job)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE ImportJobs SET
                Status=@Status, OptionsJson=@OptionsJson, FilePath=@FilePath,
                TotalItems=@TotalItems, ImportedItems=@ImportedItems, FailedItems=@FailedItems,
                SkippedItems=@SkippedItems, CompletedAt=@CompletedAt, UpdatedAt=GETUTCDATE()
            WHERE Id=@Id",
            new
            {
                Status = job.Status.ToString(), job.OptionsJson, job.FilePath,
                job.TotalItems, job.ImportedItems, job.FailedItems, job.SkippedItems,
                job.CompletedAt, job.Id
            });
    }

    public async Task DeleteJobAsync(Guid id, Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM ImportJobs WHERE Id = @Id AND OwnerId = @OwnerId",
            new { Id = id, OwnerId = ownerId });
    }

    public async Task AddItemsAsync(IEnumerable<ImportItem> items)
    {
        var rows = items.Select(i => new
        {
            i.JobId, ItemType = i.ItemType.ToString(), i.SourceId, i.Title,
            Status = i.Status.ToString(), i.Ordinal, i.DataJson
        }).ToList();
        if (rows.Count == 0) return;

        using var conn = _ctx.CreateConnection();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(@"
            INSERT INTO ImportItems (JobId, ItemType, SourceId, Title, Status, Ordinal, DataJson)
            VALUES (@JobId, @ItemType, @SourceId, @Title, @Status, @Ordinal, @DataJson)",
            rows, tx);
        tx.Commit();
    }

    public async Task<List<ImportItem>> GetPendingBatchAsync(Guid jobId, int take)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<ImportItem>(@"
            SELECT TOP (@Take) * FROM ImportItems
            WHERE JobId = @JobId AND Status = 'Pending'
            ORDER BY Ordinal, Id", new { JobId = jobId, Take = take })).ToList();
    }

    public async Task UpdateItemAsync(ImportItem item)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE ImportItems SET Status=@Status, TargetId=@TargetId, Error=@Error WHERE Id=@Id",
            new { Status = item.Status.ToString(), item.TargetId, item.Error, item.Id });
    }

    public async Task<List<ImportItem>> GetItemsAsync(Guid jobId, ImportItemStatus? status = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = "SELECT * FROM ImportItems WHERE JobId = @JobId"
            + (status.HasValue ? " AND Status = @Status" : "")
            + " ORDER BY Ordinal, Id";
        return (await conn.QueryAsync<ImportItem>(sql,
            new { JobId = jobId, Status = status?.ToString() })).ToList();
    }

    public async Task<Guid?> FindImportedTargetAsync(Guid jobId, ImportItemType type, string sourceId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Guid?>(@"
            SELECT TOP 1 TargetId FROM ImportItems
            WHERE JobId = @JobId AND ItemType = @Type AND SourceId = @SourceId AND Status = 'Imported' AND TargetId IS NOT NULL",
            new { JobId = jobId, Type = type.ToString(), SourceId = sourceId });
    }

    public async Task<Dictionary<ImportItemType, int>> GetTypeCountsAsync(Guid jobId, ImportItemStatus? status = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = "SELECT ItemType, COUNT(*) AS Cnt FROM ImportItems WHERE JobId = @JobId"
            + (status.HasValue ? " AND Status = @Status" : "")
            + " GROUP BY ItemType";
        var rows = await conn.QueryAsync<(string ItemType, int Cnt)>(sql,
            new { JobId = jobId, Status = status?.ToString() });
        var result = new Dictionary<ImportItemType, int>();
        foreach (var r in rows)
            if (Enum.TryParse<ImportItemType>(r.ItemType, out var t)) result[t] = r.Cnt;
        return result;
    }

    public async Task RecomputeJobCountsAsync(Guid jobId)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE ImportJobs SET
                TotalItems    = (SELECT COUNT(*) FROM ImportItems WHERE JobId=@JobId),
                ImportedItems = (SELECT COUNT(*) FROM ImportItems WHERE JobId=@JobId AND Status='Imported'),
                FailedItems   = (SELECT COUNT(*) FROM ImportItems WHERE JobId=@JobId AND Status='Failed'),
                SkippedItems  = (SELECT COUNT(*) FROM ImportItems WHERE JobId=@JobId AND Status='Skipped'),
                UpdatedAt     = GETUTCDATE()
            WHERE Id=@JobId", new { JobId = jobId });
    }
}
