using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// Turns stored menu items into the ordered links a navbar or footer renders, and validates what an
/// editor submits. Pure, so the backward-compatibility promise is testable: a site with no header menu
/// gets exactly the links it got before menus existed (<see cref="FromPages"/>).
/// </summary>
public static class NavigationMenu
{
    public const int MaxItemsPerMenu = 40;
    /// <summary>Short enough to sit in any theme's header bar without overflowing at tablet width.</summary>
    public const int MaxLabelLength = 40;
    public const int MaxUrlLength = 2048;

    /// <summary>
    /// The pre-menu behaviour: nav pages in sort order, each linking to /{slug} — exactly what seven of the
    /// eight navbars rendered before menus existed. (The Feed navbar linked /pages/{slug}, a route that never
    /// existed; it now gets /{slug} too, which fixes its broken page links.)
    /// </summary>
    public static IReadOnlyList<NavLink> FromPages(IEnumerable<Page> navPages) =>
        navPages.OrderBy(p => p.SortOrder)
                .Select(p => new NavLink(p.Title, "/" + p.Slug, 0, false))
                .ToList();

    /// <summary>
    /// Ordered links from resolved items: top-level items by sort order, each followed by its children
    /// by sort order. Items whose target is gone or unpublished are skipped (a child of a skipped parent
    /// is skipped with it, so nothing appears in the wrong place). Titles are left clean: views mark a
    /// child (Depth 1) themselves, visually only, so screen readers do not read out a dash.
    /// </summary>
    public static IReadOnlyList<NavLink> Resolve(IEnumerable<MenuItem> items)
    {
        var list = items.ToList();
        var topLevel = list.Where(i => i.ParentId is null).OrderBy(i => i.SortOrder).ToList();
        var links = new List<NavLink>();

        foreach (var parent in topLevel)
        {
            var link = ToLink(parent, depth: 0);
            if (link is null) continue;
            links.Add(link);

            foreach (var child in list.Where(i => i.ParentId == parent.Id).OrderBy(i => i.SortOrder))
            {
                var childLink = ToLink(child, depth: 1);
                if (childLink is not null) links.Add(childLink);
            }
        }
        return links;
    }

    private static NavLink? ToLink(MenuItem item, int depth)
    {
        string? href = item.LinkType switch
        {
            MenuLinkTypes.Home     => "/",
            MenuLinkTypes.Page     => SlugHref("/", item),
            MenuLinkTypes.Category => SlugHref("/category/", item),
            MenuLinkTypes.Tag      => SlugHref("/tag/", item),
            MenuLinkTypes.Series   => SlugHref("/series/", item),
            MenuLinkTypes.Custom   => ValidateCustomUrl(item.Url) is null ? item.Url!.Trim() : null,
            _                      => null,
        };
        if (href is null) return null;

        var title = !string.IsNullOrWhiteSpace(item.Label) ? item.Label.Trim()
            : item.LinkType == MenuLinkTypes.Home ? "Home"
            : !string.IsNullOrWhiteSpace(item.TargetTitle) ? item.TargetTitle
            : item.LinkType == MenuLinkTypes.Custom ? href
            : null;
        if (title is null) return null;

        return new NavLink(title, href, depth, item.OpenInNewTab);
    }

    private static string? SlugHref(string prefix, MenuItem item) =>
        item.TargetAvailable && !string.IsNullOrWhiteSpace(item.TargetSlug) ? prefix + item.TargetSlug : null;

    /// <summary>
    /// A custom link may be a site path ("/about", "/search?q=x") or an absolute http(s) address.
    /// Anything else — javascript:, data:, protocol-relative "//host", backslashes — is refused, because
    /// it would render as a link on every page. Returns null when valid.
    /// </summary>
    public static string? ValidateCustomUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Enter the address the link should open.";
        url = url.Trim();
        if (url.Length > MaxUrlLength) return $"Addresses can be at most {MaxUrlLength} characters.";
        if (url.Contains('\\') || url.Any(char.IsControl)) return "Enter a path starting with / or a full address starting with https://.";

        if (url.StartsWith('/'))
            return url.StartsWith("//") ? "Enter a path starting with a single /, or a full address starting with https://." : null;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? null
            : "Enter a path starting with / or a full address starting with https://.";
    }

    /// <summary>
    /// Checks a whole submitted menu: known types, a target where one is needed, valid custom links,
    /// label length, one level of nesting, and the item cap. Returns one message per problem, keyed by
    /// the item's position, empty when valid. Home is a footer-only choice: every navbar theme already
    /// carries its own Home link.
    /// </summary>
    public static IReadOnlyList<(int Index, string Message)> Validate(string location, IReadOnlyList<MenuItem> items)
    {
        var errors = new List<(int, string)>();
        if (!MenuLocations.All.Contains(location)) errors.Add((-1, "Unknown menu."));
        if (items.Count > MaxItemsPerMenu) errors.Add((-1, $"A menu can have at most {MaxItemsPerMenu} items."));

        var ids = items.Select(i => i.Id).ToHashSet();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!MenuLinkTypes.All.Contains(item.LinkType)) { errors.Add((i, "Choose what this item links to.")); continue; }
            if (item.LinkType == MenuLinkTypes.Home && location == MenuLocations.Header)
                errors.Add((i, "The header already has a Home link in every theme; add Home to the footer menu instead."));
            if (item.LinkType is MenuLinkTypes.Page or MenuLinkTypes.Category or MenuLinkTypes.Tag or MenuLinkTypes.Series && item.TargetId is null)
                errors.Add((i, $"Choose the {item.LinkType} this item links to."));
            if (item.LinkType == MenuLinkTypes.Custom && ValidateCustomUrl(item.Url) is { } urlError)
                errors.Add((i, urlError));
            if ((item.Label ?? "").Trim().Length > MaxLabelLength)
                errors.Add((i, $"Labels can be at most {MaxLabelLength} characters."));
            if (item.LinkType == MenuLinkTypes.Custom && string.IsNullOrWhiteSpace(item.Label))
                errors.Add((i, "Give a custom link a label."));
            if (item.ParentId is { } parentId)
            {
                var parent = items.FirstOrDefault(p => p.Id == parentId);
                if (parent is null || !ids.Contains(parentId)) errors.Add((i, "This item is nested under an item that is not in the menu."));
                else if (parent.ParentId is not null) errors.Add((i, "Menus nest one level deep: move this item under a top-level item."));
            }
        }
        return errors;
    }
}
