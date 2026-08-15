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
}
