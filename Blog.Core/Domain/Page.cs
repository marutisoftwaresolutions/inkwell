namespace Blog.Core.Domain;

public class Page
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Guid AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public bool IsInNav { get; set; } = true;
    public int SortOrder { get; set; }
    public Guid? ParentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public Guid? FeaturedImageId { get; set; }
    public string? FeaturedImageUrl { get; set; }

    // SEO & Social
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? CanonicalUrl { get; set; }
    
    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    public string? OgImage { get; set; }
    
    public string? TwitterTitle { get; set; }
    public string? TwitterDescription { get; set; }

    /// <summary>Custom field values, a flat { key: value } JSON object (ADR-0025). Read through CustomFields.</summary>
    public string? CustomFieldsJson { get; set; }
    public string? TwitterImage { get; set; }
}
