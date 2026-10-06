using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

/// <summary>Newsletter issues and deliveries (ADR-0028). State changes are single guarded UPDATEs, so a double
/// click or two scheduler ticks cannot queue, cancel or finish an issue twice.</summary>
public class NewsletterRepository : INewsletterRepository
{
    private readonly DapperContext _ctx;
    public NewsletterRepository(DapperContext ctx) => _ctx = ctx;

    private const string ListColumns = @"Id, OwnerId, Subject, Preheader, IntroMarkdown, PostIds, Status, SiteBaseUrl,
        RecipientCount, SentCount, FailedCount, CreatedBy, QueuedBy, QueuedByName, CreatedAt, UpdatedAt, QueuedAt, FinishedAt";

    public async Task<IReadOnlyList<NewsletterIssue>> ListAsync(Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<NewsletterIssue>(
            $"SELECT {ListColumns} FROM NewsletterIssues WHERE OwnerId = @ownerId ORDER BY COALESCE(QueuedAt, UpdatedAt) DESC", new { ownerId })).ToList();
    }

    public async Task<NewsletterIssue?> GetAsync(Guid id, Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<NewsletterIssue>(
            "SELECT * FROM NewsletterIssues WHERE Id = @id AND OwnerId = @ownerId", new { id, ownerId });
    }

    public async Task CreateAsync(NewsletterIssue i)
    {
        if (i.Id == Guid.Empty) i.Id = Guid.NewGuid();
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO NewsletterIssues (Id, OwnerId, Subject, Preheader, IntroMarkdown, PostIds, Status, CreatedBy, CreatedAt, UpdatedAt)
            VALUES (@Id, @OwnerId, @Subject, @Preheader, @IntroMarkdown, @PostIds, 'Draft', @CreatedBy, SYSUTCDATETIME(), SYSUTCDATETIME())", i);
    }

    public async Task<bool> UpdateDraftAsync(NewsletterIssue i)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync(@"
            UPDATE NewsletterIssues SET Subject = @Subject, Preheader = @Preheader, IntroMarkdown = @IntroMarkdown,
                   PostIds = @PostIds, UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id AND OwnerId = @OwnerId AND Status = 'Draft'", i) > 0;
    }

    public async Task<bool> DeleteDraftAsync(Guid id, Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync("DELETE FROM NewsletterIssues WHERE Id = @id AND OwnerId = @ownerId AND Status = 'Draft'", new { id, ownerId }) > 0;
    }

    public async Task<bool> QueueAsync(Guid id, Guid ownerId, string html, string text, string siteBaseUrl, Guid queuedBy, string? queuedByName, DateTime nowUtc)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync(@"
            UPDATE NewsletterIssues SET Status = 'Queued', RenderedHtml = @html, RenderedText = @text, SiteBaseUrl = @siteBaseUrl,
                   QueuedBy = @queuedBy, QueuedByName = @queuedByName, QueuedAt = @nowUtc, UpdatedAt = @nowUtc
            WHERE Id = @id AND OwnerId = @ownerId AND Status = 'Draft'",
            new { id, ownerId, html, text, siteBaseUrl, queuedBy, queuedByName, nowUtc }) > 0;
    }

    public async Task<bool> CancelAsync(Guid id, Guid ownerId, DateTime nowUtc)
    {
        using var conn = _ctx.CreateConnection();
        var changed = await conn.ExecuteAsync(@"
            UPDATE NewsletterIssues SET Status = 'Cancelled', FinishedAt = @nowUtc, UpdatedAt = @nowUtc,
                   SentCount = (SELECT COUNT(*) FROM NewsletterDeliveries WHERE IssueId = @id AND Status = 'Sent'),
                   FailedCount = (SELECT COUNT(*) FROM NewsletterDeliveries WHERE IssueId = @id AND Status = 'Failed')
            WHERE Id = @id AND OwnerId = @ownerId AND Status IN ('Queued', 'Sending')", new { id, ownerId, nowUtc });
        if (changed == 0) return false;
        await conn.ExecuteAsync("UPDATE NewsletterDeliveries SET Status = 'Cancelled' WHERE IssueId = @id AND Status = 'Pending'", new { id });
        return true;
    }

    public async Task<NewsletterProgress> GetProgressAsync(Guid issueId, Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleAsync<NewsletterProgress>(@"
            SELECT COUNT(CASE WHEN d.Status = 'Pending' THEN 1 END) AS Pending,
                   COUNT(CASE WHEN d.Status = 'Sent' THEN 1 END) AS Sent,
                   COUNT(CASE WHEN d.Status = 'Failed' THEN 1 END) AS Failed,
                   COUNT(CASE WHEN d.Status = 'Skipped' THEN 1 END) AS Skipped,
                   COUNT(CASE WHEN d.Status = 'Cancelled' THEN 1 END) AS Cancelled
            FROM NewsletterDeliveries d JOIN NewsletterIssues i ON i.Id = d.IssueId
            WHERE d.IssueId = @issueId AND i.OwnerId = @ownerId", new { issueId, ownerId });
    }

    public async Task<IReadOnlyList<NewsletterDelivery>> GetDeliveriesAsync(Guid issueId, Guid ownerId, int take)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<NewsletterDelivery>(@"
            SELECT TOP (@take) d.Id, d.IssueId, d.MemberId, d.Email, d.Status, d.Attempts, d.NextAttemptAt, d.SentAt, d.Error
            FROM NewsletterDeliveries d JOIN NewsletterIssues i ON i.Id = d.IssueId
            WHERE d.IssueId = @issueId AND i.OwnerId = @ownerId
            ORDER BY CASE d.Status WHEN 'Failed' THEN 0 WHEN 'Pending' THEN 1 ELSE 2 END, d.Id DESC",
            new { issueId, ownerId, take })).ToList();
    }

    public async Task<IReadOnlyList<NewsletterIssue>> GetActiveAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<NewsletterIssue>(
            "SELECT * FROM NewsletterIssues WHERE Status IN ('Queued', 'Sending') ORDER BY QueuedAt")).ToList();
    }

    public async Task<int> ExpandRecipientsAsync(Guid issueId, DateTime nowUtc)
    {
        using var conn = _ctx.CreateConnection();
        // NOT EXISTS + the unique (IssueId, MemberId) index: running twice adds nobody twice.
        await conn.ExecuteAsync(@"
            INSERT INTO NewsletterDeliveries (IssueId, MemberId, Email, Status, Attempts, NextAttemptAt)
            SELECT @issueId, m.Id, m.Email, 'Pending', 0, @nowUtc
            FROM Members m
            WHERE m.Status = 'confirmed' AND m.DeletedAt IS NULL
              AND NOT EXISTS (SELECT 1 FROM NewsletterDeliveries d WHERE d.IssueId = @issueId AND d.MemberId = m.Id)", new { issueId, nowUtc });
        return await conn.ExecuteScalarAsync<int>(@"
            UPDATE NewsletterIssues SET Status = 'Sending', UpdatedAt = @nowUtc,
                   RecipientCount = (SELECT COUNT(*) FROM NewsletterDeliveries WHERE IssueId = @issueId)
            OUTPUT INSERTED.RecipientCount
            WHERE Id = @issueId AND Status IN ('Queued', 'Sending')", new { issueId, nowUtc });
    }

    public async Task<IReadOnlyList<NewsletterDelivery>> GetDueAsync(Guid issueId, DateTime nowUtc, int take)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<NewsletterDelivery>(@"
            SELECT TOP (@take) d.Id, d.IssueId, d.MemberId, d.Email, d.Status, d.Attempts, d.NextAttemptAt, d.SentAt, d.Error,
                   m.Name AS MemberName, CASE WHEN m.DeletedAt IS NULL THEN m.Status ELSE 'deleted' END AS MemberStatus, m.UnsubscribeToken
            FROM NewsletterDeliveries d LEFT JOIN Members m ON m.Id = d.MemberId
            WHERE d.IssueId = @issueId AND d.Status = 'Pending' AND d.NextAttemptAt <= @nowUtc
            ORDER BY d.Id", new { issueId, nowUtc, take })).ToList();
    }

    public async Task MarkDeliveryAsync(long deliveryId, string status, int attempts, DateTime nextAttemptAt, DateTime? sentAt, string? error)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE NewsletterDeliveries SET Status = @status, Attempts = @attempts, NextAttemptAt = @nextAttemptAt, SentAt = @sentAt, Error = @error
            WHERE Id = @deliveryId AND Status = 'Pending'", new { deliveryId, status, attempts, nextAttemptAt, sentAt, error });
    }

    public async Task<NewsletterIssue?> FinishIfDoneAsync(Guid issueId, DateTime nowUtc)
    {
        using var conn = _ctx.CreateConnection();
        var changed = await conn.ExecuteAsync(@"
            UPDATE NewsletterIssues SET Status = 'Sent', FinishedAt = @nowUtc, UpdatedAt = @nowUtc,
                   SentCount = (SELECT COUNT(*) FROM NewsletterDeliveries WHERE IssueId = @issueId AND Status = 'Sent'),
                   FailedCount = (SELECT COUNT(*) FROM NewsletterDeliveries WHERE IssueId = @issueId AND Status = 'Failed')
            WHERE Id = @issueId AND Status = 'Sending'
              AND NOT EXISTS (SELECT 1 FROM NewsletterDeliveries WHERE IssueId = @issueId AND Status = 'Pending')", new { issueId, nowUtc });
        return changed == 0 ? null : await conn.QuerySingleOrDefaultAsync<NewsletterIssue>(
            $"SELECT {ListColumns} FROM NewsletterIssues WHERE Id = @issueId", new { issueId });
    }
}
