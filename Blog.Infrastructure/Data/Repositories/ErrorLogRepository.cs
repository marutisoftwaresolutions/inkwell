using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class ErrorLogRepository : IErrorLogRepository
{
    private readonly DapperContext _ctx;

    public ErrorLogRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<bool> RecordAsync(ErrorLog e)
    {
        using var conn = _ctx.CreateConnection();
        // Upsert by Fingerprint so repeated/similar errors collapse into one counted group.
        // OUTPUT $action tells us whether this was an INSERT (new signature) or UPDATE (repeat).
        var action = await conn.ExecuteScalarAsync<string>(@"
            MERGE ErrorLogs AS t
            USING (SELECT @Fingerprint AS Fingerprint) AS s ON t.Fingerprint = s.Fingerprint
            WHEN MATCHED THEN UPDATE SET
                OccurrenceCount = t.OccurrenceCount + 1,
                LastSeenAt      = @Now,
                Method          = @Method,
                Path            = @Path,
                Message         = @Message,
                StackTrace      = @StackTrace,
                UserAgent       = @UserAgent,
                Referer         = @Referer,
                LastIpAddress   = @LastIpAddress
            WHEN NOT MATCHED THEN INSERT
                (Fingerprint, StatusCode, Method, Path, ExceptionType, Message, StackTrace,
                 UserAgent, Referer, LastIpAddress, OccurrenceCount, FirstSeenAt, LastSeenAt)
            VALUES
                (@Fingerprint, @StatusCode, @Method, @Path, @ExceptionType, @Message, @StackTrace,
                 @UserAgent, @Referer, @LastIpAddress, 1, @Now, @Now)
            OUTPUT $action;",
            new
            {
                e.Fingerprint, e.StatusCode, e.Method, e.Path, e.ExceptionType,
                e.Message, e.StackTrace, e.UserAgent, e.Referer, e.LastIpAddress,
                Now = DateTime.UtcNow
            });
        return string.Equals(action, "INSERT", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<(IReadOnlyList<ErrorLog> Items, int Total)> GetPagedAsync(
        int page, int pageSize, int? statusCode, string? search)
    {
        using var conn = _ctx.CreateConnection();

        var where = new List<string>();
        var p = new DynamicParameters();
        if (statusCode.HasValue) { where.Add("StatusCode = @StatusCode"); p.Add("StatusCode", statusCode.Value); }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(Path LIKE @Search OR ExceptionType LIKE @Search OR Message LIKE @Search)");
            p.Add("Search", "%" + search.Trim() + "%");
        }
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        p.Add("Offset", (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM ErrorLogs {whereClause}", p);

        var items = (await conn.QueryAsync<ErrorLog>($@"
            SELECT * FROM ErrorLogs
            {whereClause}
            ORDER BY LastSeenAt DESC, OccurrenceCount DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", p)).ToList();

        return (items, total);
    }

    public async Task<ErrorStats> GetStatsAsync()
    {
        using var conn = _ctx.CreateConnection();
        var row = await conn.QuerySingleAsync<ErrorStats>(@"
            SELECT
                COUNT(1)                                                         AS TotalGroups,
                ISNULL(SUM(OccurrenceCount), 0)                                  AS TotalOccurrences,
                SUM(CASE WHEN StatusCode = 404 THEN 1 ELSE 0 END)                AS NotFoundGroups,
                SUM(CASE WHEN StatusCode >= 500 THEN 1 ELSE 0 END)               AS ServerErrorGroups
            FROM ErrorLogs;");
        return row;
    }
}
