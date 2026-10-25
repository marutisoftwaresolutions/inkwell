using System.Security.Claims;
using Blog.Core.Interfaces;
using Blog.Web.Models;
using Microsoft.AspNetCore.Routing;

namespace Blog.Web.Services;

/// <summary>
/// Builds the <see cref="DeskShellModel"/> for the admin layout once per request: site name and
/// colour scheme from the tenant settings, the signed-in user's avatar, the navigation visible to
/// this user, and the counts shown as sidebar badges (comments awaiting moderation; posts waiting
/// for review when the user may publish). The layout injects this service instead of querying
/// repositories itself (architecture rule: views never query). Fails open: any lookup that throws
/// leaves its default in place so the Desk still renders when the database is unavailable.
/// </summary>
public sealed class DeskShellService
{
    private readonly IUserRepository _users;
    private readonly ISettingRepository _settings;
    private readonly ICommentRepository _comments;
    private readonly IPostRepository _posts;
    private readonly ITenantContext _tenant;
    private DeskShellModel? _cached;

    public DeskShellService(IUserRepository users, ISettingRepository settings, ICommentRepository comments, IPostRepository posts, ITenantContext tenant)
    {
        _users = users;
        _settings = settings;
        _comments = comments;
        _posts = posts;
        _tenant = tenant;
    }

    public async Task<DeskShellModel> GetAsync(ClaimsPrincipal user, RouteValueDictionary route)
    {
        if (_cached is not null) return _cached;

        string siteName = "Inkwell", scheme = "system";
        string avatar = "/avatar/" + System.Net.WebUtility.UrlEncode(user.Identity?.Name ?? "A");
        int pending = 0, review = 0;
        bool cloud = false;
        try { cloud = _tenant.IsCloudMode; } catch { /* no tenant: single-site defaults */ }

        try
        {
            var primaryAdmin = await _users.GetFirstAdminAsync();
            var settings = await _settings.GetSettingsAsync(primaryAdmin?.Id ?? Guid.Empty);
            if (!string.IsNullOrWhiteSpace(settings?.SiteName)) siteName = settings!.SiteName;
            if (settings?.Theme is "light" or "dark") scheme = settings!.Theme;
        }
        catch { /* settings unavailable: defaults */ }

        try
        {
            if (Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                var u = await _users.GetByIdAsync(uid);
                if (!string.IsNullOrEmpty(u?.AvatarUrl)) avatar = u!.AvatarUrl!;
            }
        }
        catch { /* avatar falls back to the generated initial */ }

        try { pending = await _comments.GetPendingCountAsync(); } catch { /* badge stays at 0 */ }
        try
        {
            if (user.HasClaim("Permission", "posts.publish"))
                review = await _posts.GetTotalCountAsync(Blog.Core.Domain.PostStatus.InReview, null);
        }
        catch { /* badge stays at 0 */ }

        var controller = route["controller"]?.ToString();
        var action = route["action"]?.ToString();
        var admin = DeskNavigation.IsAdmin(user);

        return _cached = new DeskShellModel
        {
            SiteName = siteName,
            ColorScheme = scheme,
            UserName = user.Identity?.Name ?? "",
            AvatarUrl = avatar,
            RoleLabel = user.FindFirst(ClaimTypes.Role)?.Value ?? "",
            IsCloudMode = cloud,
            PendingComments = pending,
            ReviewQueue = review,
            Items = DeskNavigation.Visible(user, cloud).ToList(),
            Commands = DeskNavigation.Commands.Where(c => !c.AdminOnly || admin).ToList(),
            Current = DeskNavigation.Current(controller, action),
        };
    }
}
