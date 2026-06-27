using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

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

    public async Task RecordAsync(PageView view)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO PageViews (OwnerId, Path, Referrer, Source, Medium, Campaign, Term, Content, IpHash, UserAgent, Country, Region, CreatedAt)
            VALUES (@OwnerId, @Path, @Referrer, @Source, @Medium, @Campaign, @Term, @Content, @IpHash, @UserAgent, @Country, @Region, @CreatedAt)",
            new
            {
                view.OwnerId,
                view.Path,
                view.Referrer,
                view.Source,
                view.Medium,
                view.Campaign,
                view.Term,
                view.Content,
                view.IpHash,
                view.UserAgent,
                view.Country,
                view.Region,
                view.CreatedAt
            });
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

    public async Task<IReadOnlyList<(string Source, string Medium, int Count)>> GetSourceBreakdownAsync(Guid ownerId, DateTime from, DateTime to)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<(string Source, string Medium, int Count)>($@"
            SELECT ISNULL(Source, 'direct') AS Source,
                   ISNULL(Medium, 'none')   AS Medium,
                   COUNT(1)                 AS Count
            FROM PageViews
            WHERE OwnerId = @OwnerId AND CreatedAt >= @From AND CreatedAt < @To
              AND {ContentPathFilter}
            GROUP BY ISNULL(Source, 'direct'), ISNULL(Medium, 'none')
            ORDER BY Count DESC",
            new { OwnerId = ownerId, From = from, To = to });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<(string Path, int Views)>> GetTopPagesAsync(Guid ownerId, DateTime from, DateTime to, int limit = 20)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<(string Path, int Views)>($@"
            SELECT TOP (@Limit) Path, COUNT(1) AS Views
            FROM PageViews
            WHERE OwnerId = @OwnerId AND CreatedAt >= @From AND CreatedAt < @To
              AND {ContentPathFilter}
            GROUP BY Path
            ORDER BY Views DESC",
            new { OwnerId = ownerId, From = from, To = to, Limit = limit });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<(string Referrer, int Count)>> GetTopReferrersAsync(Guid ownerId, DateTime from, DateTime to, int limit = 20)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<(string Referrer, int Count)>($@"
            SELECT TOP (@Limit) ISNULL(Referrer, '(direct)') AS Referrer, COUNT(1) AS Count
            FROM PageViews
            WHERE OwnerId = @OwnerId AND CreatedAt >= @From AND CreatedAt < @To
              AND Referrer IS NOT NULL AND {ContentPathFilter}
            GROUP BY Referrer
            ORDER BY Count DESC",
            new { OwnerId = ownerId, From = from, To = to, Limit = limit });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<(string Campaign, int Count)>> GetCampaignsAsync(Guid ownerId, DateTime from, DateTime to)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<(string Campaign, int Count)>($@"
            SELECT ISNULL(Campaign, '(none)') AS Campaign, COUNT(1) AS Count
            FROM PageViews
            WHERE OwnerId = @OwnerId AND CreatedAt >= @From AND CreatedAt < @To
              AND Campaign IS NOT NULL AND {ContentPathFilter}
            GROUP BY Campaign
            ORDER BY Count DESC",
            new { OwnerId = ownerId, From = from, To = to });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<(string Country, int Count)>> GetCountryBreakdownAsync(Guid ownerId, DateTime from, DateTime to, int limit = 20)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<(string Country, int Count)>($@"
            SELECT TOP (@Limit) ISNULL(Country, 'Unknown') AS Country, COUNT(1) AS Count
            FROM PageViews
            WHERE OwnerId = @OwnerId AND CreatedAt >= @From AND CreatedAt < @To
              AND Country IS NOT NULL AND {ContentPathFilter}
            GROUP BY Country
            ORDER BY Count DESC",
            new { OwnerId = ownerId, From = from, To = to, Limit = limit });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<(string Region, int Count)>> GetRegionBreakdownAsync(Guid ownerId, DateTime from, DateTime to, int limit = 20)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<(string Region, int Count)>($@"
            SELECT TOP (@Limit) ISNULL(Region, 'Unknown') AS Region, COUNT(1) AS Count
            FROM PageViews
            WHERE OwnerId = @OwnerId AND CreatedAt >= @From AND CreatedAt < @To
              AND Region IS NOT NULL AND {ContentPathFilter}
            GROUP BY Region
            ORDER BY Count DESC",
            new { OwnerId = ownerId, From = from, To = to, Limit = limit });
        return rows.ToList();
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
