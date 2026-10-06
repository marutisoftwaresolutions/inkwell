using System.Net;
using System.Text.RegularExpressions;
using Markdig;

namespace Blog.Core.Services;

/// <summary>Which editor a post is written in (ADR-0022).</summary>
public static class EditorModes
{
    /// <summary>The visual block editor; <c>Posts.Html</c> is the source.</summary>
    public const string Rich = "rich";
    /// <summary>Markdown; <c>Posts.Markdown</c> is the source and <c>Posts.Html</c> is rendered from it on every save.</summary>
    public const string Markdown = "markdown";

    public static string Normalize(string? mode) =>
        string.Equals(mode, Markdown, StringComparison.OrdinalIgnoreCase) ? Markdown : Rich;
}

/// <summary>
/// Renders Markdown to the HTML the rest of the platform reads (ADR-0022). One pipeline for saving and
/// for the editor's preview, so what the author previews is what readers get. CommonMark plus pipe
/// tables, strikethrough, task lists, footnotes and bare-link autolinking. Raw HTML passes through, as it
/// does in the visual editor: authors are trusted staff, and it is what makes switching a post from the
/// visual editor to Markdown lossless (its HTML becomes the Markdown source unchanged). No heading ids are
/// added here; the "On this page" list assigns those to every post the same way.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseTaskLists()
        .UseFootnotes()
        .UseAutoLinks()
        .UseListExtras()
        .Build();

    public static string ToHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? string.Empty : Markdig.Markdown.ToHtml(markdown, Pipeline).Trim();

    /// <summary>The same Markdown, but raw HTML in the source is escaped, not passed through (newsletter intros:
    /// an email has no use for scripts, frames or forms, and the text arrives as the author typed it).</summary>
    private static readonly MarkdownPipeline NoHtmlPipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UseListExtras()
        .DisableHtml()
        .Build();

    public static string ToHtmlWithoutRawHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? string.Empty : Markdig.Markdown.ToHtml(markdown, NoHtmlPipeline).Trim();

    /// <summary>Readable plain text of rendered HTML: blocks on their own lines, inline tags removed without adding spaces.</summary>
    public static string ToPlainLines(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var blocks = Regex.Replace(html, @"</(p|h[1-6]|li|blockquote|pre)>|<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        blocks = Regex.Replace(blocks, @"<li[^>]*>", "- ", RegexOptions.IgnoreCase);
        var text = WebUtility.HtmlDecode(Regex.Replace(blocks, "<[^>]+>", ""));
        var lines = text.Split('\n').Select(l => Regex.Replace(l, @"[ \t]+", " ").Trim());
        return Regex.Replace(string.Join("\n", lines), @"\n{3,}", "\n\n").Trim();
    }

    /// <summary>
    /// True when <paramref name="html"/>, used as Markdown source, renders to the same markup (ignoring
    /// whitespace between tags). Guards the switch from the visual editor so it never changes a post.
    /// </summary>
    public static bool RendersUnchanged(string? html)
    {
        static string Squash(string s) => Regex.Replace(Regex.Replace(s, @">\s+<", "><"), @"\s+", " ").Trim();
        return Squash(ToHtml(html)) == Squash(html ?? string.Empty);
    }

    /// <summary>The searchable text of rendered HTML: tags removed, entities decoded, whitespace collapsed.</summary>
    public static string ToPlaintext(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
