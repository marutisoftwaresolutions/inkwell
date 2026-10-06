using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Blog.Web.Services;

/// <summary>
/// Reads the header and footer menus for rendering, cached per site for a minute (the navbar renders
/// on every public page), and resolves which site a request belongs to.
///
/// Menus are stored under the same owner as theme settings — the "branding" owner the public layout
/// passes to the navbar and footer: the tenant in cloud mode, otherwise the first administrator.
///
/// Fail-open: any failure returns "no menu", and the navbar falls back to its pre-menu behaviour
/// (nav pages). A menu can never be the reason a page does not render.
/// </summary>
public sealed class MenuService
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    private readonly IMenuRepository _menus;
    private readonly IUserRepository _users;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MenuService> _log;

    public MenuService(IMenuRepository menus, IUserRepository users, IMemoryCache cache, ILogger<MenuService> log)
    {
        _menus = menus;
        _users = users;
        _cache = cache;
        _log = log;
    }

    private static string CacheKey(Guid ownerId, string location) => $"menu:{ownerId:N}:{location}";

    /// <summary>The resolved links for a menu, or an empty list when it has none (or cannot be read).</summary>
    public async Task<IReadOnlyList<NavLink>> GetLinksAsync(Guid ownerId, string location)
    {
        var key = CacheKey(ownerId, location);
        if (_cache.TryGetValue(key, out IReadOnlyList<NavLink>? cached) && cached is not null) return cached;

        try
        {
            var links = NavigationMenu.Resolve(await _menus.GetResolvedAsync(ownerId, location));
            _cache.Set(key, links, CacheLifetime);
            return links;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Menu {Location} could not be read; falling back to the theme's default links.", location);
            return [];
        }
    }

    /// <summary>Call after saving a menu so the next page view shows it.</summary>
    public void Invalidate(Guid ownerId, string location) => _cache.Remove(CacheKey(ownerId, location));

    /// <summary>The site a Desk request edits menus for; matches what the public layout renders.</summary>
    public async Task<Guid> ResolveOwnerAsync(HttpContext context)
    {
        var tenant = context.RequestServices.GetService<ITenantContext>();
        if (tenant is { IsCloudMode: true, IsResolved: true }) return tenant.UserId;
        return (await _users.GetFirstAdminAsync())?.Id ?? Guid.Empty;
    }
}
