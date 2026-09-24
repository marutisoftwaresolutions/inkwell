namespace Blog.Core.Domain;

/// <summary>
/// A content snapshot of a post or page, written on every saved change and restorable from the
/// editor. Slug and status are recorded so the list can say what was live at the time, but a
/// restore never touches them — see <c>RevisionSnapshot</c>.
/// </summary>
public class Revision
{
    public Guid Id { get; set; }
    /// <summary>"Post" or "Page".</summary>
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    /// <summary>1, 2, 3… per entity, in the order written. Stable even after older ones are pruned.</summary>
    public int Number { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    /// <summary>Inkwell-only blocks (FAQ, Key Facts, How-To, roundup, answer capsule) as one JSON bag. Null for pages.</summary>
    public string? StructuredJson { get; set; }

    public string Status { get; set; } = string.Empty;
    /// <summary>Why it was written: Saved, Published, Scheduled, Created, Restored from #n.</summary>
    public string Reason { get; set; } = string.Empty;
    public Guid AuthorId { get; set; }
    public string? AuthorName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Body length, for the list — filled by the summary query so bodies are not transferred.</summary>
    public int HtmlLength { get; set; }
}
