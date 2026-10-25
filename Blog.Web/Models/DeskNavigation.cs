using System.Security.Claims;

namespace Blog.Web.Models;

/// <summary>
/// The Desk's navigation, declared once. The sidebar, the command palette (Ctrl/Cmd-K) and the
/// breadcrumb all read this list, so a new admin screen is added here and nowhere else.
/// Items are gated by role, by cloud mode and by nothing else; the controllers still enforce
/// their own policies.
/// </summary>
public static class DeskNavigation
{
    public sealed record Item(
        string Group,
        string Label,
        string Href,
        string Controller,
        string Icon,
        string? Action = null,
        bool AdminOnly = false,
        bool HideInCloud = false,
        string? Keywords = null)
    {
        public bool IsActive(string? controller, string? action) =>
            string.Equals(Controller, controller, StringComparison.OrdinalIgnoreCase)
            && (Action is null || string.Equals(Action, action, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A quick action the palette offers beside navigation (create something).</summary>
    public sealed record Command(string Label, string Href, string Icon, string? Keywords = null, bool AdminOnly = false);

    public const string Content = "Content";
    public const string Audience = "Audience";
    public const string Growth = "Growth";
    public const string System = "System";
    public const string Settings = "Settings";

    public static readonly string[] GroupOrder = { Content, Audience, Growth, System, Settings };

    // Lucide-style 24x24 stroke icons, inner markup only. Kept short; the view wraps them in <svg>.
    private const string IcDashboard = "<rect x=\"3\" y=\"3\" width=\"7\" height=\"7\"/><rect x=\"14\" y=\"3\" width=\"7\" height=\"7\"/><rect x=\"14\" y=\"14\" width=\"7\" height=\"7\"/><rect x=\"3\" y=\"14\" width=\"7\" height=\"7\"/>";
    private const string IcPosts = "<path d=\"M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z\"/><polyline points=\"14 2 14 8 20 8\"/><line x1=\"16\" y1=\"13\" x2=\"8\" y2=\"13\"/><line x1=\"16\" y1=\"17\" x2=\"8\" y2=\"17\"/>";
    private const string IcPages = "<path d=\"M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z\"/><polyline points=\"13 2 13 9 20 9\"/>";
    private const string IcMedia = "<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"2\"/><circle cx=\"8.5\" cy=\"8.5\" r=\"1.5\"/><polyline points=\"21 15 16 10 5 21\"/>";
    private const string IcSeries = "<line x1=\"8\" y1=\"6\" x2=\"21\" y2=\"6\"/><line x1=\"8\" y1=\"12\" x2=\"21\" y2=\"12\"/><line x1=\"8\" y1=\"18\" x2=\"21\" y2=\"18\"/><line x1=\"3\" y1=\"6\" x2=\"3.01\" y2=\"6\"/><line x1=\"3\" y1=\"12\" x2=\"3.01\" y2=\"12\"/><line x1=\"3\" y1=\"18\" x2=\"3.01\" y2=\"18\"/>";
    private const string IcCategories = "<path d=\"M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z\"/>";
    private const string IcTags = "<path d=\"M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z\"/><line x1=\"7\" y1=\"7\" x2=\"7.01\" y2=\"7\"/>";
    private const string IcCalendar = "<rect x=\"3\" y=\"4\" width=\"18\" height=\"18\" rx=\"2\"/><line x1=\"16\" y1=\"2\" x2=\"16\" y2=\"6\"/><line x1=\"8\" y1=\"2\" x2=\"8\" y2=\"6\"/><line x1=\"3\" y1=\"10\" x2=\"21\" y2=\"10\"/>";
    private const string IcComments = "<path d=\"M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z\"/>";
    private const string IcSubscribers = "<path d=\"M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2\"/><circle cx=\"9\" cy=\"7\" r=\"4\"/><path d=\"M23 21v-2a4 4 0 0 0-3-3.87\"/><path d=\"M16 3.13a4 4 0 0 1 0 7.75\"/>";
    private const string IcNewsletter = "<rect x=\"2\" y=\"4\" width=\"20\" height=\"16\" rx=\"2\"/><path d=\"m22 7-10 6L2 7\"/>";
    private const string IcForms = "<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"2\"/><path d=\"M7 8h10\"/><path d=\"M7 12h10\"/><path d=\"M7 16h6\"/>";
    private const string IcAnalytics = "<line x1=\"18\" y1=\"20\" x2=\"18\" y2=\"10\"/><line x1=\"12\" y1=\"20\" x2=\"12\" y2=\"4\"/><line x1=\"6\" y1=\"20\" x2=\"6\" y2=\"14\"/>";
    private const string IcHealth = "<path d=\"M22 12h-4l-3 9L9 3l-3 9H2\"/>";
    private const string IcLink = "<path d=\"M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71\"/><path d=\"M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71\"/>";
    private const string IcCrawlers = "<rect x=\"4\" y=\"8\" width=\"16\" height=\"12\" rx=\"2\"/><path d=\"M12 8V4\"/><circle cx=\"12\" cy=\"3\" r=\"1\"/><line x1=\"9\" y1=\"13\" x2=\"9\" y2=\"15\"/><line x1=\"15\" y1=\"13\" x2=\"15\" y2=\"15\"/><path d=\"M2 14h2\"/><path d=\"M20 14h2\"/>";
    private const string IcRedirects = "<polyline points=\"15 14 20 9 15 4\"/><path d=\"M4 20v-7a4 4 0 0 1 4-4h12\"/>";
    private const string IcSecurity = "<path d=\"M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z\"/>";
    private const string IcErrors = "<path d=\"M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z\"/><line x1=\"12\" y1=\"9\" x2=\"12\" y2=\"13\"/><line x1=\"12\" y1=\"17\" x2=\"12.01\" y2=\"17\"/>";
    private const string IcAudit = "<path d=\"M9 11l3 3L22 4\"/><path d=\"M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11\"/>";
    private const string IcBackups = "<path d=\"M21 8v13H3V8\"/><path d=\"M1 3h22v5H1z\"/><path d=\"M10 12h4\"/>";
    private const string IcExport = "<path d=\"M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4\"/><polyline points=\"17 8 12 3 7 8\"/><line x1=\"12\" y1=\"3\" x2=\"12\" y2=\"15\"/>";
    private const string IcImport = "<path d=\"M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4\"/><polyline points=\"7 10 12 15 17 10\"/><line x1=\"12\" y1=\"15\" x2=\"12\" y2=\"3\"/>";
    private const string IcWebhooks = "<path d=\"M18 16.98h-5.99c-1.1 0-1.95.94-2.48 1.9A4 4 0 0 1 2 17c.01-.7.2-1.4.57-2\"/><path d=\"m6 17 3.13-5.78c.53-.97.1-2.18-.5-3.1a4 4 0 1 1 6.89-4.06\"/><path d=\"m12 6 3.13 5.73C15.66 12.7 16.9 13 18 13a4 4 0 0 1 0 8\"/>";
    private const string IcSettings = "<circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z\"/>";
    private const string IcUsers = "<path d=\"M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2\"/><circle cx=\"9\" cy=\"7\" r=\"4\"/><path d=\"M23 21v-2a4 4 0 0 0-3-3.87\"/><path d=\"M16 3.13a4 4 0 0 1 0 7.75\"/>";
    private const string IcTheme = "<circle cx=\"13.5\" cy=\"6.5\" r=\"2.5\"/><circle cx=\"17.5\" cy=\"10.5\" r=\"2.5\"/><circle cx=\"8.5\" cy=\"7.5\" r=\"2.5\"/><circle cx=\"6.5\" cy=\"12.5\" r=\"2.5\"/><path d=\"M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10c.93 0 1.5-.63 1.5-1.5 0-.39-.15-.74-.39-1.01-.23-.26-.38-.61-.38-1 0-.83.67-1.5 1.5-1.5H16c3.31 0 6-2.69 6-6 0-5.17-4.49-9-10-9z\"/>";
    private const string IcMenus = "<line x1=\"4\" y1=\"6\" x2=\"20\" y2=\"6\"/><line x1=\"4\" y1=\"12\" x2=\"20\" y2=\"12\"/><line x1=\"4\" y1=\"18\" x2=\"14\" y2=\"18\"/>";
    private const string IcProfile = "<path d=\"M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2\"/><circle cx=\"12\" cy=\"7\" r=\"4\"/>";
    public const string IcPlus = "<line x1=\"12\" y1=\"5\" x2=\"12\" y2=\"19\"/><line x1=\"5\" y1=\"12\" x2=\"19\" y2=\"12\"/>";
    public const string IcExternal = "<path d=\"M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6\"/><polyline points=\"15 3 21 3 21 9\"/><line x1=\"10\" y1=\"14\" x2=\"21\" y2=\"3\"/>";
    public const string IcUpload = "<path d=\"M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4\"/><polyline points=\"17 8 12 3 7 8\"/><line x1=\"12\" y1=\"3\" x2=\"12\" y2=\"15\"/>";
    public const string IcSearch = "<circle cx=\"11\" cy=\"11\" r=\"8\"/><line x1=\"21\" y1=\"21\" x2=\"16.65\" y2=\"16.65\"/>";

    public static readonly Item Dashboard = new(Content, "Dashboard", "/admin/dashboard", "Dashboard", IcDashboard, Keywords: "home overview");

    public static readonly IReadOnlyList<Item> Items = new[]
    {
        Dashboard,
        new Item(Content, "Posts", "/admin/posts", "Posts", IcPosts, Keywords: "articles write"),
        new Item(Content, "Pages", "/admin/pages", "Pages", IcPages, Keywords: "static about contact"),
        new Item(Content, "Media", "/admin/media", "Media", IcMedia, Keywords: "images uploads files"),
        new Item(Content, "Series", "/admin/series", "Series", IcSeries, Keywords: "collections"),
        new Item(Content, "Categories", "/admin/categories", "Categories", IcCategories),
        new Item(Content, "Tags", "/admin/tags", "Tags", IcTags),
        new Item(Content, "Calendar", "/admin/calendar", "Calendar", IcCalendar, Keywords: "schedule planning"),
        new Item(Content, "Import", "/admin/import", "Import", IcImport, Keywords: "wordpress ghost migrate"),

        new Item(Audience, "Comments", "/admin/comments", "Comments", IcComments, Keywords: "moderation spam"),
        new Item(Audience, "Subscribers", "/admin/subscribers", "Subscribers", IcSubscribers, AdminOnly: true, Keywords: "members email list"),
        new Item(Audience, "Newsletter", "/admin/newsletter", "NewsletterAdmin", IcNewsletter, AdminOnly: true, HideInCloud: true, Keywords: "email campaign send"),
        new Item(Audience, "Forms", "/admin/forms", "Forms", IcForms, AdminOnly: true, Keywords: "contact submissions"),

        new Item(Growth, "Analytics", "/admin/analytics", "Analytics", IcAnalytics, Keywords: "traffic views stats"),
        new Item(Growth, "Content Health", "/admin/content-health", "ContentHealth", IcHealth, Keywords: "seo freshness review"),
        new Item(Growth, "Link Audit", "/admin/link-audit", "LinkAudit", IcLink, Keywords: "broken links internal"),
        new Item(Growth, "AI Crawlers", "/admin/ai-crawlers", "AiCrawlers", IcCrawlers, Keywords: "gptbot claudebot bots llms"),
        new Item(Growth, "Redirects", "/admin/redirects", "Redirects", IcRedirects, AdminOnly: true, Keywords: "404 301 gone"),

        new Item(System, "Security", "/admin/security", "Security", IcSecurity, AdminOnly: true, Keywords: "firewall blocked ip"),
        new Item(System, "Error Monitor", "/admin/errors", "Errors", IcErrors, AdminOnly: true, Keywords: "exceptions logs"),
        new Item(System, "Audit Trail", "/admin/audit", "Audit", IcAudit, AdminOnly: true, Keywords: "history who changed"),
        new Item(System, "Backups", "/admin/backups", "Backups", IcBackups, AdminOnly: true, HideInCloud: true, Keywords: "restore snapshot"),
        new Item(System, "Export", "/admin/export", "Export", IcExport, AdminOnly: true, HideInCloud: true, Keywords: "wordpress ghost wxr download"),
        new Item(System, "Webhooks", "/admin/webhooks", "Webhooks", IcWebhooks, AdminOnly: true, Keywords: "integrations events"),

        new Item(Settings, "General", "/admin/settings", "Settings", IcSettings, Keywords: "site name seo analytics comments identity"),
        new Item(Settings, "Theme", "/admin/theme", "ThemeSettings", IcTheme, Keywords: "layout preset colours fonts"),
        new Item(Settings, "Menus", "/admin/menus", "Menus", IcMenus, Keywords: "navigation footer links"),
        new Item(Settings, "Users", "/admin/users", "Users", IcUsers, AdminOnly: true, Keywords: "roles permissions invite"),
        new Item(Settings, "Profile", "/account/profile", "Account", IcProfile, Action: "Profile", Keywords: "password avatar two-factor"),
    };

    public static readonly IReadOnlyList<Command> Commands = new[]
    {
        new Command("New post", "/admin/posts/create", IcPlus, "write create article"),
        new Command("New page", "/admin/pages/create", IcPlus, "create static"),
        new Command("Upload media", "/admin/media", IcUpload, "image file"),
        new Command("View site", "/", IcExternal, "public homepage open"),
    };

    public static bool IsAdmin(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Role)?.Value == "Admin" || user.IsInRole("Admin");

    public static IEnumerable<Item> Visible(ClaimsPrincipal user, bool cloudMode)
    {
        var admin = IsAdmin(user);
        return Items.Where(i => (!i.AdminOnly || admin) && (!i.HideInCloud || !cloudMode));
    }

    public static Item? Current(string? controller, string? action) =>
        Items.FirstOrDefault(i => i.Action is not null && i.IsActive(controller, action))
        ?? Items.FirstOrDefault(i => i.Action is null && i.IsActive(controller, action));
}
