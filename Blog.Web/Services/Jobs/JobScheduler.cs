using Blog.Core.Interfaces;

namespace Blog.Web.Services.Jobs;

/// <summary>Live view of one job for Admin → Dashboard: what the ledger says plus whether it is running now.</summary>
public sealed record JobStatus(string Name, TimeSpan Interval, DateTime? LastStartedAt, DateTime? LastFinishedAt,
    bool LastSucceeded, string? LastMessage, int RunCount, bool IsRunning);

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
    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ledgerWarned = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public JobScheduler(IServiceScopeFactory scopes, ILogger<JobScheduler> log)
    {
        _scopes = scopes;
        _log = log;
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

        foreach (var job in jobs)
        {
            if (ct.IsCancellationRequested) return;
            if (!TryMarkRunning(job.Name)) continue;   // already running in this process (overlapping tick)

            // The claim is the only "is it due" check: one atomic UPDATE that a second process
            // serving the same database cannot also win. The same instant is recorded as the start.
            var started = DateTime.UtcNow;
            bool claimed;
            try { claimed = await ledger.TryClaimAsync(job.Name, started, job.Interval); }
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
                    r?.LastSucceeded ?? false, r?.LastMessage, r?.RunCount ?? 0, running);
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
