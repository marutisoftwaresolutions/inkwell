using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// Makes the draft that "Duplicate" creates from a post or a page. The copy carries everything an
/// author would otherwise retype (text, blocks, images, search and social fields, custom fields) and
/// nothing that belongs to the original's life on the site: it is a draft, has no dates, no views,
/// no review note, no address yet and no canonical address (which would point search engines at the
/// original). Pure; the caller saves it.
/// </summary>
public static class ContentCopy
{
    public const string Suffix = " (copy)";
    public const int MaxTitleLength = 255;

    /// <summary>The original's title with " (copy)" added once, shortened so the result still fits the column.</summary>
    public static string CopyTitle(string? title)
    {
        var name = (title ?? "").Trim();
        if (name.Length == 0) name = "Untitled";
        if (name.Length + Suffix.Length > MaxTitleLength) name = name[..(MaxTitleLength - Suffix.Length)].TrimEnd();
        return name + Suffix;
    }

    /// <summary>A draft copy of <paramref name="source"/> owned by <paramref name="authorId"/>. Categories and tags are assigned by the caller.</summary>
    public static Post Of(Post source, Guid authorId) => new()
    {
        Id = Guid.Empty,
        Uuid = Guid.NewGuid(),
        Title = CopyTitle(source.Title),
        Slug = string.Empty,                       // the post service makes a free one from the title
        Html = source.Html,
        Plaintext = source.Plaintext,
        Markdown = source.Markdown,
        EditorMode = source.EditorMode,
        Type = source.Type,
        Visibility = source.Visibility,
        FeatureImage = source.FeatureImage,
        MetaTitle = source.MetaTitle,
        MetaDescription = source.MetaDescription,
        CanonicalUrl = null,
        OgImage = source.OgImage,
        OgTitle = source.OgTitle,
        OgDescription = source.OgDescription,
        TwitterImage = source.TwitterImage,
        TwitterTitle = source.TwitterTitle,
        TwitterDescription = source.TwitterDescription,
        AuthorId = authorId,
        Status = PostStatus.Draft,
        PublishedAt = null,
        ScheduledAt = null,
        LastVerifiedAt = null,
        NextReviewAt = null,
        ReviewNote = null,
        ReviewRequestedAt = null,
        ViewCount = 0,
        AllowComments = source.AllowComments,
        FaqJson = source.FaqJson,
        AnswerCapsule = source.AnswerCapsule,
        KeyFactsJson = source.KeyFactsJson,
        HowToJson = source.HowToJson,
        RoundupJson = source.RoundupJson,
        CustomFieldsJson = source.CustomFieldsJson,
    };

    /// <summary>A draft copy of <paramref name="source"/> with the address <paramref name="slug"/>; it is not in the navigation until someone puts it there.</summary>
    public static Page Of(Page source, Guid authorId, string slug) => new()
    {
        Id = Guid.Empty,
        Title = CopyTitle(source.Title),
        Slug = slug,
        Content = source.Content,
        Markdown = source.Markdown,
        EditorMode = source.EditorMode,
        AuthorId = authorId,
        IsPublished = false,
        IsInNav = false,
        SortOrder = source.SortOrder,
        ParentId = source.ParentId,
        PublishedAt = null,
        FeaturedImageId = source.FeaturedImageId,
        FeaturedImageUrl = source.FeaturedImageUrl,
        MetaTitle = source.MetaTitle,
        MetaDescription = source.MetaDescription,
        CanonicalUrl = null,
        OgTitle = source.OgTitle,
        OgDescription = source.OgDescription,
        OgImage = source.OgImage,
        TwitterTitle = source.TwitterTitle,
        TwitterDescription = source.TwitterDescription,
        TwitterImage = source.TwitterImage,
        CustomFieldsJson = source.CustomFieldsJson,
    };

    /// <summary>
    /// The address to try for a copied page: the original's with "-copy", then "-copy-2", "-copy-3" …
    /// <paramref name="attempt"/> starts at 1. Kept within the column's length.
    /// </summary>
    public static string CopySlug(string? slug, int attempt)
    {
        var stem = string.IsNullOrWhiteSpace(slug) ? "page" : slug.Trim();
        var tail = attempt <= 1 ? "-copy" : $"-copy-{attempt}";
        if (stem.Length + tail.Length > MaxTitleLength) stem = stem[..(MaxTitleLength - tail.Length)].TrimEnd('-');
        return stem + tail;
    }
}
