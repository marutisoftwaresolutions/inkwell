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
    /// Where a category or tag chip on a listing links (ADR-0038). Each chip goes to its own archive
    /// (<c>/category/{slug}</c>, <c>/tag/{slug}</c>), or to the home feed when it is the active filter, so it
    /// clears. Chips used to toggle the clicked value into the current set, which minted a new
    /// <c>/?category=…&amp;tags=…</c> URL per chip per page: an unbounded tree crawlers walked millions of times.
    /// </summary>
    public static string FacetUrl(string type, string slug,
        IReadOnlyCollection<string> selectedCategories, IReadOnlyCollection<string> selectedTags)
    {
        var isCategory = string.Equals(type, "category", StringComparison.OrdinalIgnoreCase);
        var active = isCategory
            ? selectedCategories.Contains(slug, StringComparer.OrdinalIgnoreCase)
            : selectedTags.Contains(slug, StringComparer.OrdinalIgnoreCase);
        if (active || !IsSlug(slug)) return "/";
        return isCategory ? $"/category/{slug.ToLowerInvariant()}" : $"/tag/{slug.ToLowerInvariant()}";
    }

    /// <summary>
    /// For a home-feed URL that combines more than one filter value (<c>?tags=a,b</c>,
    /// <c>?category=x&amp;tags=y</c>), the single archive it collapses to: the last tag (the one a chip added
    /// most recently), else the last category; <c>/</c> when a value is not a valid slug. Null when the
    /// URL holds at most one value, which the single-value redirect already handles (ADR-0038).
    /// </summary>
    public static string? CollapseFacets(string? category, string? tags)
    {
        static string[] Split(string? raw) =>
            string.IsNullOrWhiteSpace(raw) ? [] : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var cats = Split(category);
        var tagList = Split(tags);
        if (cats.Length + tagList.Length <= 1) return null;

        var (route, slug) = tagList.Length > 0 ? ("tag", tagList[^1]) : ("category", cats[^1]);
        return IsSlug(slug) ? $"/{route}/{slug.ToLowerInvariant()}" : "/";
    }

    private static bool IsSlug(string value) =>
        value.Length is > 0 and <= 200 && value.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-');

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
