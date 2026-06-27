using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class AuditRepository : IAuditRepository
{
    private readonly DapperContext _ctx;

    public AuditRepository(DapperContext ctx) => _ctx = ctx;

    public async Task LogAsync(AuditLog entry)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO AuditLogs
                (OwnerId, UserId, UserName, Action, EntityType, EntityId, EntityName,
                 OldValues, NewValues, IpAddress, UserAgent, CreatedAt)
            VALUES
                (@OwnerId, @UserId, @UserName, @Action, @EntityType, @EntityId, @EntityName,
                 @OldValues, @NewValues, @IpAddress, @UserAgent, @CreatedAt)",
            new
            {
                entry.OwnerId, entry.UserId, entry.UserName,
                entry.Action, entry.EntityType, entry.EntityId, entry.EntityName,
                entry.OldValues, entry.NewValues,
                entry.IpAddress, entry.UserAgent, entry.CreatedAt
            });
    }

    public async Task<(IReadOnlyList<AuditLog> Items, int Total)> GetPagedAsync(
        Guid ownerId, int page, int pageSize,
        string? entityType, string? action, Guid? userId,
        DateTime? from, DateTime? to)
    {
        using var conn = _ctx.CreateConnection();

        var where = new List<string> { "OwnerId = @OwnerId" };
        var p = new DynamicParameters();
        p.Add("OwnerId", ownerId);

        if (!string.IsNullOrEmpty(entityType)) { where.Add("EntityType = @EntityType"); p.Add("EntityType", entityType); }
        if (!string.IsNullOrEmpty(action))     { where.Add("Action = @Action");         p.Add("Action",     action); }
        if (userId.HasValue)                   { where.Add("UserId = @UserId");          p.Add("UserId",     userId.Value); }
        if (from.HasValue)                     { where.Add("CreatedAt >= @From");        p.Add("From",       from.Value); }
        if (to.HasValue)                       { where.Add("CreatedAt < @To");           p.Add("To",         to.Value.AddDays(1)); }

        var whereClause = string.Join(" AND ", where);

        p.Add("Offset",   (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM AuditLogs WHERE {whereClause}", p);

        var items = await conn.QueryAsync<AuditLog>($@"
            SELECT * FROM AuditLogs
            WHERE {whereClause}
            ORDER BY CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", p);

        return (items.ToList(), total);
    }
}
