using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Blog.Core.Services;

/// <summary>
/// Turns a post's stored HTML into Markdown, for clients that ask for the text without the page
/// around it (AI agents, command-line readers). It covers what Inkwell's editors produce: headings,
/// paragraphs, emphasis, links, images, lists, quotes, code, tables, rules, and the callout, toggle
/// and bookmark blocks. Anything it does not know is reduced to its text, never dropped, and
/// scripts, styles, forms and frames are left out. Links and image addresses are made absolute so
/// the text stands on its own. Pure; the input is the site's own content, capped in length.
/// </summary>
public static partial class HtmlToMarkdown
{
    /// <summary>Longest HTML converted. A post past this is cut, with a note, instead of being walked for ever.</summary>
    public const int MaxHtmlLength = 600_000;

    [GeneratedRegex(@"<!--.*?-->|<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.Singleline | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex Token();

    [GeneratedRegex(@"([a-zA-Z:_-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", RegexOptions.CultureInvariant, 500)]
    private static partial Regex Attr();

    [GeneratedRegex(@"[ \t\r\n\f]+", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex Space();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex BlankLines();

    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
        { "script", "style", "noscript", "svg", "form", "iframe", "template", "object", "embed", "head", "button", "select", "textarea" };

    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
        { "p", "div", "section", "article", "figure", "figcaption", "header", "footer", "main", "aside", "nav", "details", "dl", "dt", "dd", "address" };

    private sealed class Frame(string tag)
    {
        public readonly string Tag = tag;
        public readonly StringBuilder Text = new();
        public string? Href;
        public int ListIndex;                 // 0 for a bulleted list, else the next number
        public List<List<string>>? Rows;      // table rows of cells
        public List<string>? Cells;
    }

    /// <summary>The Markdown for a piece of HTML. <paramref name="baseUrl"/> makes relative links absolute; leave it null to keep them as written.</summary>
    public static string Convert(string? html, Uri? baseUrl = null)
    {
        var source = html ?? "";
        var cut = source.Length > MaxHtmlLength;
        if (cut) source = source[..MaxHtmlLength];

        var stack = new Stack<Frame>();
        stack.Push(new Frame(""));
        var skipDepth = 0;
        string? skipTag = null;
        var preDepth = 0;
        var at = 0;

        void Text(string raw)
        {
            if (skipDepth > 0 || raw.Length == 0) return;
            var text = WebUtility.HtmlDecode(raw);
            if (preDepth == 0)
            {
                text = Space().Replace(text, " ");
                var sb = stack.Peek().Text;
                if (text == " " && (sb.Length == 0 || sb[^1] is '\n' or ' ')) return;
                if (sb.Length == 0 || sb[^1] == '\n') text = text.TrimStart(' ');
                text = text.Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("[", "\\[").Replace("]", "\\]");
            }
            stack.Peek().Text.Append(text);
        }

        string Absolute(string? address)
        {
            var value = (address ?? "").Trim();
            if (value.Length == 0 || baseUrl is null) return value;
            if (value.StartsWith('#') || value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) return value;
            return Uri.TryCreate(baseUrl, value, out var abs) && abs.Scheme is "http" or "https" ? abs.AbsoluteUri : value;
        }

        void Block(string text)
        {
            var body = text.Trim('\n', ' ');
            if (body.Length == 0) return;
            var sb = stack.Peek().Text;
            sb.Append("\n\n").Append(body).Append("\n\n");
        }

        void Close(string tag)
        {
            // Close down to the matching frame; an unmatched closing tag is ignored.
            if (!stack.Any(f => f.Tag == tag)) return;
            while (stack.Count > 1)
            {
                var frame = stack.Pop();
                var inner = frame.Text.ToString();
                var parent = stack.Peek();
                switch (frame.Tag)
                {
                    case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                        Block(new string('#', frame.Tag[1] - '0') + " " + OneLine(inner));
                        break;
                    case "strong" or "b":
                        if (inner.Trim().Length > 0) parent.Text.Append("**").Append(inner.Trim()).Append("**");
                        break;
                    case "em" or "i":
                        if (inner.Trim().Length > 0) parent.Text.Append('*').Append(inner.Trim()).Append('*');
                        break;
                    case "code" when preDepth == 0:
                        if (inner.Length > 0) parent.Text.Append('`').Append(inner.Replace("\\`", "`").Replace("\\*", "*").Replace("\\_", "_").Replace("\\[", "[").Replace("\\]", "]")).Append('`');
                        break;
                    case "pre":
                        preDepth = Math.Max(0, preDepth - 1);
                        Block("```\n" + inner.Trim('\n') + "\n```");
                        break;
                    case "a":
                        var label = OneLine(inner);
                        if (label.Length == 0) break;
                        parent.Text.Append(string.IsNullOrEmpty(frame.Href) ? label : $"[{label}]({frame.Href})");
                        break;
                    case "blockquote":
                        Block(string.Join('\n', Tidy(inner).Split('\n').Select(l => l.Length == 0 ? ">" : "> " + l)));
                        break;
                    case "li":
                        var list = stack.FirstOrDefault(f => f.Tag is "ul" or "ol");
                        var marker = list is { ListIndex: > 0 } ? $"{list.ListIndex++}. " : "- ";
                        var lines = Tidy(inner).Split('\n');
                        // No indent of its own: an item inside another item is indented by that item's continuation lines below, once per level.
                        const string indent = "";
                        parent.Text.Append('\n').Append(indent).Append(marker).Append(lines[0]);
                        foreach (var line in lines.Skip(1)) parent.Text.Append('\n').Append(indent).Append("  ").Append(line);
                        break;
                    case "ul" or "ol":
                        if (stack.Any(f => f.Tag == "li")) parent.Text.Append(inner.TrimEnd());      // a nested list stays inside its item
                        else Block(inner);
                        break;
                    case "td" or "th":
                        stack.FirstOrDefault(f => f.Cells is not null)?.Cells!.Add(OneLine(inner).Replace("|", "\\|"));
                        break;
                    case "tr":
                        var table = stack.FirstOrDefault(f => f.Rows is not null);
                        if (table is not null && frame.Cells is { Count: > 0 }) table.Rows!.Add(frame.Cells);
                        break;
                    case "table":
                        Block(Table(frame.Rows ?? []));
                        break;
                    case "summary":
                        Block("**" + OneLine(inner) + "**");
                        break;
                    case "thead" or "tbody" or "tfoot" or "span" or "u" or "small" or "sup" or "sub" or "mark" or "time" or "label" or "abbr" or "cite" or "q":
                        parent.Text.Append(inner);
                        break;
                    default:
                        if (Blocks.Contains(frame.Tag)) Block(inner); else parent.Text.Append(inner);
                        break;
                }
                if (frame.Tag == tag) break;
            }
        }

        foreach (Match m in Token().Matches(source))
        {
            Text(source[at..m.Index]);
            at = m.Index + m.Length;
            if (m.Value.StartsWith("<!--", StringComparison.Ordinal)) continue;

            var closing = m.Groups[1].Value == "/";
            var tag = m.Groups[2].Value.ToLowerInvariant();

            if (skipDepth > 0)
            {
                if (tag == skipTag) skipDepth += closing ? -1 : m.Groups[3].Value.TrimEnd().EndsWith('/') ? 0 : 1;
                continue;
            }
            if (Skipped.Contains(tag))
            {
                if (!closing && !m.Groups[3].Value.TrimEnd().EndsWith('/')) { skipDepth = 1; skipTag = tag; }
                continue;
            }
            if (closing) { Close(tag); continue; }

            string? Get(string name) => Attr().Matches(m.Groups[3].Value)
                .Select(a => (Name: a.Groups[1].Value, Value: a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value))
                .Where(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(a => WebUtility.HtmlDecode(a.Value)).FirstOrDefault();

            switch (tag)
            {
                case "br":
                    stack.Peek().Text.Append(preDepth > 0 ? "\n" : "  \n");
                    break;
                case "hr":
                    Block("---");
                    break;
                case "img":
                    var src = Absolute(Get("src"));
                    if (src.Length > 0) stack.Peek().Text.Append($"![{OneLine(Get("alt") ?? "").Replace("[", "").Replace("]", "")}]({src})");
                    break;
                case "input" or "meta" or "link" or "source" or "track" or "wbr" or "col":
                    break;
                default:
                    var frame = new Frame(tag);
                    if (tag == "a") frame.Href = Absolute(Get("href"));
                    if (tag == "ol") frame.ListIndex = 1;
                    if (tag == "table") frame.Rows = [];
                    if (tag == "tr") frame.Cells = [];
                    if (tag == "pre") preDepth++;
                    stack.Push(frame);
                    break;
            }
        }
        Text(source[at..]);
        while (stack.Count > 1) Close(stack.Peek().Tag);

        var result = Tidy(stack.Peek().Text.ToString());
        return cut ? result + "\n\n*This text was cut: the post is longer than this version carries.*" : result;
    }

    private static string OneLine(string text) => Space().Replace(text, " ").Trim();

    private static string Tidy(string text)
    {
        var lines = text.Replace("\r", "").Split('\n').Select(l => l.TrimEnd());
        return BlankLines().Replace(string.Join('\n', lines), "\n\n").Trim('\n', ' ');
    }

    private static string Table(List<List<string>> rows)
    {
        if (rows.Count == 0) return "";
        var width = rows.Max(r => r.Count);
        string Row(List<string> cells) => "| " + string.Join(" | ", Enumerable.Range(0, width).Select(i => i < cells.Count ? cells[i] : "")) + " |";
        var sb = new StringBuilder(Row(rows[0])).Append('\n').Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", width))).Append(" |");
        foreach (var row in rows.Skip(1)) sb.Append('\n').Append(Row(row));
        return sb.ToString();
    }
}
