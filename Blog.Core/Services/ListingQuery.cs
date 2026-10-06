namespace Blog.Core.Services;

/// <summary>
/// Keeps listing pages (home feed, search, category, tag, author, series) to a finite set of URLs.
/// Before this, every listing answered any query string with 200 — <c>/?p=123</c>, <c>/?anything</c>,
/// and <c>/?page=99999</c> (an empty page) were each a distinct URL a crawler could keep fetching,
/// and crawlers did: on a live install, verified GPTBot and Applebot traffic concentrated on the home
/// page in exactly this way.
///
/// Two rules, both pure so they can be tested without a request:
/// <list type="bullet">
/// <item><see cref="Canonicalize"/> keeps only the parameters a listing understands plus campaign
/// attribution, drops everything else, and reports whether anything changed (the caller answers 301).
/// It never rewrites a parameter the site's own links produce, so a reader clicking a pager link is
/// never redirected.</item>
/// <item><see cref="IsPastLastPage"/> says when a page number is beyond the last page (the caller
/// answers 404).</item>
/// </list>
/// </summary>
public static class ListingQuery
{
    /// <summary>Parameters listing pages read or their own links emit.</summary>
    private static readonly string[] ListingKeys = ["page", "search", "q", "category", "tags"];

    /// <summary>
    /// Campaign and ad-click parameters. They are not read by the listing, but the page-view beacon
    /// sends <c>location.search</c> for attribution, and analytics tools read the click ids; a 301
    /// that dropped them would silently break both.
    /// </summary>
    private static readonly string[] AttributionKeys =
    [
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "gclid", "gbraid", "wbraid", "fbclid", "msclkid"
    ];

    private static readonly HashSet<string> Allowed =
        new(ListingKeys.Concat(AttributionKeys), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The query pairs to keep, in their original order, and whether the request differs from them:
    /// an unknown or empty parameter, a repeated key, a key in the wrong case, or a <c>page</c> that
    /// is not a whole number of at least 1.
    /// </summary>
    public static (bool Changed, IReadOnlyList<KeyValuePair<string, string>> Kept) Canonicalize(
        IEnumerable<KeyValuePair<string, string?>> pairs)
    {
        var kept = new List<KeyValuePair<string, string>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var (rawKey, rawValue) in pairs)
        {
            var key = rawKey ?? "";
            if (!Allowed.Contains(key) || string.IsNullOrWhiteSpace(rawValue) || !seen.Add(key))
            {
                changed = true;
                continue;
            }

            var lower = key.ToLowerInvariant();
            if (lower == "page" && !(int.TryParse(rawValue, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 1))
            {
                changed = true;
                continue;
            }

            if (!string.Equals(key, lower, StringComparison.Ordinal)) changed = true;
            kept.Add(new KeyValuePair<string, string>(lower, rawValue!));
        }

        return (changed, kept);
    }

    /// <summary>
    /// True when <paramref name="page"/> asks for a page after the last one. Page 1 is never "past the
    /// end": an empty listing's first page is a real page with an empty state, not a missing one.
    /// </summary>
    public static bool IsPastLastPage(int page, int totalItems, int pageSize)
    {
        if (page <= 1) return false;
        if (pageSize <= 0) return true;
        var lastPage = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        return page > lastPage;
    }
}
