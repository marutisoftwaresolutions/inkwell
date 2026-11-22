using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface ICategoryRepository
{
    Task<List<Category>> GetAllAsync(Guid authorId);
    Task<List<Category>> GetPublicAsync(Guid authorId);
    Task<Category?> GetByIdAsync(Guid id, Guid authorId);
    Task<Category?> GetBySlugAsync(string slug, Guid authorId);
    Task<Guid> CreateAsync(Category category);
    Task UpdateAsync(Category category);
    /// <summary>Places a category under another, or at the top level (null). The caller has checked the move with CategoryTree.</summary>
    Task SetParentAsync(Guid id, Guid authorId, Guid? parentId);
    /// <summary>Sets or clears (null) the category's description. The caller has normalised it with ArchiveIntro.</summary>
    Task SetDescriptionAsync(Guid id, Guid authorId, string? description);
    Task DeleteAsync(Guid id, Guid authorId);
    Task<bool> SlugExistsAsync(string slug, Guid authorId, Guid? excludeId = null);
    Task<List<Category>> GetForPostAsync(Guid postId);
}
