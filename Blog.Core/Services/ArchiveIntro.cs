using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Blog.Core.Services;

/// <summary>
/// The optional description of a category or tag: plain text the author types, shown as an introduction above the
/// archive's list and used for the page's meta description.
///
/// It is deliberately not HTML. Everything is encoded first, then exactly one construct is recognised: a link written
/// as <c>[text](/path)</c> or <c>[text](https://host/path)</c>. That is enough for an archive page to point at the
/// articles that matter most on the topic, and there is nothing to sanitise because no markup is ever taken from the
/// author. A blank line starts a new paragraph.
/// </summary>
public static partial class ArchiveIntro
{
    /// <summary>Longest description stored. Long enough for two short paragraphs, short enough to stay an introduction.</summary>
    public const int MaxLength = 600;

    /// <summary>Meta descriptions longer than this are cut at a word boundary.</summary>
    public const int MetaLength = 155;

    // Applied to already-encoded text, so the link text can never contain a tag. The target is a site-relative path
    // or an https URL, with no spaces, quotes or angle brackets (their encoded forms contain '&', which is excluded
    // from a path here only when it is the start of an entity we produced: '&quot;', '&lt;', '&gt;', '&#39;').
    [GeneratedRegex(@"\[([^\[\]\r\n]{1,120})\]\(((?:/(?!/)|https://)[^\s()]{0,300})\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\r\n?|\n")]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"\n[ \t]*\n+")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Trims, normalises line breaks and caps the length. Empty input becomes null (no description).</summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var clean = LineBreak().Replace(text, "\n").Trim();
        if (clean.Length > MaxLength) clean = clean[..MaxLength].TrimEnd();
        return clean.Length == 0 ? null : clean;
    }

    /// <summary>Why a description cannot be saved, in words for the author, or null when it can.</summary>
    public static string? Validate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var length = LineBreak().Replace(text, "\n").Trim().Length;
        return length > MaxLength
            ? $"The description is {length} characters. Keep it to {MaxLength} or fewer."
            : null;
    }

    /// <summary>Safe HTML for the archive page: one &lt;p&gt; per paragraph, links as anchors, everything else encoded.</summary>
    public static string ToHtml(string? text)
    {
        var clean = Normalize(text);
        if (clean is null) return string.Empty;

        var html = new StringBuilder();
        foreach (var paragraph in ParagraphBreak().Split(clean))
        {
            var line = Whitespace().Replace(paragraph, " ").Trim();
            if (line.Length == 0) continue;
            var encoded = WebUtility.HtmlEncode(line);
            var linked = LinkPattern().Replace(encoded, match =>
            {
                var target = match.Groups[2].Value;
                // An encoded quote or angle bracket inside the target means the author typed one; leave it as text.
                if (target.Contains("&quot;") || target.Contains("&lt;") || target.Contains("&gt;") || target.Contains("&#39;"))
                    return match.Value;
                var external = target.StartsWith("https://", StringComparison.Ordinal);
                return external
                    ? $"<a href=\"{target}\" rel=\"nofollow noopener\">{match.Groups[1].Value}</a>"
                    : $"<a href=\"{target}\">{match.Groups[1].Value}</a>";
            });
            html.Append("<p>").Append(linked).Append("</p>");
        }
        return html.ToString();
    }

    /// <summary>The description as one line of plain text, with links reduced to their text.</summary>
    public static string ToPlainText(string? text)
    {
        var clean = Normalize(text);
        if (clean is null) return string.Empty;
        var withoutLinks = LinkPattern().Replace(clean, m => m.Groups[1].Value);
        return Whitespace().Replace(withoutLinks, " ").Trim();
    }

    /// <summary>The meta description for the archive page, or null when there is no description to build it from.</summary>
    public static string? ToMetaDescription(string? text)
    {
        var plain = ToPlainText(text);
        return plain.Length == 0 ? null : TextHelper.SmartTruncate(plain, MetaLength);
    }
}
