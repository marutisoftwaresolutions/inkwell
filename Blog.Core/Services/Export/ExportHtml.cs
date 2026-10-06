using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blog.Core.Domain;
using static System.Net.WebUtility;

namespace Blog.Core.Services.Export;

/// <summary>
/// Builds the body an export writes for a post: Inkwell's structured blocks (answer capsule, key facts,
/// roundup, how-to, FAQ) rendered as plain, self-contained HTML around the article body, so a reader on
/// WordPress or Ghost never meets an empty section where a block used to be (Data Portability rule).
/// The markup uses only elements every theme styles: headings, paragraphs, lists, a table.
/// </summary>
public static class ExportHtml
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static string PostBody(ExportPost p)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(p.AnswerCapsule))
            sb.Append("<p><strong>").Append(HtmlEncode(p.AnswerCapsule.Trim())).Append("</strong></p>\n");

        var facts = Parse<List<KeyFact>>(p.KeyFactsJson)?.Where(f => !string.IsNullOrWhiteSpace(f.Label)).ToList();
        if (facts is { Count: > 0 })
        {
            sb.Append("<h2>Key facts</h2>\n<ul>\n");
            foreach (var f in facts)
                sb.Append("<li><strong>").Append(HtmlEncode(f.Label.Trim())).Append(":</strong> ")
                  .Append(HtmlEncode((f.Value ?? "").Trim())).Append("</li>\n");
            sb.Append("</ul>\n");
        }

        var roundup = Parse<RoundupData>(p.RoundupJson);
        if (roundup is { HasEntries: true }) AppendRoundup(sb, roundup);

        sb.Append(p.Html ?? "").Append('\n');

        var steps = Parse<List<HowToStep>>(p.HowToJson)?.Where(s => !string.IsNullOrWhiteSpace(s.Text) || !string.IsNullOrWhiteSpace(s.Name)).ToList();
        if (steps is { Count: > 0 })
        {
            sb.Append("<h2>Step by step</h2>\n<ol>\n");
            foreach (var s in steps)
            {
                sb.Append("<li>");
                if (!string.IsNullOrWhiteSpace(s.Name)) sb.Append("<strong>").Append(HtmlEncode(s.Name.Trim())).Append("</strong> ");
                sb.Append(HtmlEncode((s.Text ?? "").Trim())).Append("</li>\n");
            }
            sb.Append("</ol>\n");
        }

        var faqs = Parse<List<FaqItem>>(p.FaqJson)?.Where(f => !string.IsNullOrWhiteSpace(f.Question)).ToList();
        if (faqs is { Count: > 0 })
        {
            sb.Append("<h2>Frequently asked questions</h2>\n");
            foreach (var f in faqs)
                sb.Append("<h3>").Append(HtmlEncode(f.Question.Trim())).Append("</h3>\n<p>")
                  .Append(HtmlEncode((f.Answer ?? "").Trim())).Append("</p>\n");
        }

        if (p.Series.Count > 0)
            foreach (var s in p.Series)
                sb.Append("<p><em>Part ").Append(s.Part).Append(" of the series “").Append(HtmlEncode(s.Title)).Append("”.</em></p>\n");

        return sb.ToString();
    }

    private static void AppendRoundup(StringBuilder sb, RoundupData r)
    {
        var ranked = r.Ranked.ToList();
        sb.Append("<h2>At a glance</h2>\n<table>\n<thead><tr><th>Rank</th><th>Product</th><th>Best for</th><th>Score</th></tr></thead>\n<tbody>\n");
        foreach (var e in ranked)
            sb.Append("<tr><td>").Append(e.Rank).Append("</td><td>").Append(HtmlEncode(e.Name))
              .Append("</td><td>").Append(HtmlEncode(e.BestFor ?? "")).Append("</td><td>")
              .Append(e.Score.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append("/10</td></tr>\n");
        sb.Append("</tbody>\n</table>\n");

        foreach (var e in ranked)
        {
            sb.Append("<h2>").Append(e.Rank).Append(". ").Append(HtmlEncode(e.Name));
            if (!string.IsNullOrWhiteSpace(e.Badge)) sb.Append(" — ").Append(HtmlEncode(e.Badge));
            sb.Append("</h2>\n");
            if (!string.IsNullOrWhiteSpace(e.BestFor)) sb.Append("<p><strong>Best for:</strong> ").Append(HtmlEncode(e.BestFor)).Append("</p>\n");
            if (!string.IsNullOrWhiteSpace(e.Standout)) sb.Append("<p><strong>Standout:</strong> ").Append(HtmlEncode(e.Standout)).Append("</p>\n");
            // Body is trusted HTML from the editor (same as on the Inkwell page); plain text gets a paragraph.
            if (!string.IsNullOrWhiteSpace(e.Body))
                sb.Append(e.Body.TrimStart().StartsWith('<') ? e.Body : "<p>" + HtmlEncode(e.Body) + "</p>").Append('\n');
            if (e.Pros.Count > 0)
            {
                sb.Append("<p><strong>Strengths</strong></p>\n<ul>\n");
                foreach (var x in e.Pros) sb.Append("<li>").Append(HtmlEncode(x)).Append("</li>\n");
                sb.Append("</ul>\n");
            }
            if (e.Cons.Count > 0)
            {
                sb.Append("<p><strong>Limits</strong></p>\n<ul>\n");
                foreach (var x in e.Cons) sb.Append("<li>").Append(HtmlEncode(x)).Append("</li>\n");
                sb.Append("</ul>\n");
            }
            sb.Append("<p><strong>Score:</strong> ").Append(e.Score.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append("/10");
            if (e.HasWebsite)
                sb.Append(" · <a href=\"").Append(HtmlEncode(e.Website!)).Append("\" rel=\"nofollow noopener\">Visit website</a>");
            sb.Append("</p>\n");
        }

        if (r.Methodology.Count > 0)
        {
            sb.Append("<h2>How we scored</h2>\n<ol>\n");
            foreach (var m in r.Methodology)
                sb.Append("<li><strong>").Append(HtmlEncode(m.Step)).Append("</strong> ").Append(HtmlEncode(m.Detail)).Append("</li>\n");
            sb.Append("</ol>\n");
        }
    }

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); }
        catch (JsonException) { return null; }
    }

    // ── Upload references ───────────────────────────────────────────────────────

    // src/href/srcset values that point into this site's uploads folder, relative or absolute.
    private static readonly Regex UploadRef = new(
        @"(?<pre>\b(?:src|href|srcset|poster)\s*=\s*[""']?)(?<origin>https?://[^/""'\s>]+)?(?<path>/uploads/[^""'\s>?#,]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns the upload paths an HTML fragment refers to, and the fragment with each reference rewritten
    /// by <paramref name="rewrite"/> (which receives the site-relative path, e.g. <c>/uploads/a/b.jpg</c>).
    /// Absolute references are matched only when their origin is <paramref name="siteOrigin"/>; other
    /// sites' <c>/uploads/</c> are left alone.
    /// </summary>
    public static string RewriteUploads(string html, string siteOrigin, Func<string, string> rewrite, ISet<string> found)
    {
        if (string.IsNullOrEmpty(html)) return html ?? "";
        var origin = siteOrigin.TrimEnd('/');
        return UploadRef.Replace(html, m =>
        {
            var o = m.Groups["origin"].Value;
            if (o.Length > 0 && !string.Equals(o, origin, StringComparison.OrdinalIgnoreCase)) return m.Value;
            var path = UrlDecode(m.Groups["path"].Value);
            found.Add(path);
            return m.Groups["pre"].Value + rewrite(path);
        });
    }

    /// <summary>The site-relative upload path of an image reference, or null when it is not one of ours.</summary>
    public static string? UploadPathOf(string? url, string siteOrigin)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var u = url.Trim();
        var origin = siteOrigin.TrimEnd('/');
        if (u.StartsWith(origin + "/", StringComparison.OrdinalIgnoreCase)) u = u[origin.Length..];
        if (!u.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) return null;
        var cut = u.IndexOfAny(new[] { '?', '#' });
        return UrlDecode(cut >= 0 ? u[..cut] : u);
    }

    /// <summary>Ghost's location for an upload: <c>/uploads/x/y.jpg</c> → <c>/content/images/x/y.jpg</c>.</summary>
    public static string GhostImagePath(string uploadPath) => "/content/images/" + uploadPath["/uploads/".Length..];

    /// <summary>Cuts text to Ghost's field limits at a word boundary; the caller counts truncations.</summary>
    public static string? Limit(string? s, int max, ref int truncated)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        truncated++;
        var cut = s[..max];
        var space = cut.LastIndexOf(' ');
        return (space > max / 2 ? cut[..space] : cut).TrimEnd() ;
    }
}
