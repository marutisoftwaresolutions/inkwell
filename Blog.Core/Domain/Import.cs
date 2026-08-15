using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blog.Core.Domain;

public enum ImportSource { WordPress, Ghost }

public enum ImportJobStatus { Draft, Analyzed, Running, Paused, Completed, Failed, Cancelled }

public enum ImportItemType { Author, Category, Tag, Image, Post, Page, Comment }

public enum ImportItemStatus { Pending, Selected, Imported, Skipped, Failed }

/// <summary>
/// A migration run (one uploaded WordPress/Ghost export). Tracks overall status + counts.
/// Per-unit detail lives in <see cref="ImportItem"/> rows so progress, exceptions and resume work.
/// </summary>
public class ImportJob
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public ImportSource Source { get; set; }
    public string? FileName { get; set; }
    /// <summary>Server temp path of the uploaded export file (quarantined). Deleted after Completed/Cancelled.</summary>
    public string? FilePath { get; set; }
    public ImportJobStatus Status { get; set; } = ImportJobStatus.Draft;
    public string? OptionsJson { get; set; }
    public int TotalItems { get; set; }
    public int ImportedItems { get; set; }
    public int FailedItems { get; set; }
    public int SkippedItems { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    [JsonIgnore]
    public ImportOptions Options
    {
        get
        {
            if (string.IsNullOrWhiteSpace(OptionsJson)) return new ImportOptions();
            try { return JsonSerializer.Deserialize<ImportOptions>(OptionsJson) ?? new ImportOptions(); }
            catch (JsonException) { return new ImportOptions(); }
        }
    }
}

/// <summary>One source unit (a post, image, term, author, …) and its import outcome.</summary>
public class ImportItem
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public ImportItemType ItemType { get; set; }
    /// <summary>Stable source key (WP post_id / attachment URL / term slug / author login) — dedup + re-run.</summary>
    public string SourceId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public ImportItemStatus Status { get; set; } = ImportItemStatus.Pending;
    /// <summary>Id of the created entity (Post/Media/Category/…). Enables rollback and cross-references.</summary>
    public Guid? TargetId { get; set; }
    public string? Error { get; set; }
    /// <summary>Processing order: lower first (authors→terms→images→posts→comments) so dependencies exist.</summary>
    public int Ordinal { get; set; }
    /// <summary>Serialized parsed payload (a Parsed* DTO) so batches never re-parse the export file.</summary>
    public string? DataJson { get; set; }
}

/// <summary>User-chosen import scope + policies (serialized to <see cref="ImportJob.OptionsJson"/>).</summary>
public class ImportOptions
{
    public bool ImportPosts { get; set; } = true;
    public bool ImportPages { get; set; } = true;
    public bool ImportCategories { get; set; } = true;
    public bool ImportTags { get; set; } = true;
    public bool ImportAuthors { get; set; } = true;
    public bool ImportComments { get; set; }
    public bool ImportImages { get; set; } = true;
    public bool ConvertToWebP { get; set; } = true;
    public bool PublishedOnly { get; set; }
    public bool SanitizeHtml { get; set; } = true;
    public bool CreateRedirects { get; set; } = true;
    /// <summary>Skip | Overwrite | Suffix (append -2, -3, …).</summary>
    public string SlugConflict { get; set; } = "Suffix";
    /// <summary>When set, every imported post is attributed to this user (authors not imported as new users).</summary>
    public Guid? DefaultAuthorId { get; set; }
}

/// <summary>Lightweight preview of what an export contains (shown before the import runs).</summary>
public class ImportManifest
{
    public ImportSource Source { get; set; }
    public int Posts { get; set; }
    public int PublishedPosts { get; set; }
    public int DraftPosts { get; set; }
    public int Pages { get; set; }
    public int Categories { get; set; }
    public int Tags { get; set; }
    public int Authors { get; set; }
    public int Comments { get; set; }
    public int Images { get; set; }
    public string? DetectedFormat { get; set; }
    public List<string> Warnings { get; set; } = new();
}

// ── Parsed payload DTOs (serialized into ImportItem.DataJson) ─────────────────────────────

public class ParsedPost
{
    public string Title { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? Html { get; set; }
    public string? Excerpt { get; set; }
    /// <summary>WordPress status: publish / draft / pending / private / future.</summary>
    public string Status { get; set; } = "publish";
    public DateTime? PublishedAt { get; set; }
    public string? AuthorLogin { get; set; }
    /// <summary>Original permalink (for a SEO redirect old-URL → new-slug).</summary>
    public string? OriginalLink { get; set; }
    public List<string> CategorySlugs { get; set; } = new();
    public List<string> TagSlugs { get; set; } = new();
    public string? FeatureImageUrl { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public bool IsPage { get; set; }
}

public class ParsedTerm
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsTag { get; set; }
}

public class ParsedAuthor
{
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public class ParsedImage
{
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
}

public class ParsedComment
{
    public string PostSourceId { get; set; } = string.Empty;
    public string? AuthorName { get; set; }
    public string? AuthorEmail { get; set; }
    public string? AuthorUrl { get; set; }
    public string? Content { get; set; }
    public DateTime? Date { get; set; }
    public bool Approved { get; set; }
}

/// <summary>A single unit emitted by a parser: its type/order + a Parsed* payload.</summary>
public class ParsedRecord
{
    public ImportItemType Type { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public int Ordinal { get; set; }
    public object Payload { get; set; } = null!;
}
