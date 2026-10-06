namespace Blog.Web.Models;

public class NavbarViewModel
{
    public string SiteName { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public string SiteLogoUrl { get; set; } = string.Empty;
    public List<Blog.Core.Domain.Page> Pages { get; set; } = new List<Blog.Core.Domain.Page>();
    public List<Blog.Core.Domain.Category> Categories { get; set; } = new List<Blog.Core.Domain.Category>();

    /// <summary>
    /// The links the navbar renders, in order: the header menu from Admin → Menus when one exists, otherwise
    /// the nav pages as before menus existed (<see cref="Blog.Core.Services.NavigationMenu.FromPages"/>; the one
    /// intended difference is the Feed navbar's page links, which pointed at a route that never existed).
    /// Depth 1 marks a child item; top-level bars show depth 0 only.
    /// </summary>
    public IReadOnlyList<Blog.Core.Domain.NavLink> Links { get; set; } = [];
}
