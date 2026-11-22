using System.Globalization;
using System.Text;
using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// A post as one Markdown document, for a client that asks for the text without the page around it:
/// the title, where it lives, when it was published and last changed, the short answer and key facts
/// when the post has them, the body, and the questions and answers. It carries exactly what a reader
/// sees on the page, nothing kept from readers. A post written in Markdown is given as it was written.
/// Also decides when a request is asking for this form. Pure.
/// </summary>
public static class PostMarkdown
{
    public const string ContentType = "text/markdown; charset=utf-8";

    /// <summary>
    /// True when the request's Accept header asks for Markdown ahead of HTML: it names
    /// <c>text/markdown</c> and either does not name <c>text/html</c> or names Markdown first. A browser
    /// (which leads with text/html) never matches.
    /// </summary>
    public static bool Wanted(string? acceptHeader)
    {
        var accept = (acceptHeader ?? "").ToLowerInvariant();
        var markdown = accept.IndexOf("text/markdown", StringComparison.Ordinal);
        if (markdown < 0) return false;
        var html = accept.IndexOf("text/html", StringComparison.Ordinal);
        return html < 0 || markdown < html;
    }

    public static string Build(Post post, string canonicalUrl, string? siteName, string? authorName)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(OneLine(post.Title)).Append("\n\n");

        var facts = new List<string> { $"Source: {canonicalUrl}" };
        if (!string.IsNullOrWhiteSpace(siteName)) facts.Add("Site: " + OneLine(siteName));
        if (!string.IsNullOrWhiteSpace(authorName)) facts.Add("Author: " + OneLine(authorName));
        if (post.PublishedAt is DateTime published) facts.Add("Published: " + published.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (post.UpdatedAt > (post.PublishedAt ?? DateTime.MinValue).AddDays(1)) facts.Add("Updated: " + post.UpdatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (post.LastVerifiedAt is DateTime verified) facts.Add("Last verified: " + verified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        foreach (var fact in facts) sb.Append("- ").Append(fact).Append('\n');
        sb.Append('\n');

        if (!string.IsNullOrWhiteSpace(post.MetaDescription)) sb.Append("> ").Append(OneLine(post.MetaDescription)).Append("\n\n");
        if (post.HasAnswerCapsule) sb.Append("## In short\n\n").Append(OneLine(post.AnswerCapsule!)).Append("\n\n");

        var keyFacts = post.KeyFacts.Where(f => !string.IsNullOrWhiteSpace(f.Label) && !string.IsNullOrWhiteSpace(f.Value)).ToList();
        if (keyFacts.Count > 0)
        {
            sb.Append("## Key facts\n\n");
            foreach (var f in keyFacts) sb.Append("- **").Append(OneLine(f.Label)).Append(":** ").Append(OneLine(f.Value)).Append('\n');
            sb.Append('\n');
        }

        Uri.TryCreate(canonicalUrl, UriKind.Absolute, out var baseUrl);
        var body = post.IsMarkdown && !string.IsNullOrWhiteSpace(post.Markdown) ? post.Markdown!.Trim() : HtmlToMarkdown.Convert(post.Html, baseUrl);
        if (body.Length > 0) sb.Append(body).Append("\n\n");

        var faqs = post.Faqs.Where(f => !string.IsNullOrWhiteSpace(f.Question) && !string.IsNullOrWhiteSpace(f.Answer)).ToList();
        if (faqs.Count > 0)
        {
            sb.Append("## Questions and answers\n\n");
            foreach (var f in faqs) sb.Append("### ").Append(OneLine(f.Question)).Append("\n\n").Append(HtmlToMarkdown.Convert(f.Answer, baseUrl)).Append("\n\n");
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
