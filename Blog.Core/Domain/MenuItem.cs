namespace Blog.Core.Domain;

/// <summary>
/// One entry in a site's header or footer menu. It points at content by id (a page, category, tag or
/// series), so renaming that content's slug never breaks the menu; or at the home feed; or at a
/// checked custom address. One level of nesting: an item with a <see cref="ParentId"/> is shown under
/// its parent.
/// </summary>
public class MenuItem
{
    public Guid     Id           { get; set; }
    public Guid     OwnerId      { get; set; }
    /// <summary><see cref="MenuLocations"/>.</summary>
    public string   Location     { get; set; } = MenuLocations.Header;
    public Guid?    ParentId     { get; set; }
    public int      SortOrder    { get; set; }
    /// <summary>Shown text; blank means "use the target's own title".</summary>
    public string?  Label        { get; set; }
    /// <summary><see cref="MenuLinkTypes"/>.</summary>
    public string   LinkType     { get; set; } = MenuLinkTypes.Page;
    public Guid?    TargetId     { get; set; }
    public string?  Url          { get; set; }
    public bool     OpenInNewTab { get; set; }
    public DateTime CreatedAt    { get; set; }
    public DateTime UpdatedAt    { get; set; }

    // Filled by the repository's resolving read (joins to the target); not stored.
    public string?  TargetSlug      { get; set; }
    public string?  TargetTitle     { get; set; }
    /// <summary>False when the target no longer exists or (for a page) is not published.</summary>
    public bool     TargetAvailable { get; set; } = true;
}

public static class MenuLocations
{
    public const string Header = "header";
    public const string Footer = "footer";
    public static readonly string[] All = [Header, Footer];
}

/// <summary>What a menu item points at. Stored values: never rename one.</summary>
public static class MenuLinkTypes
{
    public const string Home     = "home";
    public const string Page     = "page";
    public const string Category = "category";
    public const string Tag      = "tag";
    public const string Series   = "series";
    public const string Custom   = "custom";
    public static readonly string[] All = [Home, Page, Category, Tag, Series, Custom];
}

/// <summary>A resolved, render-ready menu link, in display order.</summary>
public sealed record NavLink(string Title, string Href, int Depth, bool OpenInNewTab);
