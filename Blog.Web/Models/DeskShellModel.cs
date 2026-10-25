namespace Blog.Web.Models;

/// <summary>
/// Everything the Desk shell (sidebar, topbar, command palette) needs for one request, gathered once
/// by <see cref="Blog.Web.Services.DeskShellService"/> so the layout never queries a
/// repository itself (architecture rule: views never query). Every field has a safe default: the
/// shell renders even when the database is unavailable.
/// </summary>
public sealed class DeskShellModel
{
    public string SiteName { get; init; } = "Inkwell";
    public string ColorScheme { get; init; } = "system";
    public string UserName { get; init; } = "";
    public string AvatarUrl { get; init; } = "/avatar/A";
    public string RoleLabel { get; init; } = "";
    public bool IsCloudMode { get; init; }
    public int PendingComments { get; init; }
    public int ReviewQueue { get; init; }
    public IReadOnlyList<DeskNavigation.Item> Items { get; init; } = Array.Empty<DeskNavigation.Item>();
    public IReadOnlyList<DeskNavigation.Command> Commands { get; init; } = Array.Empty<DeskNavigation.Command>();
    public DeskNavigation.Item? Current { get; init; }

    public string SiteInitial => string.IsNullOrWhiteSpace(SiteName) ? "I" : SiteName.Trim()[..1].ToUpperInvariant();

    /// <summary>Count badge for a navigation item, or 0 when there is nothing to announce.</summary>
    public int BadgeFor(DeskNavigation.Item item) => item.Controller switch
    {
        "Comments" => PendingComments,
        "Posts" => ReviewQueue,
        _ => 0,
    };
}
