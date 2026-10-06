using System.Security.Claims;
using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Menus. The header and footer navigation menus, each saved as a whole. A site with no header
/// menu keeps the theme's nav-page links; saving a menu empty returns it to that default. Menus are
/// presentation, so they share the Theme permission.
/// </summary>
[Authorize(Policy = "CanManageThemes")]
[Route("admin/menus")]
public class MenusController : Controller
{
    private readonly IMenuRepository _menus;
    private readonly MenuService _menuService;
    private readonly IPageRepository _pages;
    private readonly ICategoryRepository _categories;
    private readonly ITagRepository _tags;
    private readonly ISeriesRepository _series;
    private readonly AuditService _audit;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public MenusController(IMenuRepository menus, MenuService menuService, IPageRepository pages, ICategoryRepository categories,
        ITagRepository tags, ISeriesRepository series, AuditService audit)
    {
        _menus = menus;
        _menuService = menuService;
        _pages = pages;
        _categories = categories;
        _tags = tags;
        _series = series;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var ownerId = await _menuService.ResolveOwnerAsync(HttpContext);
        var model = await BuildModelAsync(ownerId);
        // Resolved rows, so an item whose page was unpublished or deleted says why it is hidden on the site.
        model.Header = ToRows(await _menus.GetResolvedAsync(ownerId, MenuLocations.Header));
        model.Footer = ToRows(await _menus.GetResolvedAsync(ownerId, MenuLocations.Footer));
        ViewData["Title"] = "Menus";
        return View(model);
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string location, string? itemsJson)
    {
        if (!MenuLocations.All.Contains(location)) return BadRequest();
        var ownerId = await _menuService.ResolveOwnerAsync(HttpContext);

        List<MenuRow> rows;
        try { rows = JsonSerializer.Deserialize<List<MenuRow>>(itemsJson ?? "[]", Json) ?? []; }
        catch (JsonException) { rows = []; ModelState.AddModelError("", "The menu could not be read. Reload the page and try again."); }

        var items = ToItems(ownerId, location, rows);
        var model = await BuildModelAsync(ownerId);
        foreach (var r in rows) { r.Error = null; r.Unavailable = false; }

        foreach (var (index, message) in NavigationMenu.Validate(location, items))
        {
            if (index < 0 || index >= rows.Count) ModelState.AddModelError("", message);
            else rows[index].Error ??= message;
        }

        MarkForeignTargets(rows, model.Targets);

        if (!ModelState.IsValid || rows.Any(r => r.Error is not null))
        {
            // Re-show exactly what was submitted; each message travels with its row, so it stays beside
            // that row if the editor then reorders the list.
            model.Header = location == MenuLocations.Header ? rows : ToRows(await _menus.GetResolvedAsync(ownerId, MenuLocations.Header));
            model.Footer = location == MenuLocations.Footer ? rows : ToRows(await _menus.GetResolvedAsync(ownerId, MenuLocations.Footer));
            model.ErrorLocation = location;
            ViewData["Title"] = "Menus";
            return View("Index", model);
        }

        var hadItems = (await _menus.GetItemsAsync(ownerId, location)).Count > 0;
        await _menus.ReplaceAsync(ownerId, location, items);
        _menuService.Invalidate(ownerId, location);

        var name = location == MenuLocations.Header ? "Header menu" : "Footer menu";
        var action = items.Count == 0 ? AuditActions.MenuDeleted : hadItems ? AuditActions.MenuUpdated : AuditActions.MenuCreated;
        if (items.Count > 0 || hadItems)
            await _audit.LogAsync(action, "Menu", location, $"{name}: {items.Count} item(s)");

        TempData["Success"] = items.Count > 0 ? $"{name} saved."
            : hadItems ? $"{name} removed. The site shows its default links again."
            : $"The {name.ToLowerInvariant()} is empty, so the site shows its default links.";
        // Land back on the menu that was saved, not the top of the page.
        return Redirect(Url.Action(nameof(Index)) + $"#menu-{location}-title");
    }

    /// <summary>
    /// A target must be this site's own content. The editor only offers those, so anything else is a
    /// forged or stale id; refusing it keeps another tenant's page titles and slugs out of this navbar
    /// (the resolving read joins by id alone). Marks each such row; returns how many were marked.
    /// </summary>
    public static int MarkForeignTargets(IReadOnlyList<MenuRow> rows, IReadOnlyDictionary<string, List<MenuTarget>> siteTargets)
    {
        var marked = 0;
        foreach (var r in rows)
        {
            if (r.TargetId is not { } targetId || r.Type is null || !siteTargets.TryGetValue(r.Type, out var allowed)) continue;
            if (allowed.Any(t => t.Id == targetId)) continue;
            r.Error ??= $"Choose a {r.Type} from this site; the one saved here is no longer available.";
            marked++;
        }
        return marked;
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Rows arrive in display order; a row marked <see cref="MenuRow.Child"/> is nested under the
    /// nearest top-level row above it. Ids are regenerated on every save — nothing refers to them.
    /// </summary>
    public static List<MenuItem> ToItems(Guid ownerId, string location, IReadOnlyList<MenuRow> rows)
    {
        var now = DateTime.UtcNow;
        var items = new List<MenuItem>();
        Guid? currentParent = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var item = new MenuItem
            {
                Id = Guid.NewGuid(), OwnerId = ownerId, Location = location, SortOrder = i,
                Label = string.IsNullOrWhiteSpace(r.Label) ? null : r.Label.Trim(),
                LinkType = r.Type ?? "", TargetId = r.TargetId,
                Url = r.Type == MenuLinkTypes.Custom ? r.Url?.Trim() : null,
                OpenInNewTab = r.NewTab, CreatedAt = now, UpdatedAt = now,
            };
            if (r.Child && currentParent is { } parent) item.ParentId = parent;
            else currentParent = item.Id;
            items.Add(item);
        }
        return items;
    }

    public static List<MenuRow> ToRows(IReadOnlyList<MenuItem> items)
    {
        // Stored order is parents by SortOrder with children anywhere; show each child under its parent.
        var rows = new List<MenuRow>();
        foreach (var parent in items.Where(i => i.ParentId is null).OrderBy(i => i.SortOrder))
        {
            rows.Add(MenuRow.From(parent, child: false));
            rows.AddRange(items.Where(i => i.ParentId == parent.Id).OrderBy(i => i.SortOrder).Select(c => MenuRow.From(c, child: true)));
        }
        return rows;
    }

    private async Task<MenusViewModel> BuildModelAsync(Guid ownerId)
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : ownerId;
        var pages = (await _pages.GetAllAsync(ownerId)).Concat(userId == ownerId ? [] : await _pages.GetAllAsync(userId))
            .Where(p => p.IsPublished).DistinctBy(p => p.Id).OrderBy(p => p.Title).ToList();

        return new MenusViewModel
        {
            Targets = new Dictionary<string, List<MenuTarget>>
            {
                [MenuLinkTypes.Page]     = pages.Select(p => new MenuTarget(p.Id, p.Title, "/" + p.Slug)).ToList(),
                [MenuLinkTypes.Category] = (await _categories.GetAllAsync(ownerId)).OrderBy(c => c.Name).Select(c => new MenuTarget(c.Id, c.Name, "/category/" + c.Slug)).ToList(),
                [MenuLinkTypes.Tag]      = (await _tags.GetAllAsync(ownerId)).OrderBy(t => t.Name).Select(t => new MenuTarget(t.Id, t.Name, "/tag/" + t.Slug)).ToList(),
                [MenuLinkTypes.Series]   = (await _series.GetAllAsync(ownerId)).OrderBy(s => s.Title).Select(s => new MenuTarget(s.Id, s.Title, "/series/" + s.Slug)).ToList(),
            },
        };
    }
}

/// <summary>One editor row, as the Menus screen posts it (JSON, in display order).</summary>
public sealed class MenuRow
{
    public string? Type { get; set; }
    public Guid? TargetId { get; set; }
    public string? Label { get; set; }
    public string? Url { get; set; }
    public bool NewTab { get; set; }
    /// <summary>Nested under the nearest top-level row above it.</summary>
    public bool Child { get; set; }

    /// <summary>Server to editor only: the target is unpublished or deleted, so the link is hidden on the site.</summary>
    public bool Unavailable { get; set; }

    /// <summary>Server to editor only: why this row was not saved.</summary>
    public string? Error { get; set; }

    public static MenuRow From(MenuItem i, bool child) => new()
    {
        Type = i.LinkType, TargetId = i.TargetId, Label = i.Label, Url = i.Url, NewTab = i.OpenInNewTab, Child = child,
        Unavailable = !i.TargetAvailable,
    };
}

public sealed record MenuTarget(Guid Id, string Title, string Path);

public sealed class MenusViewModel
{
    public List<MenuRow> Header { get; set; } = [];
    public List<MenuRow> Footer { get; set; } = [];
    public Dictionary<string, List<MenuTarget>> Targets { get; set; } = [];
    public string? ErrorLocation { get; set; }
}
