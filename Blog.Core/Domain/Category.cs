namespace Blog.Core.Domain;

public class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public Guid AuthorId { get; set; }
    /// <summary>The category this one sits under, or null at the top level. See <see cref="Blog.Core.Services.CategoryTree"/>.</summary>
    public Guid? ParentId { get; set; }
    /// <summary>Optional introduction shown on the category's public page and used as its meta description. Plain text with [text](/path) links; see <see cref="Blog.Core.Services.ArchiveIntro"/>.</summary>
    public string? Description { get; set; }
    public int PostCount { get; set; }
}
