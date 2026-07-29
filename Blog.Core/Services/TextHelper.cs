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
    /// Applies a single " | {siteName}" brand suffix to a page title: empty title → site name;
    /// a title that already contains the site name is left untouched (no double brand); otherwise
    /// the suffix is appended. Keeps every page's &lt;title&gt; consistent.
    /// </summary>
    public static string BrandTitle(string? rawTitle, string? siteName)
    {
        rawTitle = rawTitle?.Trim();
        siteName = siteName?.Trim();
        if (string.IsNullOrWhiteSpace(rawTitle)) return siteName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(siteName)) return rawTitle;
        return rawTitle.Contains(siteName, StringComparison.OrdinalIgnoreCase)
            ? rawTitle
            : $"{rawTitle} | {siteName}";
    }
}
