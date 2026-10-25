using Blog.Core.Interfaces;
using Blog.Core.Services.Export.Push;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

/// <summary>
/// Push runs and their items (ADR-0029). Enums are stored by name. Item outcomes are written one guarded row
/// at a time, so a push interrupted anywhere resumes at the first unfinished item. No credential ever reaches
/// these tables.
/// </summary>
public sealed class ExportPushRepository : IExportPushRepository
{
    private readonly DapperContext _ctx;
    public ExportPushRepository(DapperContext ctx) => _ctx = ctx;

    private const string ContentKinds = "('Page', 'Post')";

    public async Task CreateAsync(ExportPush push, IReadOnlyList<NewPushItem> items)
    {
        if (push.Id == Guid.Empty) push.Id = Guid.NewGuid();
        using var conn = _ctx.CreateConnection();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(@"
            INSERT INTO ExportPushes (Id, Target, SiteUrl, ConflictPolicy, IncludeDrafts, IncludePages, SourceBaseUrl, Status, Message,
                                      TotalCount, StartedBy, StartedByName, StartedAt, UpdatedAt)
            VALUES (@Id, @Target, @SiteUrl, @ConflictPolicy, @IncludeDrafts, @IncludePages, @SourceBaseUrl, @Status, @Message,
                    @TotalCount, @StartedBy, @StartedByName, @StartedAt, @StartedAt)",
            new
            {
                push.Id, Target = push.Target.ToString(), push.SiteUrl, ConflictPolicy = push.ConflictPolicy.ToString(),
                push.IncludeDrafts, push.IncludePages, push.SourceBaseUrl, Status = push.Status.ToString(), push.Message,
                TotalCount = items.Count, push.StartedBy, push.StartedByName, push.StartedAt,
            }, tx);
        foreach (var chunk in items.Chunk(500))
            await conn.ExecuteAsync(@"
                INSERT INTO ExportPushItems (PushId, Kind, SourceKey, Title, Seq, Status, NextAttemptAt, UpdatedAt)
                VALUES (@PushId, @Kind, @SourceKey, @Title, @Seq, 'Pending', @At, @At)",
                chunk.Select(i => new { PushId = push.Id, Kind = i.Kind.ToString(), i.SourceKey, i.Title, i.Seq, At = push.StartedAt }), tx);
        tx.Commit();
        push.TotalCount = items.Count;
    }

    public async Task<ExportPush?> GetAsync(Guid id)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ExportPush>("SELECT * FROM ExportPushes WHERE Id = @id", new { id });
    }

    public async Task<IReadOnlyList<ExportPush>> ListRecentAsync(int take)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<ExportPush>("SELECT TOP (@take) * FROM ExportPushes ORDER BY StartedAt DESC", new { take })).ToList();
    }

    public async Task<IReadOnlyList<ExportPush>> GetRunningAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<ExportPush>("SELECT * FROM ExportPushes WHERE Status = 'Running' ORDER BY StartedAt")).ToList();
    }

    public async Task<ExportPush?> GetActiveAsync()
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<ExportPush>(
            "SELECT TOP 1 * FROM ExportPushes WHERE Status IN ('Running', 'Paused') ORDER BY StartedAt DESC");
    }

    public async Task<ExportPushCounts> CountAsync(Guid pushId)
    {
        using var conn = _ctx.CreateConnection();
        var rows = (await conn.QueryAsync<(string Status, int N)>($@"
            SELECT Status, COUNT(*) FROM ExportPushItems WHERE PushId = @pushId AND Kind IN {ContentKinds} GROUP BY Status", new { pushId }))
            .ToDictionary(r => r.Status, r => r.N, StringComparer.OrdinalIgnoreCase);
        int N(PushItemStatus s) => rows.GetValueOrDefault(s.ToString());
        return new ExportPushCounts(N(PushItemStatus.Pending), N(PushItemStatus.Created), N(PushItemStatus.Updated),
            N(PushItemStatus.Skipped), N(PushItemStatus.Failed), N(PushItemStatus.Cancelled));
    }

    public async Task<IReadOnlyList<ExportPushItem>> GetDueAsync(Guid pushId, DateTime nowUtc, int take)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<ExportPushItem>($@"
            SELECT TOP (@take) * FROM ExportPushItems
            WHERE PushId = @pushId AND Kind IN {ContentKinds} AND Status = 'Pending' AND NextAttemptAt <= @nowUtc
            ORDER BY Seq, Id", new { pushId, nowUtc, take })).ToList();
    }

    public async Task<ExportPushItem> GetOrAddAsync(Guid pushId, PushItemKind kind, string sourceKey, string? title)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleAsync<ExportPushItem>(@"
            IF NOT EXISTS (SELECT 1 FROM ExportPushItems WITH (UPDLOCK, HOLDLOCK) WHERE PushId = @pushId AND Kind = @kind AND SourceKey = @sourceKey)
                INSERT INTO ExportPushItems (PushId, Kind, SourceKey, Title, Seq, Status, NextAttemptAt, UpdatedAt)
                VALUES (@pushId, @kind, @sourceKey, @title, 0, 'Pending', SYSUTCDATETIME(), SYSUTCDATETIME());
            SELECT * FROM ExportPushItems WHERE PushId = @pushId AND Kind = @kind AND SourceKey = @sourceKey;",
            new { pushId, kind = kind.ToString(), sourceKey, title });
    }

    public async Task<ExportPushItem?> FindAsync(Guid pushId, PushItemKind kind, string sourceKey)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ExportPushItem>(
            "SELECT * FROM ExportPushItems WHERE PushId = @pushId AND Kind = @kind AND SourceKey = @sourceKey",
            new { pushId, kind = kind.ToString(), sourceKey });
    }

    public async Task MarkItemAsync(long itemId, PushItemStatus status, int attempts, DateTime nextAttemptAtUtc,
        string? remoteId, string? remoteUrl, string? error, DateTime nowUtc)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE ExportPushItems SET Status = @status, Attempts = @attempts, NextAttemptAt = @nextAttemptAtUtc,
                   RemoteId = COALESCE(@remoteId, RemoteId), RemoteUrl = COALESCE(@remoteUrl, RemoteUrl), Error = @error, UpdatedAt = @nowUtc
            WHERE Id = @itemId",
            new
            {
                itemId, status = status.ToString(), attempts, nextAttemptAtUtc,
                remoteId = PushRules.Trim(remoteId, 100), remoteUrl = PushRules.Trim(remoteUrl, 1000), error = PushRules.Trim(error, 500), nowUtc,
            });
    }

    public async Task SetStatusAsync(Guid pushId, PushStatus status, string? message, DateTime? resumeAtUtc, DateTime nowUtc, DateTime? finishedAtUtc)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE ExportPushes SET Status = @status, Message = @message, ResumeAt = @resumeAtUtc, UpdatedAt = @nowUtc,
                   FinishedAt = COALESCE(@finishedAtUtc, FinishedAt)
            WHERE Id = @pushId",
            new { pushId, status = status.ToString(), message = PushRules.Trim(message, 500), resumeAtUtc, nowUtc, finishedAtUtc });
    }

    public async Task<int> CancelPendingAsync(Guid pushId, DateTime nowUtc)
    {
        using var conn = _ctx.CreateConnection();
        // Every pending row stops (images and terms too); the count is of posts and pages, which is what the operator chose.
        return await conn.ExecuteScalarAsync<int>($@"
            DECLARE @content INT = (SELECT COUNT(*) FROM ExportPushItems WHERE PushId = @pushId AND Status = 'Pending' AND Kind IN {ContentKinds});
            UPDATE ExportPushItems SET Status = 'Cancelled', UpdatedAt = @nowUtc WHERE PushId = @pushId AND Status = 'Pending';
            SELECT @content;", new { pushId, nowUtc });
    }

    public async Task<IReadOnlyList<ExportPushItem>> ListContentItemsAsync(Guid pushId, PushItemStatus? status)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<ExportPushItem>($@"
            SELECT * FROM ExportPushItems
            WHERE PushId = @pushId AND Kind IN {ContentKinds} AND (@status IS NULL OR Status = @status)
            ORDER BY Seq, Id", new { pushId, status = status?.ToString() })).ToList();
    }

    private const string ItemMatch = @"(@like IS NULL OR Title LIKE @like ESCAPE '\' OR Error LIKE @like ESCAPE '\' OR RemoteUrl LIKE @like ESCAPE '\')";
    private static string? Like(string? q) => string.IsNullOrWhiteSpace(q) ? null
        : "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";

    public async Task<IReadOnlyList<ExportPushItem>> SearchContentItemsAsync(Guid pushId, PushItemStatus? status, string? query, int page, int pageSize)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<ExportPushItem>($@"
            SELECT * FROM ExportPushItems
            WHERE PushId = @pushId AND Kind IN {ContentKinds} AND (@status IS NULL OR Status = @status) AND {ItemMatch}
            ORDER BY Seq, Id
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY",
            new { pushId, status = status?.ToString(), like = Like(query), skip = (Math.Max(1, page) - 1) * pageSize, take = pageSize })).ToList();
    }

    public async Task<int> CountContentItemsAsync(Guid pushId, PushItemStatus? status, string? query)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<int>($@"
            SELECT COUNT(*) FROM ExportPushItems
            WHERE PushId = @pushId AND Kind IN {ContentKinds} AND (@status IS NULL OR Status = @status) AND {ItemMatch}",
            new { pushId, status = status?.ToString(), like = Like(query) });
    }

    public async Task<int> PruneFinishedAsync(DateTime olderThanUtc)
    {
        using var conn = _ctx.CreateConnection();
        // Items go with the push (ON DELETE CASCADE). A small batch per run keeps the lock short.
        return await conn.ExecuteAsync(@"
            DELETE TOP (20) FROM ExportPushes
            WHERE Status IN ('Completed', 'Cancelled') AND FinishedAt IS NOT NULL AND FinishedAt < @olderThanUtc", new { olderThanUtc });
    }
}
