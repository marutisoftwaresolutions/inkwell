using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class WebhookRepository : IWebhookRepository
{
    private readonly DapperContext _ctx;
    public WebhookRepository(DapperContext ctx) => _ctx = ctx;

    private const string Columns =
        "w.Id, w.OwnerId, w.Name, w.Url, w.SecretProtected, w.Events, w.IsEnabled, w.SiteBaseUrl, w.CreatedAt, w.UpdatedAt";

    public async Task<IReadOnlyList<Webhook>> GetAllAsync(Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<Webhook>($@"
            SELECT {Columns},
                   d.CreatedAt    AS LastDeliveryAt,
                   d.Status       AS LastDeliveryStatus,
                   d.ResponseCode AS LastResponseCode
              FROM Webhooks w
              OUTER APPLY (SELECT TOP 1 CreatedAt, Status, ResponseCode
                             FROM WebhookDeliveries
                            WHERE WebhookId = w.Id
                            ORDER BY CreatedAt DESC, Id DESC) d
             WHERE w.OwnerId = @OwnerId
             ORDER BY w.CreatedAt DESC;", new { OwnerId = ownerId });
        return rows.ToList();
    }

    public async Task<Webhook?> GetByIdAsync(Guid id, Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Webhook>(
            $"SELECT {Columns} FROM Webhooks w WHERE w.Id = @Id AND w.OwnerId = @OwnerId;", new { Id = id, OwnerId = ownerId });
    }

    public async Task<IReadOnlyList<Webhook>> GetEnabledAsync(Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<Webhook>(
            $"SELECT {Columns} FROM Webhooks w WHERE w.OwnerId = @OwnerId AND w.IsEnabled = 1;", new { OwnerId = ownerId });
        return rows.ToList();
    }

    public async Task CreateAsync(Webhook w)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO Webhooks (Id, OwnerId, Name, Url, SecretProtected, Events, IsEnabled, SiteBaseUrl, CreatedAt, UpdatedAt)
            VALUES (@Id, @OwnerId, @Name, @Url, @SecretProtected, @Events, @IsEnabled, @SiteBaseUrl, @CreatedAt, @UpdatedAt);", w);
    }

    public async Task UpdateAsync(Webhook w)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE Webhooks
               SET Name = @Name, Url = @Url, SecretProtected = @SecretProtected, Events = @Events,
                   IsEnabled = @IsEnabled, SiteBaseUrl = @SiteBaseUrl, UpdatedAt = @UpdatedAt
             WHERE Id = @Id AND OwnerId = @OwnerId;", w);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync("DELETE FROM Webhooks WHERE Id = @Id AND OwnerId = @OwnerId;",
            new { Id = id, OwnerId = ownerId }) > 0;
    }

    public async Task<int> EnqueueAsync(IReadOnlyCollection<WebhookDelivery> deliveries)
    {
        if (deliveries.Count == 0) return 0;
        using var conn = _ctx.CreateConnection();
        using var tx = conn.BeginTransaction();
        var queued = await conn.ExecuteAsync(@"
            INSERT INTO WebhookDeliveries (WebhookId, OwnerId, EventId, EventType, EventKey, PayloadJson, Status, Attempts, NextAttemptAt, CreatedAt)
            SELECT @WebhookId, @OwnerId, @EventId, @EventType, @EventKey, @PayloadJson, @Status, 0, @NextAttemptAt, @CreatedAt
             WHERE @EventKey IS NULL
                OR NOT EXISTS (SELECT 1 FROM WebhookDeliveries WHERE WebhookId = @WebhookId AND EventKey = @EventKey);",
            deliveries, tx);
        tx.Commit();
        return queued;
    }

    public async Task<long> CreateDeliveryAsync(WebhookDelivery d)
    {
        using var conn = _ctx.CreateConnection();
        d.Id = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO WebhookDeliveries (WebhookId, OwnerId, EventId, EventType, EventKey, PayloadJson, Status, Attempts, NextAttemptAt, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@WebhookId, @OwnerId, @EventId, @EventType, @EventKey, @PayloadJson, @Status, 0, @NextAttemptAt, @CreatedAt);", d);
        return d.Id;
    }

    public async Task<IReadOnlyList<WebhookDelivery>> GetDueAsync(DateTime nowUtc, int take)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<WebhookDelivery>(@"
            SELECT TOP (@Take) d.*
              FROM WebhookDeliveries d
              JOIN Webhooks w ON w.Id = d.WebhookId AND w.IsEnabled = 1
             WHERE d.Status = @Pending AND d.NextAttemptAt <= @Now
             ORDER BY d.NextAttemptAt, d.Id;",
            new { Take = take, Now = nowUtc, Pending = WebhookDeliveryStatus.Pending });
        return rows.ToList();
    }

    public async Task RecordAttemptAsync(WebhookDelivery d)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE WebhookDeliveries
               SET Status = @Status, Attempts = @Attempts, NextAttemptAt = @NextAttemptAt, LastAttemptAt = @LastAttemptAt,
                   ResponseCode = @ResponseCode, ResponseSnippet = @ResponseSnippet, Error = @Error
             WHERE Id = @Id;", d);
    }

    public async Task<WebhookDelivery?> GetDeliveryAsync(long id, Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<WebhookDelivery>(
            "SELECT * FROM WebhookDeliveries WHERE Id = @Id AND OwnerId = @OwnerId;", new { Id = id, OwnerId = ownerId });
    }

    private static string SearchFilter(string? q) => string.IsNullOrWhiteSpace(q)
        ? ""
        : @"AND (EventType LIKE @Like ESCAPE '\' OR Status LIKE @Like ESCAPE '\' OR Error LIKE @Like ESCAPE '\')";

    private static string Like(string? q) =>
        "%" + (q ?? "").Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[") + "%";

    public async Task<IReadOnlyList<WebhookDelivery>> SearchDeliveriesAsync(Guid webhookId, Guid ownerId, string? q, int page, int pageSize)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<WebhookDelivery>($@"
            SELECT Id, WebhookId, OwnerId, EventId, EventType, EventKey, '' AS PayloadJson, Status, Attempts,
                   NextAttemptAt, LastAttemptAt, ResponseCode, ResponseSnippet, Error, CreatedAt
              FROM WebhookDeliveries
             WHERE WebhookId = @WebhookId AND OwnerId = @OwnerId {SearchFilter(q)}
             ORDER BY CreatedAt DESC, Id DESC
             OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;",
            new { WebhookId = webhookId, OwnerId = ownerId, Like = Like(q), Skip = Math.Max(0, (page - 1) * pageSize), Take = pageSize });
        return rows.ToList();
    }

    public async Task<int> CountDeliveriesAsync(Guid webhookId, Guid ownerId, string? q)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<int>($@"
            SELECT COUNT(1) FROM WebhookDeliveries
             WHERE WebhookId = @WebhookId AND OwnerId = @OwnerId {SearchFilter(q)};",
            new { WebhookId = webhookId, OwnerId = ownerId, Like = Like(q) });
    }

    public async Task<int> PruneAsync(DateTime olderThanUtc)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync(@"
            DELETE TOP (2000) FROM WebhookDeliveries
             WHERE CreatedAt < @Cutoff AND Status <> @Pending;",
            new { Cutoff = olderThanUtc, Pending = WebhookDeliveryStatus.Pending });
    }
}
