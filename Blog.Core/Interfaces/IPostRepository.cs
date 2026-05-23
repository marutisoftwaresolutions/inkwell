using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalItems { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);
}

public class PostFilter
{
    public string? Search { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? TagId { get; set; }
    public List<string> Categories { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public Guid? AuthorId { get; set; }
    public PostStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public interface IPostRepository
{
    Task<PagedResult<Post>> GetPostsAsync(PostFilter filter);
    Task<Post?> GetByIdAsync(Guid id, Guid? authorId = null);
    Task<Post?> GetBySlugAsync(string slug, Guid? authorId = null);
    Task<Guid> CreateAsync(Post post);
    Task UpdateAsync(Post post);
    Task DeleteAsync(Guid id, Guid? authorId = null);
    Task IncrementViewCountAsync(Guid id);
    Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null);
    Task<int> GetTotalCountAsync(PostStatus? status = null, Guid? authorId = null);
    Task<int> GetViewsTodayAsync(Guid? authorId = null);
    Task<List<Post>> GetRecentPostsAsync(int count = 5, Guid? authorId = null);
    Task AssignCategoriesAsync(Guid postId, List<Guid> categoryIds);
    Task AssignTagsAsync(Guid postId, List<Guid> tagIds);
    Task<List<Post>> GetRelatedPostsAsync(Guid postId, List<Guid> categoryIds, List<Guid> tagIds, int count = 3);
}
