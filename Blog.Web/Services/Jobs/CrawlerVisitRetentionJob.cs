using Blog.Core.Interfaces;

namespace Blog.Web.Services.Jobs;

/// <summary>
/// Nightly prune of <c>CrawlerVisits</c>. The table records every identified crawler request and
/// grows without bound on any public site; each owner's retention window comes from their own
/// settings (<c>CrawlerVisitRetentionDays</c>, default 90), so a tenant who never opens Settings
/// is still pruned sensibly.
/// </summary>
public sealed class CrawlerVisitRetentionJob : IScheduledJob
{
    public const int MinDays = 7, MaxDays = 3650, DefaultDays = 90;

    private readonly ICrawlerVisitRepository _visits;
    private readonly ISettingRepository _settings;

    public CrawlerVisitRetentionJob(ICrawlerVisitRepository visits, ISettingRepository settings)
    {
        _visits = visits;
        _settings = settings;
    }

    public string Name => "crawler-visit-retention";
    public TimeSpan Interval => TimeSpan.FromHours(24);

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var owners = await _visits.GetOwnerIdsAsync();
        var removed = 0;
        foreach (var owner in owners)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var days = ClampDays((await _settings.GetSettingsAsync(owner)).CrawlerVisitRetentionDays);
            removed += await _visits.PruneAsync(owner, DateTime.UtcNow.AddDays(-days));
        }
        return $"Pruned {removed} crawler visit(s) across {owners.Count} owner(s).";
    }

    public static int ClampDays(int days) => days <= 0 ? DefaultDays : Math.Clamp(days, MinDays, MaxDays);
}
