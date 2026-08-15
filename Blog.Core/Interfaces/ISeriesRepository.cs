using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface ISeriesRepository
{
    Task<List<Series>> GetAllAsync(Guid authorId);
    Task<Series?> GetByIdAsync(Guid id, Guid authorId);
    Task<Series?> GetBySlugAsync(string slug, Guid authorId);
    Task<Guid> CreateAsync(Series series);
    Task UpdateAsync(Series series);
    Task DeleteAsync(Guid id, Guid authorId);
    Task<bool> SlugExistsAsync(string slug, Guid authorId, Guid? excludeId = null);

    /// <summary>Posts in a series, ordered by SortOrder. Set <paramref name="publishedOnly"/> for public views.</summary>
    Task<List<Post>> GetPostsAsync(Guid seriesId, bool publishedOnly);

    /// <summary>Replaces the series' post membership with the given posts, in the order supplied.</summary>
    Task SetPostsAsync(Guid seriesId, IList<Guid> orderedPostIds);

    /// <summary>The series a post belongs to (the earliest-created one, if several), scoped to the owner. Null if none.</summary>
    Task<Series?> GetForPostAsync(Guid postId, Guid authorId);
}
