using System.Security.Claims;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;

namespace Blog.Web.Services;

/// <summary>
/// Writes revisions on save and prunes to the tenant's limit. Fail-open by design: a revision
/// that cannot be written is logged and the save still succeeds — history must never block work.
/// </summary>
public sealed class RevisionService
{
    public const int DefaultKeep = 25, MinKeep = 1, MaxKeep = 200;

    private readonly IRevisionRepository _revisions;
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenant;
    private readonly ILogger<RevisionService> _log;

    public RevisionService(IRevisionRepository revisions, ISettingRepository settings, IUserRepository users,
        ITenantContext tenant, ILogger<RevisionService> log)
    {
        _revisions = revisions;
        _settings = settings;
        _users = users;
        _tenant = tenant;
        _log = log;
    }

    /// <summary>Snapshot the post as it now is. Skipped when nothing an author would call content changed.</summary>
    public Task RecordAsync(Post post, string reason, ClaimsPrincipal user) =>
        RecordAsync(RevisionSnapshot.FromPost(post, reason, UserId(user), UserName(user)));

    public Task RecordAsync(Page page, string reason, ClaimsPrincipal user) =>
        RecordAsync(RevisionSnapshot.FromPage(page, reason, UserId(user), UserName(user)));

    private async Task RecordAsync(Revision candidate)
    {
        try
        {
            var latest = await _revisions.GetLatestAsync(candidate.EntityType, candidate.EntityId);
            if (latest is not null && RevisionSnapshot.SameContent(latest, candidate)) return;

            await _revisions.CreateAsync(candidate);
            await _revisions.PruneAsync(candidate.EntityType, candidate.EntityId, await KeepAsync());
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Revision for {Type} {Id} not written ({Reason}).", candidate.EntityType, candidate.EntityId, candidate.Reason);
        }
    }

    public static int ClampKeep(int keep) => keep <= 0 ? DefaultKeep : Math.Clamp(keep, MinKeep, MaxKeep);

    private async Task<int> KeepAsync()
    {
        try
        {
            var ownerId = _tenant.IsCloudMode && _tenant.IsResolved ? _tenant.UserId
                : (await _users.GetFirstAdminAsync())?.Id ?? Guid.Empty;
            return ClampKeep((await _settings.GetSettingsAsync(ownerId)).RevisionsPerItem);
        }
        catch { return DefaultKeep; }
    }

    private static Guid UserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    private static string? UserName(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name;
}
