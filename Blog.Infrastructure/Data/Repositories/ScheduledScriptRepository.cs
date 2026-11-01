using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class ScheduledScriptRepository : IScheduledScriptRepository
{
    private const string Columns = "Id, Name, ScriptFile, Frequency, DayOfWeek, TimeOfDayUtc, Enabled, Source, NextRunAt, LastRunAt, LastSucceeded, LastOutput, LastDurationMs, CreatedAt, UpdatedAt";
    private readonly DapperContext _ctx;
    public ScheduledScriptRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<ScheduledScript>> GetAllAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<ScheduledScript>($"SELECT {Columns} FROM ScheduledScripts ORDER BY Source, Name;")).ToList();
    }

    public async Task<ScheduledScript?> GetByIdAsync(Guid id)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ScheduledScript>($"SELECT {Columns} FROM ScheduledScripts WHERE Id = @Id;", new { Id = id });
    }

    public async Task<ScheduledScript?> GetByNameAsync(string name)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ScheduledScript>($"SELECT {Columns} FROM ScheduledScripts WHERE Name = @Name;", new { Name = name });
    }

    public async Task<IReadOnlyList<ScheduledScript>> GetDueAsync(DateTime nowUtc)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<ScheduledScript>(
            $"SELECT {Columns} FROM ScheduledScripts WHERE Enabled = 1 AND NextRunAt IS NOT NULL AND NextRunAt <= @Now ORDER BY NextRunAt;",
            new { Now = nowUtc })).ToList();
    }

    public async Task UpsertAsync(ScheduledScript s)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            MERGE ScheduledScripts AS target
            USING (SELECT @Id AS Id) AS source ON target.Id = source.Id
            WHEN MATCHED THEN UPDATE SET
                Name = @Name, ScriptFile = @ScriptFile, Frequency = @Frequency, DayOfWeek = @DayOfWeek,
                TimeOfDayUtc = @TimeOfDayUtc, Enabled = @Enabled, Source = @Source, NextRunAt = @NextRunAt, UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (Id, Name, ScriptFile, Frequency, DayOfWeek, TimeOfDayUtc, Enabled, Source, NextRunAt, CreatedAt, UpdatedAt)
                VALUES (@Id, @Name, @ScriptFile, @Frequency, @DayOfWeek, @TimeOfDayUtc, @Enabled, @Source, @NextRunAt, SYSUTCDATETIME(), SYSUTCDATETIME());",
            new { s.Id, s.Name, s.ScriptFile, s.Frequency, s.DayOfWeek, s.TimeOfDayUtc, s.Enabled, s.Source, s.NextRunAt });
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync("DELETE FROM ScheduledScripts WHERE Id = @Id;", new { Id = id }) > 0;
    }

    public async Task RecordRunAsync(Guid id, DateTime ranAtUtc, bool succeeded, string output, int durationMs, DateTime nextRunAtUtc)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE ScheduledScripts SET LastRunAt = @RanAt, LastSucceeded = @Succeeded, LastOutput = @Output,
                   LastDurationMs = @DurationMs, NextRunAt = @NextRunAt, UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id;",
            new { Id = id, RanAt = ranAtUtc, Succeeded = succeeded, Output = output, DurationMs = durationMs, NextRunAt = nextRunAtUtc });
    }
}
