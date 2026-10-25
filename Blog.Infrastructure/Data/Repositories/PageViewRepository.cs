using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;
using System.Data;

namespace Blog.Infrastructure.Data.Repositories;

public class PageViewRepository : IPageViewRepository
{
    private readonly DapperContext _ctx;
    public PageViewRepository(DapperContext ctx) => _ctx = ctx;

    // Only home page and single-segment post slugs count as content page views.
    // CHARINDEX('/', Path, 2) = 0 means no second slash — excludes /category/x, /tag/x, /admin/x etc.
    // The NOT IN list covers single-segment admin/utility roots that pass the slash check.
    private const string ContentPathFilter =
        "(Path = '/' OR (CHARINDEX('/', Path, 2) = 0 AND Path NOT IN (" +
        "'/feed','/sitemap.xml','/robots.txt'," +
        "'/admin','/account','/newsletter','/avatar','/og-image','/landing','/error'" +
        ")))";

    public async Task<int> RecordManyAsync(IReadOnlyCollection<PageView> views)
    {
        if (views.Count == 0) return 0;
        using var conn = _ctx.CreateConnection();
        using var tx = conn.BeginTransaction();
        var written = await conn.ExecuteAsync(@"
            INSERT INTO PageViews (OwnerId, Path, Referrer, Source, Medium, Campaign, Term, Content, IpHash, UserAgent, Country, Region, CreatedAt)
            VALUES (@OwnerId, @Path, @Referrer, @Source, @Medium, @Campaign, @Term, @Content, @IpHash, @UserAgent, @Country, @Region, @CreatedAt)",
            views, tx);
        tx.Commit();
        return written;
    }

    public async Task<int> GetTotalAsync(Guid ownerId, DateTime from, DateTime to)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM PageViews WHERE OwnerId = @OwnerId AND CreatedAt >= @From AND CreatedAt < @To AND {ContentPathFilter}",
            new { OwnerId = ownerId, From = from, To = to });
    }

    public async Task<IReadOnlyList<(DateTime Date, int Count)>> GetDailyAsync(Guid ownerId, DateTime from, DateTime to)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<(DateTime Date, int Count)>($@"
            SELECT CAST(CreatedAt AS DATE) AS Date, COUNT(1) AS Count
            FROM PageViews
            WHERE OwnerId = @OwnerId AND CreatedAt >= @From AND CreatedAt < @To
              AND {ContentPathFilter}
            GROUP BY CAST(CreatedAt AS DATE)
            ORDER BY Date",
            new { OwnerId = ownerId, From = from, To = to });
        return rows.ToList();
    }

    // Analytics reads the whole report in one call (ADR-0037): the procedure scans the date range once
    // through the covering IX_PageViews_OwnerId_CreatedAt and returns eight result sets in a fixed order.
    // The content-page filter inside the procedure is the same rule as ContentPathFilter above.
    public async Task<AnalyticsReport> GetReportAsync(Guid ownerId, DateTime from, DateTime to, int top = 20)
    {
        using var conn = _ctx.CreateConnection();
        using var grid = await conn.QueryMultipleAsync("dbo.usp_Analytics_Report",
            new { OwnerId = ownerId, From = from, To = to, Top = top },
            commandType: CommandType.StoredProcedure, commandTimeout: ReportTimeoutSeconds);

        return new AnalyticsReport
        {
            Total     = await grid.ReadSingleAsync<int>(),
            Daily     = (await grid.ReadAsync<(DateTime Date, int Count)>()).ToList(),
            Sources   = (await grid.ReadAsync<(string Source, string Medium, int Count)>()).ToList(),
            TopPages  = (await grid.ReadAsync<(string Path, int Views)>()).ToList(),
            Referrers = (await grid.ReadAsync<(string Referrer, int Count)>()).ToList(),
            Campaigns = (await grid.ReadAsync<(string Campaign, int Count)>()).ToList(),
            Countries = (await grid.ReadAsync<(string Country, int Count)>()).ToList(),
            Regions   = (await grid.ReadAsync<(string Region, int Count)>()).ToList(),
        };
    }

    /// <summary>A report read is an admin page load, not the hot path; give it longer than the 30-second default.</summary>
    private const int ReportTimeoutSeconds = 90;

    // Summarises finished days into PageViewDaily (ADR-0037). The procedure serialises itself with an app
    // lock, so a second caller gets (0, 0) instead of inserting the same day twice.
    public async Task<(int Days, int Rows)> RollUpAsync(DateTime throughDay, int maxDays)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleAsync<(int Days, int Rows)>("dbo.usp_PageViews_RollUp",
            new { Through = throughDay.Date, MaxDays = maxDays },
            commandType: CommandType.StoredProcedure, commandTimeout: ReportTimeoutSeconds * 4);
    }

    public async Task<IReadOnlyList<Guid>> GetOwnerIdsAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Guid>("SELECT DISTINCT OwnerId FROM PageViews")).ToList();
    }

    /// <summary>Rows per delete: each is a short transaction, so recording views is never held up for long.</summary>
    private const int PruneBatch = 5_000;

    // Only days already in PageViewDaily are removed: until the roll-up has summarised a day, its raw views are
    // the only record of it. With no summary at all, nothing is removed.
    public async Task<int> PruneAsync(Guid ownerId, DateTime olderThanUtc, TimeSpan budget)
    {
        using var conn = _ctx.CreateConnection();
        var stopAt = DateTime.UtcNow + budget;
        var removed = 0;
        int batch;
        do
        {
            batch = await conn.ExecuteAsync(@"
                DECLARE @Summarised DATETIME2 = DATEADD(DAY, 1, CAST((SELECT MAX([Day]) FROM PageViewDaily) AS DATETIME2));
                DELETE TOP (@Batch) FROM PageViews
                 WHERE OwnerId = @OwnerId AND CreatedAt < @Cutoff AND CreatedAt < @Summarised;",
                new { OwnerId = ownerId, Cutoff = olderThanUtc, Batch = PruneBatch }, commandTimeout: ReportTimeoutSeconds);
            removed += batch;
        } while (batch == PruneBatch && DateTime.UtcNow < stopAt);
        return removed;
    }

    public async Task<IReadOnlyList<PageView>> GetRecentAsync(Guid ownerId, int limit = 200)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<PageView>(@"
            SELECT TOP (@Limit) * FROM PageViews
            WHERE OwnerId = @OwnerId
            ORDER BY CreatedAt DESC",
            new { OwnerId = ownerId, Limit = limit });
        return rows.ToList();
    }
}
