using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class CrawlerVisitRepository : ICrawlerVisitRepository
{
    private readonly DapperContext _ctx;

    public CrawlerVisitRepository(DapperContext ctx) => _ctx = ctx;

    public async Task RecordAsync(CrawlerVisit v)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO CrawlerVisits (OwnerId, Crawler, Operator, IsAi, Path, StatusCode, UserAgent, VisitedAt)
            VALUES (@OwnerId, @Crawler, @Operator, @IsAi, @Path, @StatusCode, @UserAgent, @VisitedAt);",
            new
            {
                v.OwnerId, v.Crawler, v.Operator, v.IsAi, v.Path, v.StatusCode, v.UserAgent,
                VisitedAt = v.VisitedAt == default ? DateTime.UtcNow : v.VisitedAt
            });
    }

    // "(@OwnerId IS NULL OR OwnerId = @OwnerId)" is a classic SQL Server anti-pattern: with a
    // parameter on both sides of the OR, the optimizer cannot compile a plan that seeks the
    // (OwnerId, VisitedAt) index for the non-null case, so a self-hosted deployment — which always
    // calls these with ownerId = null — took a full scan of CrawlerVisits on every admin page load.
    // Branching the predicate text in C# gives each shape its own sargable, index-friendly query.
    private static string OwnerFilter(Guid? ownerId) => ownerId.HasValue ? "AND OwnerId = @OwnerId" : "";

    public async Task<IReadOnlyList<CrawlerActivity>> GetActivityAsync(Guid? ownerId, int days)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<CrawlerActivity>($@"
            SELECT Crawler,
                   MAX(Operator)          AS [Operator],
                   MAX(CAST(IsAi AS INT)) AS IsAi,
                   COUNT(1)               AS Visits,
                   COUNT(DISTINCT Path)   AS Pages,
                   MAX(VisitedAt)         AS LastSeen
              FROM CrawlerVisits
             WHERE VisitedAt >= @Since {OwnerFilter(ownerId)}
             GROUP BY Crawler
             ORDER BY Visits DESC;",
            new { Since = DateTime.UtcNow.AddDays(-days), OwnerId = ownerId });

        return rows.ToList();
    }

    public async Task<IReadOnlyList<CrawledPage>> GetTopPagesAsync(Guid? ownerId, int days, bool aiOnly = true, int take = 20)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<CrawledPage>($@"
            SELECT TOP (@Take)
                   Path,
                   COUNT(1)                AS Visits,
                   COUNT(DISTINCT Crawler) AS Crawlers
              FROM CrawlerVisits
             WHERE VisitedAt >= @Since
               {OwnerFilter(ownerId)}
               AND (@AiOnly = 0 OR IsAi = 1)
             GROUP BY Path
             ORDER BY Visits DESC;",
            new { Since = DateTime.UtcNow.AddDays(-days), OwnerId = ownerId, AiOnly = aiOnly ? 1 : 0, Take = take });

        return rows.ToList();
    }

    public async Task<IReadOnlyList<CrawlerDay>> GetDailyAiVisitsAsync(Guid? ownerId, int days)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<CrawlerDay>($@"
            SELECT CAST(VisitedAt AS DATE) AS Day, COUNT(1) AS Visits
              FROM CrawlerVisits
             WHERE VisitedAt >= @Since AND IsAi = 1 {OwnerFilter(ownerId)}
             GROUP BY CAST(VisitedAt AS DATE)
             ORDER BY Day;",
            new { Since = DateTime.UtcNow.AddDays(-days), OwnerId = ownerId });

        return rows.ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetOwnerIdsAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Guid>("SELECT DISTINCT OwnerId FROM CrawlerVisits")).ToList();
    }

    public async Task<int> PruneAsync(Guid ownerId, DateTime olderThanUtc)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync(
            "DELETE FROM CrawlerVisits WHERE OwnerId = @OwnerId AND VisitedAt < @Cutoff",
            new { OwnerId = ownerId, Cutoff = olderThanUtc });
    }
}
