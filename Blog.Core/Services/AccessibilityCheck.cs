using System.Net;
using System.Text.RegularExpressions;

namespace Blog.Core.Services;

/// <summary>
/// Advisory accessibility checks on an article body (v1.0.9; MARKET-RESEARCH G13): things an author can fix in the
/// editor and a screen-reader or keyboard user would notice. Like the answer-engine readiness score it never blocks a
/// publish. It reads only the stored HTML, so it says what is there, not whether the page as a whole passes WCAG.
/// Pure, so the editor sidebar and Content Health agree.
/// </summary>
public static class AccessibilityCheck
{
    /// <summary>A paragraph longer than this many words is hard to follow, especially with a screen reader or on a phone.</summary>
    public const int LongParagraphWords = 150;

    public static class Rules
    {
        public const string MissingAlt = "missing-alt";
        public const string FileNameAlt = "file-name-alt";
        public const string HeadingOrder = "heading-order";
        public const string VagueLink = "vague-link";
        public const string TableHeader = "table-header";
        public const string LongParagraph = "long-paragraph";
    }

    public sealed record Finding(string Rule, string Title, string Advice, int Count);

    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;
    private static readonly Regex Img = new(@"<img\b[^>]*>", Opts);
    private static readonly Regex Heading = new(@"<h([1-6])\b[^>]*>", Opts);
    private static readonly Regex Anchor = new(@"<a\b([^>]*)>(.*?)</a\s*>", Opts);
    private static readonly Regex Table = new(@"<table\b[^>]*>(.*?)</table\s*>", Opts);
    private static readonly Regex Paragraph = new(@"<p\b[^>]*>(.*?)</p\s*>", Opts);
    private static readonly Regex Tags = new(@"<[^>]+>", Opts);
    private static readonly Regex FileName = new(@"^[\w\-. ]+\.(jpe?g|png|gif|webp|svg|avif|bmp)$|^(img|image|dsc|pxl|screenshot|photo)[_\- ]?\d+", Opts);

    /// <summary>Link text that says nothing on its own: a screen-reader user listing the links hears only this.</summary>
    private static readonly HashSet<string> Vague = new(StringComparer.OrdinalIgnoreCase)
    {
        "click here", "here", "click", "read more", "more", "link", "this", "this link", "learn more", "go", "continue", "details",
    };

    public static IReadOnlyList<Finding> Check(string? html)
    {
        var findings = new List<Finding>();
        if (string.IsNullOrWhiteSpace(html)) return findings;

        // Images: no alt at all is read out as the file name or skipped silently. alt="" is the correct way to mark a
        // decorative image, so it is not flagged.
        var images = Img.Matches(html).Select(m => m.Value).ToList();
        var noAlt = images.Count(i => Attr(i, "alt") is null && !IsHidden(i));
        if (noAlt > 0)
            findings.Add(new(Rules.MissingAlt, Plural(noAlt, "image has", "images have") + " no alt text",
                "Describe what each image shows, in a sentence. For a purely decorative image, set an empty alt text so screen readers skip it.", noAlt));
        var fileAlt = images.Count(i => Attr(i, "alt") is { Length: > 0 } a && FileName.IsMatch(a.Trim()));
        if (fileAlt > 0)
            findings.Add(new(Rules.FileNameAlt, Plural(fileAlt, "image's alt text is", "images' alt texts are") + " a file name",
                "A file name tells a screen-reader user nothing. Replace it with what the image shows.", fileAlt));

        // Headings: the layout renders the title as the page's only h1, then levels go down one step at a time.
        var levels = Heading.Matches(html).Select(m => m.Groups[1].Value[0] - '0').ToList();
        var skips = levels.Count(l => l == 1);
        var previous = 1;
        foreach (var level in levels)
        {
            if (level > previous + 1) skips++;
            previous = level;
        }
        if (skips > 0)
            findings.Add(new(Rules.HeadingOrder, Plural(skips, "heading skips", "headings skip") + " a level or repeats the title's level",
                "Screen-reader users move through a page by its headings. Start sections at Heading 2, put Heading 3 only under a Heading 2, and do not use Heading 1 in the body (the title is the page's Heading 1).", skips));

        // Links whose text makes no sense out of context, or that have no text at all.
        var vague = 0;
        foreach (Match a in Anchor.Matches(html))
        {
            if (Attr("<a" + a.Groups[1].Value + ">", "aria-label") is { Length: > 0 }) continue;
            var text = Text(a.Groups[2].Value);
            var imgAlt = Img.Matches(a.Groups[2].Value).Select(m => Attr(m.Value, "alt")).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (text.Length == 0 && imgAlt is null) vague++;
            else if (text.Length > 0 && Vague.Contains(text.Trim(' ', '.', '…', '→', '»', ':'))) vague++;
        }
        if (vague > 0)
            findings.Add(new(Rules.VagueLink, Plural(vague, "link says", "links say") + " nothing on its own",
                "Use link text that names the destination, such as \"our RevolutionEHR review\" instead of \"click here\". Screen readers often list links without the sentence around them.", vague));

        // Tables: without a header row, a screen reader cannot say which column a cell belongs to.
        var noHeader = Table.Matches(html).Count(t => !Regex.IsMatch(t.Groups[1].Value, @"<th\b", Opts));
        if (noHeader > 0)
            findings.Add(new(Rules.TableHeader, Plural(noHeader, "table has", "tables have") + " no header row",
                "Mark the first row as a header row so each cell is read with its column name.", noHeader));

        var longParas = Paragraph.Matches(html).Count(p => WordCount(Text(p.Groups[1].Value)) > LongParagraphWords);
        if (longParas > 0)
            findings.Add(new(Rules.LongParagraph, Plural(longParas, "paragraph is", "paragraphs are") + $" over {LongParagraphWords} words",
                "Break long paragraphs into shorter ones, one idea each. They are hard to follow on a phone or with a screen reader.", longParas));

        return findings;
    }

    /// <summary>The total number of individual problems, for a list column.</summary>
    public static int IssueCount(IReadOnlyList<Finding> findings) => findings.Sum(f => f.Count);

    private static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $@"\s{name}\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>]+))", Opts);
        if (!m.Success)
            return Regex.IsMatch(tag, $@"\s{name}(\s|/?>)", Opts) ? "" : null;   // a bare attribute (alt) counts as empty
        return WebUtility.HtmlDecode(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value);
    }

    private static bool IsHidden(string tag) =>
        Attr(tag, "aria-hidden") is "true" || Attr(tag, "role") is "presentation" or "none";

    private static string Text(string html) => WebUtility.HtmlDecode(Tags.Replace(html, " ")).Replace(' ', ' ').Trim();

    private static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
}
