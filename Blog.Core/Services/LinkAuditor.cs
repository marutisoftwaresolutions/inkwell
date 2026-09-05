using System.Text.RegularExpressions;
using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>What is wrong with an internal link.</summary>
public enum LinkIssueKind
{
    /// <summary>A hop that could be avoided — the link points at a URL that 301/302s somewhere else.</summary>
    Redirect,
    /// <summary>Two or more hops: the redirect target itself redirects.</summary>
    Chain,
    /// <summary>The target was deliberately retired and answers 410.</summary>
    Gone,
    /// <summary>Nothing exists at the target and no rule covers it — a dead end.</summary>
    Broken
}

/// <summary>One problem link, with enough context to fix it without hunting.</summary>
public record LinkIssue(
    string SourceSlug,
    string SourceTitle,
    string Href,
    string Anchor,
    LinkIssueKind Kind,
    string Detail)
{
    /// <summary>Dead ends and retired targets cost the reader; a redirect hop only costs equity.</summary>
    public bool IsSevere => Kind is LinkIssueKind.Broken or LinkIssueKind.Gone;
}

/// <summary>A post or page to scan.</summary>
public record LinkAuditDocument(string Slug, string Title, string Html, bool IsPage = false);

/// <summary>
/// Finds internal links that dead-end, point at retired URLs, or take an unnecessary redirect hop.
///
/// Every link costs something when it is wrong: a broken link wastes a reader and leaks the crawl
/// budget spent following it, and a link through a 301 spends equity on a hop the author could have
/// avoided by linking the destination directly. Neither is visible without scanning, which is why
/// the site accumulated both.
///
/// Pure so the classification rules are testable without a database or a live site.
/// </summary>
public static class LinkAuditor
{
    // Internal href, relative form only — the body links this site actually writes.
    private static readonly Regex _link = new(
        @"<a\s[^>]*href=""(?<href>/[^""#][^""]*)""[^>]*>(?<anchor>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex _tags = new("<[^>]+>", RegexOptions.Compiled);

    /// <summary>
    /// Route prefixes that are generated rather than authored. They resolve from the taxonomy, not
    /// from the post table, so validating them here would produce false positives.
    /// </summary>
    private static readonly string[] _generatedPrefixes =
        ["/tag/", "/category/", "/author/", "/series", "/uploads/", "/css/", "/js/", "/lib/", "/admin/", "/account/", "/error/"];

    /// <summary>Single-segment routes that are not posts.</summary>
    private static readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase)
        { "feed", "search", "sitemap.xml", "robots.txt", "llms.txt", "llms-full.txt", "series" };

    public static IReadOnlyList<LinkIssue> Analyze(
        IEnumerable<LinkAuditDocument> documents,
        ISet<string> publishedSlugs,
        IReadOnlyDictionary<string, RedirectRule> redirects)
    {
        var issues = new List<LinkIssue>();

        foreach (var doc in documents)
        {
            if (string.IsNullOrWhiteSpace(doc.Html)) continue;

            foreach (Match m in _link.Matches(doc.Html))
            {
                var href = m.Groups["href"].Value;
                var anchor = Clean(m.Groups["anchor"].Value);
                var path = Normalize(href);

                if (path is null || ShouldSkip(path)) continue;
                if (path.TrimStart('/').Equals(doc.Slug, StringComparison.OrdinalIgnoreCase)) continue; // self link

                var slug = path.TrimStart('/');
                if (publishedSlugs.Contains(slug)) continue;   // resolves — nothing to report

                if (redirects.TryGetValue(path, out var rule))
                {
                    if (rule.IsGone)
                    {
                        issues.Add(new(doc.Slug, doc.Title, href, anchor, LinkIssueKind.Gone,
                            "The target was retired and answers 410. Point the link at current content."));
                        continue;
                    }

                    var destination = Normalize(rule.To);
                    if (destination is not null && redirects.ContainsKey(destination))
                    {
                        issues.Add(new(doc.Slug, doc.Title, href, anchor, LinkIssueKind.Chain,
                            $"Redirects to {rule.To}, which redirects again. Link the final destination directly."));
                    }
                    else
                    {
                        issues.Add(new(doc.Slug, doc.Title, href, anchor, LinkIssueKind.Redirect,
                            $"Redirects to {rule.To}. Link that directly and save the hop."));
                    }
                    continue;
                }

                issues.Add(new(doc.Slug, doc.Title, href, anchor, LinkIssueKind.Broken,
                    "Nothing exists at this URL and no redirect covers it."));
            }
        }

        return issues
            .OrderByDescending(i => i.IsSevere)
            .ThenBy(i => i.SourceSlug, StringComparer.Ordinal)
            .ThenBy(i => i.Href, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Strips the query and fragment, and drops a trailing slash, so "/a/?x=1#y" compares as "/a".</summary>
    private static string? Normalize(string? href)
    {
        if (string.IsNullOrWhiteSpace(href) || !href.StartsWith('/')) return null;

        var path = href.Split('?')[0].Split('#')[0];
        if (path.Length > 1) path = path.TrimEnd('/');
        return string.IsNullOrEmpty(path) ? null : path;
    }

    private static bool ShouldSkip(string path)
    {
        if (path == "/") return true;
        if (_generatedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return true;

        var segment = path.TrimStart('/');
        if (segment.Contains('/')) return true;              // multi-segment: generated route, not a post
        if (_reserved.Contains(segment)) return true;
        if (Path.HasExtension(segment)) return true;         // a file, not a post

        return false;
    }

    private static string Clean(string html)
    {
        var text = _tags.Replace(html, "").Trim();
        text = Regex.Replace(text, @"\s+", " ");
        return text.Length > 60 ? text[..60] + "…" : text;
    }
}
