using System.Text.Json;
using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// What a revision captures and how it goes back. Pure: capture a post or page into a
/// <see cref="Revision"/>, decide whether two captures are the same, and apply a revision to a live
/// entity. Slug and status are captured for the record but never restored — restoring a slug would
/// break a URL that may be indexed, and restoring a status would republish or unpublish silently.
/// </summary>
public static class RevisionSnapshot
{
    public const string PostType = "Post";
    public const string PageType = "Page";

    /// <summary>Inkwell-only blocks travel together as one JSON bag so the table stays flat.</summary>
    private sealed record StructuredBag(string? FaqJson, string? KeyFactsJson, string? HowToJson, string? RoundupJson, string? AnswerCapsule);

    public static Revision FromPost(Post post, string reason, Guid authorId, string? authorName) => new()
    {
        EntityType = PostType,
        EntityId = post.Id,
        Title = post.Title ?? string.Empty,
        Slug = post.Slug ?? string.Empty,
        Html = post.Html ?? string.Empty,
        MetaTitle = post.MetaTitle,
        MetaDescription = post.MetaDescription,
        StructuredJson = JsonSerializer.Serialize(new StructuredBag(post.FaqJson, post.KeyFactsJson, post.HowToJson, post.RoundupJson, post.AnswerCapsule)),
        Status = post.Status.ToString(),
        Reason = reason,
        AuthorId = authorId,
        AuthorName = authorName,
    };

    public static Revision FromPage(Page page, string reason, Guid authorId, string? authorName) => new()
    {
        EntityType = PageType,
        EntityId = page.Id,
        Title = page.Title ?? string.Empty,
        Slug = page.Slug ?? string.Empty,
        Html = page.Content ?? string.Empty,
        MetaTitle = page.MetaTitle,
        MetaDescription = page.MetaDescription,
        StructuredJson = null,
        Status = page.IsPublished ? "Published" : "Draft",
        Reason = reason,
        AuthorId = authorId,
        AuthorName = authorName,
    };

    /// <summary>
    /// True when nothing an author would call "content" differs: title, body, meta and structured
    /// blocks. Status and slug changes alone do not earn a revision — the audit trail records those.
    /// </summary>
    public static bool SameContent(Revision a, Revision b) =>
        string.Equals(a.Title, b.Title, StringComparison.Ordinal)
        && string.Equals(a.Html, b.Html, StringComparison.Ordinal)
        && string.Equals(a.MetaTitle ?? string.Empty, b.MetaTitle ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(a.MetaDescription ?? string.Empty, b.MetaDescription ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(a.StructuredJson ?? string.Empty, b.StructuredJson ?? string.Empty, StringComparison.Ordinal);

    /// <summary>Puts the revision's content back onto the post. Slug, status, dates and taxonomy are untouched.</summary>
    public static void ApplyToPost(Revision revision, Post post)
    {
        post.Title = revision.Title;
        post.Html = revision.Html;
        post.Plaintext = TextHelper.CountWords(revision.Html) == 0 ? string.Empty : StripTags(revision.Html);
        post.MetaTitle = revision.MetaTitle;
        post.MetaDescription = revision.MetaDescription;

        var bag = ParseBag(revision.StructuredJson);
        post.FaqJson = bag.FaqJson;
        post.KeyFactsJson = bag.KeyFactsJson;
        post.HowToJson = bag.HowToJson;
        post.RoundupJson = bag.RoundupJson;
        post.AnswerCapsule = bag.AnswerCapsule;
    }

    /// <summary>Puts the revision's content back onto the page. Slug and publish state are untouched.</summary>
    public static void ApplyToPage(Revision revision, Page page)
    {
        page.Title = revision.Title;
        page.Content = revision.Html;
        page.MetaTitle = revision.MetaTitle;
        page.MetaDescription = revision.MetaDescription;
    }

    private static StructuredBag ParseBag(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new StructuredBag(null, null, null, null, null);
        try { return JsonSerializer.Deserialize<StructuredBag>(json) ?? new StructuredBag(null, null, null, null, null); }
        catch (JsonException) { return new StructuredBag(null, null, null, null, null); }
    }

    private static string StripTags(string html) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " "))
            .Replace('\r', ' ').Replace('\n', ' ').Trim();
}
