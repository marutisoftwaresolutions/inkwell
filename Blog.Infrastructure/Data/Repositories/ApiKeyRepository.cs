using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class ApiKeyRepository : IApiKeyRepository
{
    private const string Columns = "Id, UserId, Name, Prefix, KeyHash, Scopes, CreatedAt, ExpiresAt, LastUsedAt, RevokedAt";
    private readonly DapperContext _ctx;
    public ApiKeyRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<Guid> CreateAsync(ApiKey key)
    {
        if (key.Id == Guid.Empty) key.Id = Guid.NewGuid();
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO ApiKeys (Id, UserId, Name, Prefix, KeyHash, Scopes, CreatedAt, ExpiresAt)
            VALUES (@Id, @UserId, @Name, @Prefix, @KeyHash, @Scopes, SYSUTCDATETIME(), @ExpiresAt)", key);
        return key.Id;
    }

    public async Task<ApiKey?> GetByHashAsync(string keyHash)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<ApiKey>(
            $"SELECT {Columns} FROM ApiKeys WHERE KeyHash = @keyHash", new { keyHash });
    }

    public async Task<List<ApiKey>> ListForUserAsync(Guid userId)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<ApiKey>(
            $"SELECT {Columns} FROM ApiKeys WHERE UserId = @userId ORDER BY CreatedAt DESC", new { userId })).ToList();
    }

    public async Task<bool> RevokeAsync(Guid id, Guid userId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync(
            "UPDATE ApiKeys SET RevokedAt = SYSUTCDATETIME() WHERE Id = @id AND UserId = @userId AND RevokedAt IS NULL",
            new { id, userId }) > 0;
    }

    public async Task<int> RevokeAllForUserAsync(Guid userId, DateTime nowUtc)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync(
            "UPDATE ApiKeys SET RevokedAt = @nowUtc WHERE UserId = @userId AND RevokedAt IS NULL AND (ExpiresAt IS NULL OR ExpiresAt > @nowUtc)",
            new { userId, nowUtc });
    }

    public async Task TouchAsync(Guid id, DateTime usedAtUtc)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("UPDATE ApiKeys SET LastUsedAt = @usedAtUtc WHERE Id = @id", new { id, usedAtUtc });
    }

    public async Task<int> CountActiveForUserAsync(Guid userId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM ApiKeys
            WHERE UserId = @userId AND RevokedAt IS NULL AND (ExpiresAt IS NULL OR ExpiresAt > SYSUTCDATETIME())", new { userId });
    }
}
