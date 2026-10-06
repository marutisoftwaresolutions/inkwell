using Blog.Core.Domain;

namespace Blog.Core.Services.Export;

/// <summary>Which foreign format an export is written in (ADR-0020).</summary>
public enum ExportTarget { WordPress, Ghost }

/// <summary>What the operator chose on Admin → Export.</summary>
public sealed record ExportOptions
{
    /// <summary>Drafts, posts in review and posts sent back. Off by default: an export is usually a move of the public site.</summary>
    public bool IncludeDrafts { get; init; }
    public bool IncludePages { get; init; } = true;
    /// <summary>WordPress only; Ghost cannot import comments.</summary>
    public bool IncludeComments { get; init; } = true;
    /// <summary>
    /// Author and commenter email addresses. Off by default (personal data). Ghost requires a valid email
    /// for every user, so without this Ghost credits every post to the account that imports it.
    /// </summary>
    public bool IncludeEmails { get; init; }
}

/// <summary>The site an export comes from.</summary>
public sealed record ExportSite(string Title, string Description, string BaseUrl, string Language, TimeZoneInfo TimeZone);

public sealed record ExportTerm(Guid Id, string Name, string Slug);

public sealed record ExportAuthor(Guid Id, string Login, string DisplayName, string Slug, string? Email, string? Bio,
    string? Website, string? ProfileImage, string Role);

public sealed record ExportSeriesRef(string Title, string Slug, int Part);

public sealed record ExportComment(Guid Id, Guid? ParentId, string AuthorName, string? AuthorEmail, string? AuthorUrl,
    string Html, bool Approved, DateTime CreatedAtUtc);

/// <summary>Search and social metadata common to posts and pages.</summary>
public sealed record ExportSeo(string? MetaTitle, string? MetaDescription, string? CanonicalUrl,
    string? OgTitle, string? OgDescription, string? OgImage,
    string? TwitterTitle, string? TwitterDescription, string? TwitterImage);

public sealed record ExportPost
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Html { get; init; } = "";
    public PostStatus Status { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public DateTime? PublishedAtUtc { get; init; }
    public DateTime? ScheduledAtUtc { get; init; }
    public Guid AuthorId { get; init; }
    public string? FeatureImage { get; init; }
    public bool AllowComments { get; init; } = true;
    public ExportSeo Seo { get; init; } = new(null, null, null, null, null, null, null, null, null);
    public string? AnswerCapsule { get; init; }
    public string? KeyFactsJson { get; init; }
    public string? FaqJson { get; init; }
    public string? HowToJson { get; init; }
    public string? RoundupJson { get; init; }
    /// <summary>Custom field values, { key: value } (ADR-0025).</summary>
    public string? CustomFieldsJson { get; init; }
    public IReadOnlyList<Guid> CategoryIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> TagIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<ExportSeriesRef> Series { get; init; } = Array.Empty<ExportSeriesRef>();
    public IReadOnlyList<ExportComment> Comments { get; init; } = Array.Empty<ExportComment>();
}

public sealed record ExportPage
{
    public Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public string Title { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Html { get; init; } = "";
    public bool IsPublished { get; init; }
    public int SortOrder { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public DateTime? PublishedAtUtc { get; init; }
    public Guid AuthorId { get; init; }
    public string? FeatureImage { get; init; }
    public ExportSeo Seo { get; init; } = new(null, null, null, null, null, null, null, null, null);
    /// <summary>Custom field values, { key: value } (ADR-0025).</summary>
    public string? CustomFieldsJson { get; init; }
}

/// <summary>Everything small enough to hold in memory: taxonomy, authors, image alt text. Bodies are streamed.</summary>
public sealed record ExportCatalog(
    IReadOnlyList<ExportTerm> Categories,
    IReadOnlyList<ExportTerm> Tags,
    IReadOnlyList<ExportAuthor> Authors,
    IReadOnlyDictionary<string, (string? Alt, string? Caption)> MediaByUrl);

/// <summary>Counts behind the fidelity screen, for the current selection.</summary>
public sealed record ExportCounts(int Posts, int Pages, int Categories, int Tags, int Authors, int Comments,
    int WithRoundup, int WithFaq, int WithHowTo, int WithKeyFacts, int WithAnswerCapsule, int InSeries,
    int WithSeo, int Drafts, int InReview, int WithCustomFields = 0);

/// <summary>Reads the content an export writes (implemented over the database in Infrastructure).</summary>
public interface IExportSource
{
    Task<ExportCounts> CountAsync(ExportOptions options);
    Task<ExportCatalog> LoadCatalogAsync(ExportOptions options);
    IAsyncEnumerable<ExportPost> StreamPostsAsync(ExportOptions options, CancellationToken ct);
    IAsyncEnumerable<ExportPage> StreamPagesAsync(ExportOptions options, CancellationToken ct);
}

/// <summary>What a writer produced: counts for the audit trail and the image files it referenced.</summary>
public sealed class ExportResult
{
    public int Posts { get; set; }
    public int Pages { get; set; }
    public int Comments { get; set; }
    public int Attachments { get; set; }
    public int Truncated { get; set; }
    /// <summary>Site-relative upload paths (<c>/uploads/…</c>) the export refers to, for bundling.</summary>
    public SortedSet<string> UploadPaths { get; } = new(StringComparer.Ordinal);
}
