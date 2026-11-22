using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class GoalCountRepository : IGoalCountRepository
{
    private readonly DapperContext _ctx;
    public GoalCountRepository(DapperContext ctx) => _ctx = ctx;

    public async Task AddAsync(IReadOnlyCollection<GoalHit> hits)
    {
        var rows = Goals.Merge(hits);
        if (rows.Count == 0) return;
        using var conn = _ctx.CreateConnection();
        // One row per site, day and goal. The update-then-insert pair is safe to repeat and needs no MERGE;
        // the batched caller is single-flight, and the primary key settles a rare race from a second process.
        foreach (var row in rows)
        {
            await conn.ExecuteAsync(@"
                UPDATE GoalCounts SET Hits = Hits + @Hits WHERE OwnerId = @OwnerId AND Day = @Day AND GoalKey = @GoalKey;
                IF @@ROWCOUNT = 0
                BEGIN
                    BEGIN TRY
                        INSERT INTO GoalCounts (OwnerId, Day, GoalKey, Hits) VALUES (@OwnerId, @Day, @GoalKey, @Hits);
                    END TRY
                    BEGIN CATCH
                        UPDATE GoalCounts SET Hits = Hits + @Hits WHERE OwnerId = @OwnerId AND Day = @Day AND GoalKey = @GoalKey;
                    END CATCH
                END", new { row.OwnerId, Day = row.Day.Date, row.GoalKey, row.Hits });
        }
    }

    public async Task<Dictionary<string, int>> GetTotalsAsync(Guid ownerId, DateTime fromDay, DateTime toDay)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<(string GoalKey, int Hits)>(@"
            SELECT GoalKey, SUM(Hits) AS Hits FROM GoalCounts
            WHERE OwnerId = @ownerId AND Day >= @fromDay AND Day <= @toDay
            GROUP BY GoalKey", new { ownerId, fromDay = fromDay.Date, toDay = toDay.Date });
        return rows.ToDictionary(r => r.GoalKey, r => r.Hits, StringComparer.Ordinal);
    }

    public async Task<List<GoalHit>> GetDailyAsync(Guid ownerId, DateTime fromDay, DateTime toDay)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<GoalHit>(@"
            SELECT OwnerId, CAST(Day AS DATETIME2) AS Day, GoalKey, Hits FROM GoalCounts
            WHERE OwnerId = @ownerId AND Day >= @fromDay AND Day <= @toDay
            ORDER BY Day, GoalKey", new { ownerId, fromDay = fromDay.Date, toDay = toDay.Date })).ToList();
    }

    public async Task<int> DeleteGoalAsync(Guid ownerId, string goalKey)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync("DELETE FROM GoalCounts WHERE OwnerId = @ownerId AND GoalKey = @goalKey", new { ownerId, goalKey });
    }
}
