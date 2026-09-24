using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public class CommentFilter
{
    public Guid? PostId { get; set; }
    public Guid? PostAuthorId { get; set; }
    public CommentStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public interface ICommentRepository
{
    Task<PagedResult<Comment>> GetCommentsAsync(CommentFilter filter);

    /// <summary>
    /// One page of comments matching <paramref name="filter"/> and the free-text <paramref name="q"/>
    /// (author name, email, body, post title), newest first. Paged in SQL like the media library;
    /// <paramref name="q"/> is data, never a pattern — LIKE metacharacters are escaped.
    /// </summary>
    Task<List<Comment>> SearchAsync(CommentFilter filter, string q);

    /// <summary>Total rows <see cref="SearchAsync"/> would return for the same filter and query.</summary>
    Task<int> CountSearchAsync(CommentFilter filter, string q);

    Task<Comment?> GetByIdAsync(Guid id);
    Task<Guid> CreateAsync(Comment comment);
    Task UpdateStatusAsync(Guid id, CommentStatus status);
    Task DeleteAsync(Guid id);
    Task<int> GetPendingCountAsync(Guid? postAuthorId = null);
    Task<List<Comment>> GetApprovedForPostAsync(Guid postId);

    /// <summary>
    /// Whether a comment with exactly this body was stored within <paramref name="window"/> on any
    /// post owned by <paramref name="postOwnerId"/>, under any author and any status. Feeds the spam
    /// filter's repeat detection. Scoped to the post owner so a repeat on one tenant's site can never
    /// block a genuine comment on another's.
    /// </summary>
    Task<bool> HasRecentDuplicateAsync(Guid postOwnerId, string content, TimeSpan window);

    /// <summary>Comments stored from <paramref name="authorIp"/> within <paramref name="window"/>.</summary>
    Task<int> CountRecentFromAddressAsync(string authorIp, TimeSpan window);
}
