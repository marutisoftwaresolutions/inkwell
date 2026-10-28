namespace Blog.Core.Services;

/// <summary>
/// Pure builders for the site's plain-text SEO/AEO documents. Kept free of web/DB types so the
/// exact crawler-permission strings can be unit-tested (this is where the Google-Extended token
/// bug lived — a test now guards it).
/// </summary>
public static class SeoDocuments
{
    /// <summary>
    /// Search engines and AI crawlers allowed to read every public page (ADR-0038). They share ONE group with
    /// <c>*</c>: a crawler obeys only the most specific group that names it, so when each had its own
    /// <c>Allow: /</c> group it ignored every Disallow below, and walked the filter URLs millions of times.
    /// Tokens checked against each operator's published list (Anthropic: ClaudeBot, Claude-User,
    /// Claude-SearchBot, 2026-10-26); <c>anthropic-ai</c> and <c>Claude-Web</c> are legacy and harmless.
    /// </summary>
    public static readonly IReadOnlyList<string> AllowedCrawlers =
    [
        "Googlebot", "Bingbot", "DuckDuckBot", "Applebot",
        "GPTBot", "OAI-SearchBot", "ChatGPT-User",
        "ClaudeBot", "Claude-User", "Claude-SearchBot", "anthropic-ai", "Claude-Web",
        "PerplexityBot", "Perplexity-User",
        "Google-Extended", "Applebot-Extended",
        "Amazonbot", "cohere-ai", "FacebookBot", "Meta-ExternalAgent",
    ];

    /// <summary>Scrapers with no citation value, refused everything.</summary>
    public static readonly IReadOnlyList<string> BlockedCrawlers = ["CCBot", "Bytespider", "omgili"];

    /// <summary>
    /// What no crawler needs: private areas, search results, previews, and listing filter/search parameters.
    /// Each parameter is matched after <c>?</c> or <c>&amp;</c> so a longer name that merely ends the same way
    /// is not caught. Pagination (<c>?page=</c>) stays crawlable, and no post, page or archive URL has a query.
    /// </summary>
    public static readonly IReadOnlyList<string> DisallowedPaths =
    [
        "/admin/", "/account/", "/setup/", "/api/", "/search", "/preview/",
        "/*?category=", "/*&category=",
        "/*?tags=", "/*&tags=",
        "/*?search=", "/*&search=",
        "/*?q=", "/*&q=",
    ];

    /// <summary>Builds the dynamic robots.txt body for the given absolute base URL.</summary>
    public static string RobotsTxt(string baseUrl)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Search engines and AI assistants: every public page, nothing private, no filter or search URLs.");
        sb.AppendLine("# One shared group on purpose: a crawler obeys only the group that names it, so these rules");
        sb.AppendLine("# must sit in the group every allowed crawler matches. Google-Extended is the exact token for");
        sb.AppendLine("# Google's AI training and grounding control.");
        sb.AppendLine("User-agent: *");
        foreach (var bot in AllowedCrawlers) sb.AppendLine($"User-agent: {bot}");
        sb.AppendLine("Allow: /");
        foreach (var path in DisallowedPaths) sb.AppendLine($"Disallow: {path}");
        sb.AppendLine();
        sb.AppendLine("# AI mass-training / scraping crawlers with no citation value — blocked");
        foreach (var bot in BlockedCrawlers)
        {
            sb.AppendLine($"User-agent: {bot}");
            sb.AppendLine("Disallow: /");
            sb.AppendLine();
        }
        sb.AppendLine($"Sitemap: {baseUrl}/sitemap.xml");
        return sb.ToString();
    }
}
