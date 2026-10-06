using System.Net;
using System.Text;

namespace Blog.Core.Services;

/// <summary>A post featured in a newsletter, already resolved to absolute URLs.</summary>
public sealed record NewsletterPost(string Title, string Url, string? Excerpt, string? ImageUrl, string? DateText);

/// <summary>What an issue says, ready to render.</summary>
public sealed record NewsletterContent(
    string SiteName, string SiteUrl, string Subject, string? Preheader, string? IntroMarkdown,
    IReadOnlyList<NewsletterPost> Posts, string Language = "en");

/// <summary>
/// Renders a newsletter as email (ADR-0028): HTML with inline styles in a single 600 px table (what mail
/// clients render reliably), and a plain-text alternative. Every value is encoded and every link absolute.
/// The per-recipient unsubscribe link is left as <see cref="UnsubscribePlaceholder"/> and filled in by
/// <see cref="Personalize"/> when each copy is sent, so the issue is rendered once and frozen.
/// </summary>
public static class NewsletterEmail
{
    public const string UnsubscribePlaceholder = "{{unsubscribe_url}}";

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    public static (string Html, string Text) Render(NewsletterContent c)
    {
        var site = c.SiteUrl.TrimEnd('/');
        // Raw HTML in the intro is shown as typed, never passed into the email.
        var introSource = string.IsNullOrWhiteSpace(c.IntroMarkdown) ? "" : MarkdownRenderer.ToHtmlWithoutRawHtml(c.IntroMarkdown);
        var introHtml = introSource.Length == 0 ? "" : AbsoluteLinks(introSource, site);
        var introText = MarkdownRenderer.ToPlainLines(introSource);

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"").Append(E(string.IsNullOrWhiteSpace(c.Language) ? "en" : c.Language)).Append("\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            // A light design: ask mail apps not to half-invert it in dark mode.
            .Append("<meta name=\"color-scheme\" content=\"light\"><meta name=\"supported-color-schemes\" content=\"light\">")
            .Append("<title>").Append(E(c.Subject)).Append("</title></head>")
            .Append("<body style=\"margin:0;padding:0;background:#f4f4f2;\">");
        if (!string.IsNullOrWhiteSpace(c.Preheader))
            // Shown by inbox lists after the subject; hidden in the message itself.
            html.Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">").Append(E(c.Preheader)).Append("</div>");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f4f4f2;\"><tr><td align=\"center\" style=\"padding:24px 12px;\">")
            .Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;max-width:600px;background:#ffffff;border-radius:8px;font-family:Georgia,'Times New Roman',serif;color:#1f1f1f;\">")
            .Append("<tr><td style=\"padding:28px 32px 8px;font-family:Arial,Helvetica,sans-serif;font-size:13px;letter-spacing:.06em;text-transform:uppercase;color:#6b6b6b;\">")
            .Append("<a href=\"").Append(E(site)).Append("\" style=\"color:#6b6b6b;text-decoration:none;\">").Append(E(c.SiteName)).Append("</a></td></tr>")
            .Append("<tr><td style=\"padding:4px 32px 0;\"><h1 style=\"margin:0;font-size:26px;line-height:1.25;font-weight:700;\">").Append(E(c.Subject)).Append("</h1></td></tr>");
        if (introHtml.Length > 0)
            html.Append("<tr><td style=\"padding:16px 32px 0;font-size:17px;line-height:1.6;\">").Append(introHtml).Append("</td></tr>");
        foreach (var p in c.Posts)
        {
            var url = Absolute(p.Url, site);
            html.Append("<tr><td style=\"padding:24px 32px 0;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-top:1px solid #e6e6e2;\"><tr><td style=\"padding-top:20px;\">");
            if (!string.IsNullOrWhiteSpace(p.ImageUrl))
                html.Append("<a href=\"").Append(E(url)).Append("\"><img src=\"").Append(E(Absolute(p.ImageUrl!, site)))
                    .Append("\" alt=\"").Append(E(p.Title)).Append("\" width=\"536\" style=\"display:block;width:100%;max-width:536px;height:auto;border:0;border-radius:6px;\"></a>");
            html.Append("<h2 style=\"margin:14px 0 6px;font-size:21px;line-height:1.3;\"><a href=\"").Append(E(url))
                .Append("\" style=\"color:#1f1f1f;text-decoration:none;\">").Append(E(p.Title)).Append("</a></h2>");
            if (!string.IsNullOrWhiteSpace(p.DateText))
                html.Append("<p style=\"margin:0 0 8px;font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#6b6b6b;\">").Append(E(p.DateText)).Append("</p>");
            if (!string.IsNullOrWhiteSpace(p.Excerpt))
                html.Append("<p style=\"margin:0 0 10px;font-size:16px;line-height:1.55;\">").Append(E(p.Excerpt)).Append("</p>");
            html.Append("<p style=\"margin:0;font-family:Arial,Helvetica,sans-serif;font-size:15px;\"><a href=\"").Append(E(url))
                .Append("\" style=\"color:#1a5fb4;\">Read “").Append(E(p.Title)).Append("”</a></p></td></tr></table></td></tr>");
        }
        html.Append("<tr><td style=\"padding:32px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:1.5;color:#6b6b6b;\">")
            .Append("You are receiving this because you subscribed to <a href=\"").Append(E(site)).Append("\" style=\"color:#6b6b6b;\">").Append(E(c.SiteName)).Append("</a>. ")
            .Append("<a href=\"").Append(UnsubscribePlaceholder).Append("\" style=\"color:#6b6b6b;\">Unsubscribe</a>")
            .Append("</td></tr></table></td></tr></table></body></html>");

        var text = new StringBuilder();
        text.AppendLine(c.SiteName).AppendLine().AppendLine(c.Subject).AppendLine(new string('=', Math.Min(c.Subject.Length, 60))).AppendLine();
        if (introText.Length > 0) text.AppendLine(introText).AppendLine();
        foreach (var p in c.Posts)
        {
            text.AppendLine(p.Title);
            if (!string.IsNullOrWhiteSpace(p.DateText)) text.AppendLine(p.DateText);
            if (!string.IsNullOrWhiteSpace(p.Excerpt)) text.AppendLine(p.Excerpt);
            text.AppendLine("Read the article: " + Absolute(p.Url, site)).AppendLine();
        }
        text.AppendLine("-- ").AppendLine($"You are receiving this because you subscribed to {c.SiteName}.")
            .AppendLine("Unsubscribe: " + UnsubscribePlaceholder);
        return (html.ToString(), text.ToString());
    }

    /// <summary>One recipient's copy: the frozen issue with their own unsubscribe link.</summary>
    public static string Personalize(string rendered, string unsubscribeUrl, bool html) =>
        rendered.Replace(UnsubscribePlaceholder, html ? WebUtility.HtmlEncode(unsubscribeUrl) : unsubscribeUrl, StringComparison.Ordinal);

    /// <summary>The address a recipient's unsubscribe link and one-click header point at.</summary>
    public static string UnsubscribeUrl(string siteUrl, string token) => $"{siteUrl.TrimEnd('/')}/newsletter/unsubscribe/{Uri.EscapeDataString(token)}";

    private static string Absolute(string url, string site) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? url : site + "/" + url.TrimStart('/');

    // Links and images in the intro written as site paths ("/my-post") must work from an inbox.
    private static string AbsoluteLinks(string html, string site) =>
        System.Text.RegularExpressions.Regex.Replace(html, "(href|src)=\"/(?!/)", m => $"{m.Groups[1].Value}=\"{site}/");
}
