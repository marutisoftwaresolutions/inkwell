using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;
using System.Data;

namespace Blog.Infrastructure.Data.Repositories;

public class CrawlerVisitRepository : ICrawlerVisitRepository
{
    private readonly DapperContext _ctx;

    public CrawlerVisitRepository(DapperContext ctx) => _ctx = ctx;

    public async Task RecordAsync(CrawlerVisit v)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO CrawlerVisits (OwnerId, Crawler, Operator, IsAi, Path, Query, StatusCode, UserAgent, VisitedAt, Verified)
            VALUES (@OwnerId, @Crawler, @Operator, @IsAi, @Path, @Query, @StatusCode, @UserAgent, @VisitedAt, @Verified);",
            new
            {
                v.OwnerId, v.Crawler, v.Operator, v.IsAi, v.Path, v.Query, v.StatusCode, v.UserAgent, v.Verified,
                VisitedAt = v.VisitedAt == default ? DateTime.UtcNow : v.VisitedAt
            });
    }

    public async Task<int> RecordManyAsync(IReadOnlyCollection<CrawlerVisit> visits)
    {
        if (visits.Count == 0) return 0;
        using var conn = _ctx.CreateConnection();
        using var tx = conn.BeginTransaction();
        var written = await conn.ExecuteAsync(@"
            INSERT INTO CrawlerVisits (OwnerId, Crawler, Operator, IsAi, Path, Query, StatusCode, UserAgent, VisitedAt, Verified)
            VALUES (@OwnerId, @Crawler, @Operator, @IsAi, @Path, @Query, @StatusCode, @UserAgent, @VisitedAt, @Verified);",
            visits.Select(v => new
            {
                v.OwnerId, v.Crawler, v.Operator, v.IsAi, v.Path, v.Query, v.StatusCode, v.UserAgent, v.Verified,
                VisitedAt = v.VisitedAt == default ? DateTime.UtcNow : v.VisitedAt
            }), tx);
        tx.Commit();
        return written;
    }

    // AI Crawlers reads the whole report in one call (ADR-0037). The procedure collapses the window once,
    // through the covering IX_CrawlerVisits_VisitedAt, to one row per crawler x path x day x check result,
    // and answers the four sections from that; only the query-string section goes back to the table.
    // Self-hosted calls pass a null owner: the date is the leading index key, so no owner predicate is
    // needed to seek (the reason the old per-section queries scanned the whole table).
    public async Task<CrawlerReport> GetReportAsync(Guid? ownerId, int days, int take = 20)
    {
        using var conn = _ctx.CreateConnection();
        using var grid = await conn.QueryMultipleAsync("dbo.usp_CrawlerVisits_Report",
            new { OwnerId = ownerId, SinceDay = DateTime.UtcNow.Date.AddDays(-(days - 1)), Take = take },
            commandType: CommandType.StoredProcedure, commandTimeout: ReportTimeoutSeconds);

        return new CrawlerReport
        {
            Activity = (await grid.ReadAsync<CrawlerActivity>()).ToList(),
            TopPages = (await grid.ReadAsync<CrawledPage>()).ToList(),
            Queried  = (await grid.ReadAsync<CrawledRequest>()).ToList(),
            Daily    = (await grid.ReadAsync<CrawlerDay>()).ToList(),
        };
    }

    /// <summary>A report read is an admin page load, not the hot path; give it longer than the 30-second default.</summary>
    private const int ReportTimeoutSeconds = 90;

    // Summarises finished days into CrawlerVisitDaily (ADR-0037). The procedure serialises itself with an app
    // lock, so a second caller gets (0, 0) instead of inserting the same day twice.
    public async Task<(int Days, int Rows)> RollUpAsync(DateTime throughDay, int maxDays)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleAsync<(int Days, int Rows)>("dbo.usp_CrawlerVisits_RollUp",
            new { Through = throughDay.Date, MaxDays = maxDays },
            commandType: CommandType.StoredProcedure, commandTimeout: ReportTimeoutSeconds * 4);
    }

    public async Task<IReadOnlyList<Guid>> GetOwnerIdsAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Guid>("SELECT DISTINCT OwnerId FROM CrawlerVisits")).ToList();
    }

    public async Task<int> PruneAsync(Guid ownerId, DateTime olderThanUtc)
    {
        using var conn = _ctx.CreateConnection();
        // The daily summary follows the same window; it is keyed by day, so a partly expired day is kept.
        await conn.ExecuteAsync(
            "DELETE FROM CrawlerVisitDaily WHERE OwnerId = @OwnerId AND [Day] < @CutoffDay",
            new { OwnerId = ownerId, CutoffDay = olderThanUtc.Date });
        return await conn.ExecuteAsync(
            "DELETE FROM CrawlerVisits WHERE OwnerId = @OwnerId AND VisitedAt < @Cutoff",
            new { OwnerId = ownerId, Cutoff = olderThanUtc });
    }
}
