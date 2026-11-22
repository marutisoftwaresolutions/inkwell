using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface ITagRepository
{
    Task<List<Tag>> GetAllAsync(Guid authorId);
    Task<List<Tag>> GetPublicAsync(Guid authorId);
    Task<Tag?> GetByIdAsync(Guid id, Guid authorId);
    Task<Tag?> GetBySlugAsync(string slug, Guid authorId);
    Task<Tag> GetOrCreateAsync(string name, Guid authorId);
    Task<Guid> CreateAsync(Tag tag);
    /// <summary>Sets or clears (null) the tag's description. The caller has normalised it with ArchiveIntro.</summary>
    Task SetDescriptionAsync(Guid id, Guid authorId, string? description);
    Task DeleteAsync(Guid id, Guid authorId);
    
    // Note: Used heavily in Public views where posts are queried universally, keep universal
    Task<List<Tag>> GetForPostAsync(Guid postId);
}
