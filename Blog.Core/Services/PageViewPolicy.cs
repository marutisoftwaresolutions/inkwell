namespace Blog.Core.Services;

/// <summary>
/// Which public paths count as a page view: the home page and single-segment post/page slugs. Used
/// both when the page is rendered (to decide whether to emit the view beacon) and when the beacon
/// arrives, so the two can never disagree. Mirrors <c>PageViewRepository.ContentPathFilter</c>.
/// </summary>
public static class PageViewPolicy
{
    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".js", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico",
        ".woff", ".woff2", ".ttf", ".eot", ".webp", ".avif", ".pdf", ".zip"
    };

    // Single-segment paths that are not post pages — utility, auth, and admin roots
    private static readonly HashSet<string> SkippedExactPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/feed", "/sitemap.xml", "/robots.txt", "/pv",
        "/admin", "/account", "/newsletter", "/avatar", "/og-image", "/landing", "/error"
    };

    public const int MaxPathLength = 450;

    public static bool IsTrackablePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/' || path.Length > MaxPathLength) return false;
        if (path == "/") return true;

        // Any path with a second slash is a category, tag, admin, or other internal page.
        if (path.IndexOf('/', 1) >= 0) return false;

        var dot = path.LastIndexOf('.');
        if (dot > 0 && SkippedExtensions.Contains(path[dot..])) return false;

        return !SkippedExactPaths.Contains(path);
    }
}
