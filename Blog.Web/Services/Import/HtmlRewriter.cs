using System.Text.RegularExpressions;

namespace Blog.Web.Services.Import;

/// <summary>
/// Cleans and rewrites imported post HTML: WordPress paragraph/shortcode normalisation, a
/// conservative built-in XSS sanitizer, image-src extraction, and src remapping to local media.
///
/// NOTE: the sanitizer is a pragmatic regex allowlist covering the common stored-XSS vectors
/// (script/style/iframe/object/embed/form, on* handlers, javascript:/vbscript: URLs). For
/// hardening, swap in a full HTML-parser sanitizer (e.g. Ganss.Xss / HtmlSanitizer) when a NuGet
/// feed is available — the call sites here are the single integration point.
/// </summary>
public partial class HtmlRewriter
{
    // ── WordPress normalisation ─────────────────────────────────────────────
    public string WpAutoParagraph(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html ?? string.Empty;
        // Strip Gutenberg block comments and unwrap [caption] shortcodes (keep inner content).
        html = BlockCommentRegex().Replace(html, "");
        html = CaptionOpenRegex().Replace(html, "");
        html = CaptionCloseRegex().Replace(html, "");

        // If the content already contains block-level HTML, WordPress stored real HTML → leave as-is.
        if (BlockLevelRegex().IsMatch(html)) return html.Trim();

        // Otherwise emulate wpautop: split on blank lines into paragraphs, single newlines → <br />.
        var blocks = Regex.Split(html.Trim(), @"\n\s*\n").Where(b => b.Trim().Length > 0);
        return string.Join("\n", blocks.Select(b => "<p>" + b.Trim().Replace("\n", "<br />\n") + "</p>"));
    }

    // ── Sanitizer ───────────────────────────────────────────────────────────
    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html ?? string.Empty;
        // Remove dangerous element blocks (open…close) and any leftover standalone tags.
        html = DangerousBlockRegex().Replace(html, "");
        html = DangerousTagRegex().Replace(html, "");
        // Strip inline event handlers (onclick=, onerror=, …).
        html = EventHandlerRegex().Replace(html, "");
        // Neutralise javascript:/vbscript:/data: (non-image) protocols in href/src.
        html = DangerousProtocolRegex().Replace(html, "$1=$2#$3");
        return html;
    }

    // ── Image handling ────────────────────────────────────────────────────────
    /// <summary>Distinct absolute http(s) image URLs referenced by &lt;img src&gt; in the HTML.</summary>
    public IReadOnlyList<string> ExtractImageUrls(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return Array.Empty<string>();
        return ImgSrcRegex().Matches(html)
            .Select(m => m.Groups[1].Value)
            .Where(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Replaces every &lt;img src&gt; using <paramref name="resolve"/> (old URL → new local URL, or null to leave).</summary>
    public string RewriteImageUrls(string? html, Func<string, string?> resolve)
    {
        if (string.IsNullOrWhiteSpace(html)) return html ?? string.Empty;
        return ImgSrcRegex().Replace(html, m =>
        {
            var oldUrl = m.Groups[1].Value;
            var newUrl = resolve(oldUrl);
            return string.IsNullOrEmpty(newUrl) ? m.Value : m.Value.Replace(oldUrl, newUrl);
        });
    }

    /// <summary>Strips a WordPress size suffix (image-1024x768.jpg → image.jpg) to match the base attachment.</summary>
    public static string StripSizeSuffix(string url) => SizeSuffixRegex().Replace(url, "$1$2");

    [GeneratedRegex(@"<!--\s*/?wp:.*?-->", RegexOptions.Singleline)] private static partial Regex BlockCommentRegex();
    [GeneratedRegex(@"\[caption[^\]]*\]", RegexOptions.IgnoreCase)] private static partial Regex CaptionOpenRegex();
    [GeneratedRegex(@"\[/caption\]", RegexOptions.IgnoreCase)] private static partial Regex CaptionCloseRegex();
    [GeneratedRegex(@"<\s*(p|div|table|ul|ol|h[1-6]|blockquote|pre|figure|article|section)\b", RegexOptions.IgnoreCase)] private static partial Regex BlockLevelRegex();
    [GeneratedRegex(@"<\s*(script|style|iframe|object|embed|form|svg|math)\b[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex DangerousBlockRegex();
    [GeneratedRegex(@"<\s*/?\s*(script|style|iframe|object|embed|form|link|meta|base|svg|math)\b[^>]*/?>", RegexOptions.IgnoreCase)] private static partial Regex DangerousTagRegex();
    [GeneratedRegex(@"\s+on\w+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase)] private static partial Regex EventHandlerRegex();
    [GeneratedRegex(@"(href|src)\s*=\s*(""|')\s*(?:javascript|vbscript):[^""']*", RegexOptions.IgnoreCase)] private static partial Regex DangerousProtocolRegex();
    [GeneratedRegex(@"<img\b[^>]*?\bsrc\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)] private static partial Regex ImgSrcRegex();
    [GeneratedRegex(@"(.+)-\d+x\d+(\.[a-zA-Z0-9]+)$")] private static partial Regex SizeSuffixRegex();
}
