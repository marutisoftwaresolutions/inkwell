using System.Text.RegularExpressions;

namespace Blog.Core.Services;

public static class TextHelper
{
    /// <summary>
    /// Produces a clean meta-description-length excerpt: collapses whitespace, cuts on a word
    /// boundary at or before <paramref name="max"/> characters, and appends an ellipsis. Used for
    /// auto-generated descriptions so they never break mid-word or exceed the SERP limit (~158).
    /// </summary>
    public static string SmartTruncate(string? text, int max = 158)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = Regex.Replace(text.Trim(), @"\s+", " ");
        if (text.Length <= max) return text;

        var slice = text.Substring(0, max - 1); // leave room for the ellipsis
        var lastSpace = slice.LastIndexOf(' ');
        if (lastSpace > max * 0.6) slice = slice.Substring(0, lastSpace);
        return slice.TrimEnd(',', '.', ';', ':', '-', ' ') + "…";
    }

    /// <summary>
    /// Longest &lt;title&gt; worth rendering. Google lays out roughly 580px of title text before
    /// cutting to an ellipsis, which is about 60 characters at typical width. Titles are measured
    /// in pixels, not characters, so this is a good-enough budget rather than an exact limit.
    /// </summary>
    public const int MaxBrandedTitleLength = 60;

    /// <summary>
    /// Applies a single " | {siteName}" brand suffix to a page title: empty title → site name;
    /// a title that already contains the site name is left untouched (no double brand); otherwise
    /// the suffix is appended — but only when the result still fits inside
    /// <see cref="MaxBrandedTitleLength"/>.
    ///
    /// The length guard matters because the suffix is pure overhead in a SERP: when a branded title
    /// overflows, the part Google cuts is the tail, and the tail is where the differentiator lives
    /// ("2026", "Compared", "FDA-Cleared"). Dropping the brand keeps the words that earn the click,
    /// and search engines display the site name separately anyway. It also keeps the platform
    /// multi-tenant-safe: a tenant with a long site name would otherwise lose the end of every
    /// title on the site, and the longer the name the more it costs.
    /// </summary>
    public static string BrandTitle(string? rawTitle, string? siteName)
    {
        rawTitle = rawTitle?.Trim();
        siteName = siteName?.Trim();
        if (string.IsNullOrWhiteSpace(rawTitle)) return siteName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(siteName)) return rawTitle;
        if (rawTitle.Contains(siteName, StringComparison.OrdinalIgnoreCase)) return rawTitle;

        var branded = $"{rawTitle} | {siteName}";
        return branded.Length <= MaxBrandedTitleLength ? branded : rawTitle;
    }
}
