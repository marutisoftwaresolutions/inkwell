using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class RedirectRepository : IRedirectRepository
{
    private readonly DapperContext _ctx;
    public RedirectRepository(DapperContext ctx) => _ctx = ctx;

    public async Task UpsertAsync(string from, string to)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            MERGE Redirects AS target
            USING (SELECT @From AS [From], @To AS [To]) AS source
            ON target.[From] = source.[From]
            WHEN MATCHED THEN
                UPDATE SET [To] = source.[To]
            WHEN NOT MATCHED THEN
                INSERT ([From], [To], CreatedAt) VALUES (source.[From], source.[To], GETUTCDATE());",
            new { From = from, To = to });
    }

    public async Task<string?> GetDestinationAsync(string from)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<string?>(
            "SELECT [To] FROM Redirects WHERE [From] = @From",
            new { From = from });
    }
}
