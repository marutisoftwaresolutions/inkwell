using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class IpFirewallRepository : IIpFirewallRepository
{
    private const string Columns =
        "Id, IpAddress, Kind, Source, Reason, Score, OffenseCount, HitCount, " +
        "LastPath, LastUserAgent, CreatedBy, CreatedAt, ExpiresAt, LastHitAt";

    private readonly DapperContext _ctx;

    public IpFirewallRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<IpFirewallRule>> GetActiveAsync()
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<IpFirewallRule>($@"
            SELECT {Columns} FROM IpFirewallRules
            WHERE Kind = 'Allow' OR ExpiresAt IS NULL OR ExpiresAt > @Now;",
            new { Now = DateTime.UtcNow });
        return rows.ToList();
    }

    public async Task<IpFirewallRule?> GetByIpAsync(string ipAddress)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<IpFirewallRule>(
            $"SELECT {Columns} FROM IpFirewallRules WHERE IpAddress = @ipAddress;", new { ipAddress });
    }

    public async Task<IpFirewallRule?> GetByIdAsync(long id)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<IpFirewallRule>(
            $"SELECT {Columns} FROM IpFirewallRules WHERE Id = @id;", new { id });
    }

    public async Task<IpFirewallRule> UpsertAsync(IpFirewallRule rule)
    {
        using var conn = _ctx.CreateConnection();
        // One rule per address: a re-block of the same IP replaces the previous rule and carries
        // its escalated OffenseCount rather than creating a second row.
        var saved = await conn.QuerySingleAsync<IpFirewallRule>($@"
            MERGE IpFirewallRules AS t
            USING (SELECT @IpAddress AS IpAddress) AS s ON t.IpAddress = s.IpAddress
            WHEN MATCHED THEN UPDATE SET
                Kind          = @Kind,
                Source        = @Source,
                Reason        = @Reason,
                Score         = @Score,
                OffenseCount  = @OffenseCount,
                LastPath      = @LastPath,
                LastUserAgent = @LastUserAgent,
                CreatedBy     = @CreatedBy,
                CreatedAt     = @CreatedAt,
                ExpiresAt     = @ExpiresAt
            WHEN NOT MATCHED THEN INSERT
                (IpAddress, Kind, Source, Reason, Score, OffenseCount, HitCount,
                 LastPath, LastUserAgent, CreatedBy, CreatedAt, ExpiresAt)
            VALUES
                (@IpAddress, @Kind, @Source, @Reason, @Score, @OffenseCount, 0,
                 @LastPath, @LastUserAgent, @CreatedBy, @CreatedAt, @ExpiresAt)
            OUTPUT inserted.Id, inserted.IpAddress, inserted.Kind, inserted.Source, inserted.Reason,
                   inserted.Score, inserted.OffenseCount, inserted.HitCount, inserted.LastPath,
                   inserted.LastUserAgent, inserted.CreatedBy, inserted.CreatedAt,
                   inserted.ExpiresAt, inserted.LastHitAt;",
            new
            {
                rule.IpAddress, rule.Kind, rule.Source, rule.Reason, rule.Score, rule.OffenseCount,
                rule.LastPath, rule.LastUserAgent, rule.CreatedBy,
                CreatedAt = rule.CreatedAt == default ? DateTime.UtcNow : rule.CreatedAt,
                rule.ExpiresAt
            });
        return saved;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync("DELETE FROM IpFirewallRules WHERE Id = @id;", new { id }) > 0;
    }

    public async Task<bool> DeleteByIpAsync(string ipAddress)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync(
            "DELETE FROM IpFirewallRules WHERE IpAddress = @ipAddress;", new { ipAddress }) > 0;
    }

    public async Task RecordHitsAsync(IReadOnlyDictionary<string, int> hits)
    {
        if (hits.Count == 0) return;
        using var conn = _ctx.CreateConnection();
        // Counters are buffered in memory and flushed here, so a flood costs one batched
        // round-trip per refresh interval instead of a write per refused request.
        await conn.ExecuteAsync(@"
            UPDATE IpFirewallRules
               SET HitCount  = HitCount + @Count,
                   LastHitAt = @Now
             WHERE IpAddress = @IpAddress;",
            hits.Select(h => new { IpAddress = h.Key, Count = h.Value, Now = DateTime.UtcNow }).ToList());
    }

    public async Task<(IReadOnlyList<IpFirewallRule> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? kind, string? search)
    {
        using var conn = _ctx.CreateConnection();

        var where = new List<string>();
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(kind)) { where.Add("Kind = @Kind"); p.Add("Kind", kind); }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(IpAddress LIKE @Search OR Reason LIKE @Search OR LastPath LIKE @Search)");
            p.Add("Search", "%" + search.Trim() + "%");
        }
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        p.Add("Offset", (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM IpFirewallRules {whereClause}", p);

        var items = (await conn.QueryAsync<IpFirewallRule>($@"
            SELECT {Columns} FROM IpFirewallRules
            {whereClause}
            ORDER BY CreatedAt DESC, Id DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", p)).ToList();

        return (items, total);
    }

    public async Task<FirewallStats> GetStatsAsync()
    {
        using var conn = _ctx.CreateConnection();
        // Every aggregate is ISNULL-wrapped: SUM over an empty table returns NULL, which would
        // fail to map onto the non-nullable counters on a fresh install with no rules yet.
        return await conn.QuerySingleAsync<FirewallStats>(@"
            SELECT
                ISNULL(SUM(CASE WHEN Kind = 'Block' AND (ExpiresAt IS NULL OR ExpiresAt > @Now) THEN 1 ELSE 0 END), 0) AS ActiveBlocks,
                ISNULL(SUM(CASE WHEN Kind = 'Block' AND ExpiresAt IS NULL THEN 1 ELSE 0 END), 0)                       AS PermanentBlocks,
                ISNULL(SUM(CASE WHEN Kind = 'Block' AND Source = 'Auto'
                                 AND (ExpiresAt IS NULL OR ExpiresAt > @Now) THEN 1 ELSE 0 END), 0)                    AS AutoBlocks,
                ISNULL(SUM(CASE WHEN Kind = 'Block' AND CreatedAt > @DayAgo THEN 1 ELSE 0 END), 0)                     AS BlocksLast24h,
                ISNULL(SUM(CASE WHEN Kind = 'Allow' THEN 1 ELSE 0 END), 0)                                             AS AllowRules,
                ISNULL(SUM(CAST(HitCount AS BIGINT)), 0)                                                               AS RequestsDenied
            FROM IpFirewallRules;",
            new { Now = DateTime.UtcNow, DayAgo = DateTime.UtcNow.AddDays(-1) });
    }

    public async Task<int> PurgeExpiredAsync()
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync(
            "DELETE FROM IpFirewallRules WHERE Kind = 'Block' AND ExpiresAt IS NOT NULL AND ExpiresAt <= @Now;",
            new { Now = DateTime.UtcNow });
    }
}
