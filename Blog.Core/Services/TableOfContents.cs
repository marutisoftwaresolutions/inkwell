using System.Text;
using System.Text.RegularExpressions;

namespace Blog.Core.Services;

public sealed record TocEntry(int Level, string Id, string Text);

/// <summary>
/// Builds an "On this page" list from a post body and gives each heading a stable id to jump to.
/// Pure: HTML in, HTML + entries out. Only h2 and h3 count — h1 is the title, h4 and below are too
/// fine for a table of contents. Ids come from the heading text (slugified) and are made unique
/// within the document; a heading that already has an id keeps it, so hand-authored anchors and
/// existing inbound links keep working.
/// </summary>
public static class TableOfContents
{
    /// <summary>Fewer headings than this and a table of contents is noise rather than navigation.</summary>
    public const int MinHeadings = 3;

    private static readonly Regex Heading = new(@"<h([23])\b([^>]*)>(.*?)</h\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex IdAttr = new(@"\sid\s*=\s*(""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex NonSlug = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    public static (string Html, IReadOnlyList<TocEntry> Entries) Build(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return (html ?? string.Empty, Array.Empty<TocEntry>());

        var entries = new List<TocEntry>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        var rewritten = Heading.Replace(html, m =>
        {
            var level = m.Groups[1].Value[0] - '0';
            var attrs = m.Groups[2].Value;
            var inner = m.Groups[3].Value;
            var text = System.Net.WebUtility.HtmlDecode(Tags.Replace(inner, "")).Trim();
            text = Regex.Replace(text, @"\s+", " ");
            if (text.Length == 0) return m.Value;

            var existing = IdAttr.Match(attrs);
            string id;
            if (existing.Success)
            {
                id = existing.Groups[2].Success ? existing.Groups[2].Value : existing.Groups[3].Value;
                used.Add(id);
                entries.Add(new TocEntry(level, id, text));
                return m.Value;
            }

            id = Unique(Slugify(text), used);
            entries.Add(new TocEntry(level, id, text));
            return $"<h{level} id=\"{id}\"{attrs}>{inner}</h{level}>";
        });

        return (rewritten, entries);
    }

    /// <summary>Show the list only when there is enough structure to be worth a glance.</summary>
    public static bool Worthwhile(IReadOnlyList<TocEntry> entries) => entries.Count(e => e.Level == 2) >= MinHeadings || entries.Count >= MinHeadings + 1;

    public static string Slugify(string text)
    {
        var s = NonSlug.Replace(text.ToLowerInvariant(), "-").Trim('-');
        if (s.Length > 60) s = s[..60].TrimEnd('-');
        return s.Length == 0 ? "section" : s;
    }

    private static string Unique(string slug, HashSet<string> used)
    {
        var candidate = slug;
        for (var i = 2; !used.Add(candidate); i++) candidate = $"{slug}-{i}";
        return candidate;
    }
}
