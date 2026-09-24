using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class PasswordResetRepository : IPasswordResetRepository
{
    private readonly DapperContext _ctx;
    public PasswordResetRepository(DapperContext ctx) => _ctx = ctx;

    public async Task CreateAsync(PasswordResetToken token)
    {
        using var conn = _ctx.CreateConnection();
        if (token.Id == Guid.Empty) token.Id = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO PasswordResetTokens (Id, UserId, TokenHash, ExpiresAt, UsedAt, CreatedAt, RequestIp)
            VALUES (@Id, @UserId, @TokenHash, @ExpiresAt, @UsedAt, @CreatedAt, @RequestIp)",
            new { token.Id, token.UserId, token.TokenHash, token.ExpiresAt, token.UsedAt, token.CreatedAt, token.RequestIp });
    }

    public async Task<PasswordResetToken?> GetByHashAsync(string tokenHash)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<PasswordResetToken>(
            "SELECT * FROM PasswordResetTokens WHERE TokenHash = @TokenHash", new { TokenHash = tokenHash });
    }

    public async Task MarkUsedAsync(Guid id)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("UPDATE PasswordResetTokens SET UsedAt = @Now WHERE Id = @Id AND UsedAt IS NULL",
            new { Id = id, Now = DateTime.UtcNow });
    }

    public async Task InvalidateForUserAsync(Guid userId)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("UPDATE PasswordResetTokens SET UsedAt = @Now WHERE UserId = @UserId AND UsedAt IS NULL",
            new { UserId = userId, Now = DateTime.UtcNow });
    }

    public async Task<int> CountRecentForUserAsync(Guid userId, TimeSpan window)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM PasswordResetTokens WHERE UserId = @UserId AND CreatedAt >= @Since",
            new { UserId = userId, Since = DateTime.UtcNow - window });
    }

    public async Task<int> PruneExpiredAsync(DateTime olderThanUtc)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync("DELETE FROM PasswordResetTokens WHERE ExpiresAt < @Cutoff", new { Cutoff = olderThanUtc });
    }
}
