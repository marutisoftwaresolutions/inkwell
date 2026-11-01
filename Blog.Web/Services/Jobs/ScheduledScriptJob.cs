using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;

namespace Blog.Web.Services.Jobs;

/// <summary>
/// Runs every enabled <see cref="ScheduledScript"/> whose next due time has passed (Admin → Scheduled
/// scripts). One scheduler tick per minute, so a script runs within a minute of its time; a script
/// that was due while the app was stopped runs on the first tick back (its NextRunAt is in the past),
/// once, and is then rescheduled from now. Each script's outcome is recorded on its own row; a failing
/// script never stops the others.
/// </summary>
public sealed class ScheduledScriptJob : IScheduledJob
{
    private readonly IScheduledScriptRepository _scripts;
    private readonly ScheduledScriptRunner _runner;
    private readonly AuditService _audit;

    public ScheduledScriptJob(IScheduledScriptRepository scripts, ScheduledScriptRunner runner, AuditService audit)
    {
        _scripts = scripts; _runner = runner; _audit = audit;
    }

    public string Name => "scheduled-scripts";
    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var due = await _scripts.GetDueAsync(now);
        if (due.Count == 0) return "Nothing due.";

        var ok = 0; var failed = 0;
        foreach (var s in due)
        {
            var result = await _runner.RunAsync(s, cancellationToken);
            var next = ScriptSchedule.NextRun(s, DateTime.UtcNow);
            await _scripts.RecordRunAsync(s.Id, now, result.Succeeded, result.Output, result.DurationMs, next);
            await _audit.LogAsync(result.Succeeded ? AuditActions.ScheduledScriptRan : AuditActions.ScheduledScriptFailed,
                "ScheduledScript", s.Id.ToString(), s.Name,
                newValues: System.Text.Json.JsonSerializer.Serialize(new { s.ScriptFile, result.DurationMs, Trigger = "schedule" }));
            if (result.Succeeded) ok++; else failed++;
        }
        return $"Ran {due.Count} script(s): {ok} succeeded, {failed} failed.";
    }
}
