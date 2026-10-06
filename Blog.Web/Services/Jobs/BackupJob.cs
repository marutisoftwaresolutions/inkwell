using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services.Backups;

namespace Blog.Web.Services.Jobs;

/// <summary>
/// Makes the backup an admin queued from Admin → Backups (ADR-0019), then applies retention. Checks
/// every minute and does nothing when no backup is queued. A backup runs inside the scheduler rather
/// than a request because a large site takes longer than a request should; while it runs, other jobs
/// wait for the next tick.
/// </summary>
public sealed class BackupJob : IScheduledJob
{
    private readonly BackupService _backups;
    private readonly AuditService _audit;

    public BackupJob(BackupService backups, AuditService audit)
    {
        _backups = backups;
        _audit = audit;
    }

    public string Name => "backup";
    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        _backups.RemoveStalePartials();
        var request = _backups.TakeRequest();
        if (request is null) return "No backup queued.";

        var (name, manifest) = await _backups.CreateAsync(request.IncludeAnalytics, cancellationToken);
        var removed = _backups.Prune();

        var rows = manifest.Tables.Values.Sum();
        await _audit.LogAsync(AuditActions.BackupCreated, "Backup", name, name,
            newValues: $"{rows} rows in {manifest.Tables.Count} tables, {manifest.MediaFiles} media files" +
                       (manifest.IncludesAnalytics ? ", page views included" : ""),
            userIdOverride: request.RequestedBy, userNameOverride: request.RequestedByName);

        return $"Wrote {name}: {rows} rows, {manifest.MediaFiles} media files; removed {removed.Count} old backup(s).";
    }
}
