namespace Blog.Core.Domain;

public class Post
{
    public Guid Id { get; set; }
    public Guid Uuid { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    
    public string? Html { get; set; }
    public string? Plaintext { get; set; }
    public string? Type { get; set; } = "post";
    public string? Visibility { get; set; } = "public";
    public string? FeatureImage { get; set; }
    public string? FeaturedImageUrl => FeatureImage;

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? CanonicalUrl { get; set; }
    public string? OgImage { get; set; }
    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    public string? TwitterImage { get; set; }
    public string? TwitterTitle { get; set; }
    public string? TwitterDescription { get; set; }


    public Guid AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? AuthorCredentials { get; set; }  // denormalized from Users.Credentials
    public string? AuthorSpecialty { get; set; }    // denormalized from Users.Specialty
    public PostStatus Status { get; set; } = PostStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime? NextReviewAt { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public int ViewCount { get; set; }
    
    public bool AllowComments { get; set; } = true;
    public List<Category> Categories { get; set; } = new();
    public List<Tag> Tags { get; set; } = new();
    public int CommentCount { get; set; }

    public string? FaqJson { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public List<FaqItem> Faqs =>
        string.IsNullOrEmpty(FaqJson) ? new() :
        System.Text.Json.JsonSerializer.Deserialize<List<FaqItem>>(FaqJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
}

public enum PostStatus
{
    Draft,
    Published,
    Scheduled,
}

public record FaqItem(string Question, string Answer);
