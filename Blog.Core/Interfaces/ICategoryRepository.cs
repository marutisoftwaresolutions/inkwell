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
    Task DeleteAsync(Guid id, Guid authorId);
    Task<bool> SlugExistsAsync(string slug, Guid authorId, Guid? excludeId = null);
    Task<List<Category>> GetForPostAsync(Guid postId);
}
