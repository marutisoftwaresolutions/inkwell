using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Blog.Infrastructure.Data.Repositories;

public class JobRunRepository : IJobRunRepository
{
    private readonly DapperContext _ctx;
    public JobRunRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<JobRun?> GetAsync(string name)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<JobRun>(
            "SELECT Name, LastStartedAt, LastFinishedAt, LastSucceeded, LastMessage, RunCount FROM Jobs WHERE Name = @Name",
            new { Name = name });
    }

    public async Task<IReadOnlyList<JobRun>> GetAllAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<JobRun>(
            "SELECT Name, LastStartedAt, LastFinishedAt, LastSucceeded, LastMessage, RunCount FROM Jobs ORDER BY Name")).ToList();
    }

    public async Task<bool> TryClaimAsync(string name, DateTime nowUtc, TimeSpan interval)
    {
        using var conn = _ctx.CreateConnection();

        // A job that has never run has no row yet. Create it idle so the claim below has something
        // to win; if another process inserts it first the primary key rejects ours, which is fine.
        try
        {
            await conn.ExecuteAsync(@"
                INSERT INTO Jobs (Name, LastStartedAt, LastFinishedAt, LastSucceeded, LastMessage, RunCount)
                SELECT @Name, NULL, NULL, 0, NULL, 0
                WHERE NOT EXISTS (SELECT 1 FROM Jobs WHERE Name = @Name);",
                new { Name = name });
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601) { /* lost the insert race — row exists */ }

        // The claim itself: one statement, so the row lock decides the winner. Only the caller whose
        // UPDATE finds the job still due moves LastStartedAt; everyone else affects zero rows.
        var claimed = await conn.ExecuteAsync(@"
            UPDATE Jobs SET LastStartedAt = @Now
            WHERE Name = @Name AND (LastStartedAt IS NULL OR LastStartedAt <= @DueBefore);",
            new { Name = name, Now = nowUtc, DueBefore = JobSchedule.DueBefore(nowUtc, interval) });
        return claimed == 1;
    }

    public async Task RecordAsync(string name, DateTime startedAtUtc, DateTime finishedAtUtc, bool succeeded, string? message)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            MERGE Jobs AS t
            USING (SELECT @Name AS Name) AS s ON t.Name = s.Name
            WHEN MATCHED THEN UPDATE SET
                LastStartedAt = @StartedAt, LastFinishedAt = @FinishedAt,
                LastSucceeded = @Succeeded, LastMessage = @Message, RunCount = t.RunCount + 1
            WHEN NOT MATCHED THEN INSERT (Name, LastStartedAt, LastFinishedAt, LastSucceeded, LastMessage, RunCount)
                VALUES (@Name, @StartedAt, @FinishedAt, @Succeeded, @Message, 1);",
            new
            {
                Name = name, StartedAt = startedAtUtc, FinishedAt = finishedAtUtc, Succeeded = succeeded,
                Message = message is { Length: > 1000 } ? message[..1000] : message
            });
    }
}
