using Blog.Core.Interfaces;

namespace Blog.Web.Services.Jobs;

/// <summary>Live view of one job for Admin → Dashboard: what the ledger says plus whether it is running now.</summary>
public sealed record JobStatus(string Name, TimeSpan Interval, DateTime? LastStartedAt, DateTime? LastFinishedAt,
    bool LastSucceeded, string? LastMessage, int RunCount, bool IsRunning,
    bool Paused = false, int? OverrideMinutes = null, bool Locked = false)
{
    /// <summary>The cadence actually in force: the operator's override when set, else the declared interval.</summary>
    public TimeSpan EffectiveInterval => Blog.Core.Services.JobSchedule.EffectiveInterval(Interval, OverrideMinutes);
}

/// <summary>Jobs the Desk may not pause or re-time (appsettings <c>Jobs:LockedJobs</c>), for housekeeping an operator pins.</summary>
public sealed class JobControlOptions
{
    public List<string> LockedJobs { get; set; } = new();
    public bool IsLocked(string name) => LockedJobs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The platform's only scheduler: one hosted service that wakes every minute, asks the Jobs ledger
/// to claim each registered <see cref="IScheduledJob"/> that is due, and runs the claimed ones one
/// at a time inside a fresh DI scope. Nothing here is clever on purpose — no external scheduler, no
/// persistence beyond the Jobs ledger, no parallelism — because the jobs it hosts are nightly
/// housekeeping and a failed tick must never cost more than the tick.
///
/// Guarantees:
///   • never delays startup — the first pass waits <see cref="StartupDelay"/> so the app is serving first;
///   • fail-open — a job that throws is recorded as failed and the scheduler carries on;
///   • catches up — a job that was due while the app-pool was recycled runs on the first tick back;
///   • ledger-driven — "due" is decided from the persisted last start, so a restart cannot re-run
///     a nightly job that already ran today;
///   • single-winner — the claim is one atomic UPDATE on the ledger row, so two processes serving
///     the same database (IIS web garden, two instances behind a balancer, an admin-triggered tick
///     overlapping the timer) cannot both run the same due job.
/// </summary>
public sealed class JobScheduler : BackgroundService
{
    public static readonly TimeSpan TickInterval  = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan StartupDelay  = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<JobScheduler> _log;
    private readonly JobControlOptions _controls;
    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ledgerWarned = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public JobScheduler(IServiceScopeFactory scopes, ILogger<JobScheduler> log, JobControlOptions? controls = null)
    {
        _scopes = scopes;
        _log = log;
        _controls = controls ?? new JobControlOptions();
    }

    /// <summary>
    /// Run one job immediately from the Desk (ADR-0040): same single-winner claim (with a zero interval,
    /// so "due" is simply "not already claimed this instant"), same ledger record, same in-process guard
    /// against overlapping with a timed run. Returns the outcome message, or null when the job is unknown
    /// or could not be claimed (already running elsewhere).
    /// </summary>
    public async Task<(bool ok, string message)?> RunNowAsync(string name, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var job = scope.ServiceProvider.GetServices<IScheduledJob>().FirstOrDefault(j => string.Equals(j.Name, name, StringComparison.OrdinalIgnoreCase));
        if (job is null) return null;
        var ledger = scope.ServiceProvider.GetRequiredService<IJobRunRepository>();
        if (!TryMarkRunning(job.Name)) return (false, "Already running.");
        var started = DateTime.UtcNow;
        try
        {
            // A paused job may still be run by hand: the pause stops the timer, not the operator.
            var row = await ledger.GetAsync(job.Name);
            if (row?.Paused == true) await ledger.SetPausedAsync(job.Name, false);
            var claimed = await ledger.TryClaimAsync(job.Name, started, TimeSpan.Zero);
            if (row?.Paused == true) await ledger.SetPausedAsync(job.Name, true);
            if (!claimed) return (false, "Could not claim the job; another instance may be running it.");

            bool ok; string message;
            try { message = await job.RunAsync(ct); ok = true; }
            catch (Exception ex) { ok = false; message = ex.GetType().Name + ": " + ex.Message; _log.LogWarning(ex, "Job {Job} failed (run now).", job.Name); }
            try { await ledger.RecordAsync(job.Name, started, DateTime.UtcNow, ok, message); } catch (Exception ex) { _log.LogDebug(ex, "Could not record run of {Job}.", job.Name); }
            return (ok, message);
        }
        finally { ClearRunning(job.Name); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Job scheduler tick failed (non-fatal)."); }

            try { await Task.Delay(TickInterval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One pass: run every job this process can claim. Public so a test or an admin action can invoke it directly.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var jobs   = scope.ServiceProvider.GetServices<IScheduledJob>().ToList();
        var ledger = scope.ServiceProvider.GetRequiredService<IJobRunRepository>();

        // Operator controls (ADR-0040): one read per tick; the claim itself still refuses a paused row in SQL.
        Dictionary<string, Blog.Core.Domain.JobRun> rows;
        try { rows = (await ledger.GetAllAsync()).ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase); }
        catch { rows = new Dictionary<string, Blog.Core.Domain.JobRun>(StringComparer.OrdinalIgnoreCase); }

        foreach (var job in jobs)
        {
            if (ct.IsCancellationRequested) return;
            rows.TryGetValue(job.Name, out var row);
            if (row?.Paused == true) continue;          // paused by the operator; nothing is claimed
            if (!TryMarkRunning(job.Name)) continue;   // already running in this process (overlapping tick)

            // The claim is the only "is it due" check: one atomic UPDATE that a second process
            // serving the same database cannot also win. The same instant is recorded as the start.
            var started = DateTime.UtcNow;
            var interval = Blog.Core.Services.JobSchedule.EffectiveInterval(job.Interval, row?.IntervalOverrideMinutes);
            bool claimed;
            try { claimed = await ledger.TryClaimAsync(job.Name, started, interval); }
            catch (Exception ex)
            {
                // No ledger (table missing, DB down): skip rather than run blind. A scheduler that
                // silently never runs is the failure the operator most needs to hear about, so the
                // first skip per job is a warning; the repeats while the outage lasts stay at Debug.
                ClearRunning(job.Name);
                bool first; lock (_gate) first = _ledgerWarned.Add(job.Name);
                if (first) _log.LogWarning(ex, "Job ledger unavailable; {Job} will not run until it is reachable again.", job.Name);
                else _log.LogDebug(ex, "Job ledger still unavailable; skipping {Job} this tick.", job.Name);
                continue;
            }
            lock (_gate) _ledgerWarned.Remove(job.Name);   // reachable again — warn afresh on the next outage
            if (!claimed) { ClearRunning(job.Name); continue; }

            bool ok; string message;
            try
            {
                message = await job.RunAsync(ct);
                ok = true;
                _log.LogInformation("Job {Job} completed: {Message}", job.Name, message);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { ClearRunning(job.Name); throw; }
            catch (Exception ex)
            {
                ok = false;
                message = ex.GetType().Name + ": " + ex.Message;
                _log.LogWarning(ex, "Job {Job} failed.", job.Name);
            }
            finally { ClearRunning(job.Name); }

            try { await ledger.RecordAsync(job.Name, started, DateTime.UtcNow, ok, message); }
            catch (Exception ex) { _log.LogDebug(ex, "Could not record run of {Job}.", job.Name); }
        }
    }

    /// <summary>Ledger rows joined with the registered jobs, for the dashboard. Fail-open: empty on any error.</summary>
    public async Task<IReadOnlyList<JobStatus>> GetStatusAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var jobs   = scope.ServiceProvider.GetServices<IScheduledJob>().ToList();
            var ledger = scope.ServiceProvider.GetRequiredService<IJobRunRepository>();
            var rows   = (await ledger.GetAllAsync()).ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

            return jobs.Select(j =>
            {
                rows.TryGetValue(j.Name, out var r);
                bool running; lock (_gate) running = _running.Contains(j.Name);
                return new JobStatus(j.Name, j.Interval, r?.LastStartedAt, r?.LastFinishedAt,
                    r?.LastSucceeded ?? false, r?.LastMessage, r?.RunCount ?? 0, running,
                    r?.Paused ?? false, r?.IntervalOverrideMinutes, _controls.IsLocked(j.Name));
            }).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Job status unavailable.");
            return Array.Empty<JobStatus>();
        }
    }

    private bool TryMarkRunning(string name) { lock (_gate) return _running.Add(name); }
    private void ClearRunning(string name)   { lock (_gate) _running.Remove(name); }
}
