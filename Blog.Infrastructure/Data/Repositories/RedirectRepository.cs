using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class RedirectRepository : IRedirectRepository
{
    private readonly DapperContext _ctx;
    public RedirectRepository(DapperContext ctx) => _ctx = ctx;

    public async Task UpsertAsync(string from, string to, int statusCode = RedirectStatus.MovedPermanently)
    {
        if (!RedirectStatus.IsSupported(statusCode)) statusCode = RedirectStatus.MovedPermanently;

        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            MERGE Redirects AS target
            USING (SELECT @From AS [From], @To AS [To], @StatusCode AS StatusCode) AS source
            ON target.[From] = source.[From]
            WHEN MATCHED THEN
                UPDATE SET [To] = source.[To], StatusCode = source.StatusCode
            WHEN NOT MATCHED THEN
                INSERT ([From], [To], StatusCode, CreatedAt)
                VALUES (source.[From], source.[To], source.StatusCode, GETUTCDATE());",
            new { From = from, To = to, StatusCode = statusCode });
    }

    public async Task<string?> GetDestinationAsync(string from)
    {
        var rule = await GetRuleAsync(from);
        // A 410 has no destination — returning its empty [To] would send callers into a redirect loop.
        return rule is null || rule.IsGone || string.IsNullOrWhiteSpace(rule.To) ? null : rule.To;
    }

    public async Task<RedirectRule?> GetRuleAsync(string from)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<RedirectRule>(
            "SELECT [From], [To], StatusCode FROM Redirects WHERE [From] = @From;",
            new { From = from });
    }

    public async Task<IReadOnlyList<RedirectRule>> GetAllAsync()
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<RedirectRule>(
            "SELECT Id, [From], [To], StatusCode, CreatedAt FROM Redirects ORDER BY CreatedAt DESC;");

        return rows.ToList();
    }

    public async Task<bool> DeleteAsync(string from)
    {
        using var conn = _ctx.CreateConnection();
        var affected = await conn.ExecuteAsync(
            "DELETE FROM Redirects WHERE [From] = @From;", new { From = from });

        return affected > 0;
    }

    public async Task<IReadOnlyList<NotFoundGroup>> GetUnhandledNotFoundsAsync(int take = 200)
    {
        using var conn = _ctx.CreateConnection();

        // A path that already has a rule is handled; re-listing it would invite an operator to write
        // a second rule for the same URL.
        var rows = await conn.QueryAsync<NotFoundGroup>(@"
            SELECT TOP (@Take)
                   e.Path,
                   SUM(e.OccurrenceCount) AS Hits,
                   MAX(e.LastSeenAt)      AS LastSeenAt,
                   MAX(e.Referer)         AS Referer
              FROM ErrorLogs e
             WHERE e.StatusCode = 404
               AND NOT EXISTS (SELECT 1 FROM Redirects r WHERE r.[From] = e.Path)
             GROUP BY e.Path
             ORDER BY MAX(e.LastSeenAt) DESC;",
            new { Take = take });

        return rows.ToList();
    }

    public async Task<IReadOnlyList<string>> GetPublishedSlugsAsync()
    {
        using var conn = _ctx.CreateConnection();
        // Status is stored as its enum *name*, and a scheduled post is live once its date passes —
        // matching how PostRepository selects public posts, so a suggestion can never point at a
        // slug the reader would also get a 404 from.
        var rows = await conn.QueryAsync<string>(@"
            SELECT Slug FROM Posts
             WHERE Slug IS NOT NULL AND LEN(Slug) > 0
               AND ((Status = 'Published' AND PublishedAt <= GETUTCDATE())
                 OR (Status = 'Scheduled' AND ScheduledAt <= GETUTCDATE()));");

        return rows.ToList();
    }
}
