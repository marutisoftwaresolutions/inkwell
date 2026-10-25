using Blog.Core.Interfaces;
using Blog.Web.Services.Export.Push;

namespace Blog.Web.Services.Jobs;

/// <summary>
/// Works through running pushes to WordPress and Ghost (ADR-0029), a batch per run
/// (<c>Exports:PushBatchSize</c>, default 25, within a 40-second budget) so a large site goes over several
/// minutes without holding the single scheduler. Nothing to do costs one query.
/// </summary>
public sealed class ExportPushJob : IScheduledJob
{
    private readonly ExportPushService _push;

    public ExportPushJob(ExportPushService push) => _push = push;

    public string Name => "export-push";
    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var pruned = await _push.PruneFinishedAsync();
        var r = await _push.RunAsync(cancellationToken);
        if (pruned > 0 && r.Sent + r.Skipped + r.Retrying + r.Failed + r.Finished + r.Paused == 0) return $"Removed {pruned} finished push(es) older than {ExportPushService.KeepFinishedFor.TotalDays:0} days.";
        if (r.Sent + r.Skipped + r.Retrying + r.Failed + r.Finished + r.Paused == 0) return "No push running.";
        return $"Push: sent {r.Sent}, skipped {r.Skipped}, will retry {r.Retrying}, failed {r.Failed}; {r.Finished} finished, {r.Paused} paused.";
    }
}
