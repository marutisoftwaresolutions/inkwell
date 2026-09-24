using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class CommentRepository : ICommentRepository
{
    private readonly DapperContext _ctx;
    public CommentRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<PagedResult<Comment>> GetCommentsAsync(CommentFilter filter)
    {
        using var conn = _ctx.CreateConnection();
        var (whereClause, p) = BuildWhere(filter, null);
        AddPaging(p, filter);

        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM Comments c WHERE {whereClause}", p);
        var items = (await conn.QueryAsync<Comment>(PageSql(whereClause), p)).ToList();

        return new PagedResult<Comment> { Items = items, TotalItems = total, Page = filter.Page, PageSize = filter.PageSize };
    }

    public async Task<List<Comment>> SearchAsync(CommentFilter filter, string q)
    {
        using var conn = _ctx.CreateConnection();
        var (whereClause, p) = BuildWhere(filter, q);
        AddPaging(p, filter);
        return (await conn.QueryAsync<Comment>(PageSql(whereClause), p)).ToList();
    }

    public async Task<int> CountSearchAsync(CommentFilter filter, string q)
    {
        using var conn = _ctx.CreateConnection();
        var (whereClause, p) = BuildWhere(filter, q);
        return await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM Comments c LEFT JOIN Posts p ON p.Id = c.PostId WHERE {whereClause}", p);
    }

    private static string PageSql(string whereClause) => $@"
            SELECT c.*, p.Title as PostTitle, p.Slug as PostSlug FROM Comments c
            LEFT JOIN Posts p ON p.Id = c.PostId
            WHERE {whereClause}
            ORDER BY c.CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

    private static void AddPaging(DynamicParameters p, CommentFilter filter)
    {
        p.Add("PageSize", filter.PageSize);
        p.Add("Offset", (Math.Max(1, filter.Page) - 1) * filter.PageSize);
    }

    /// <summary>
    /// The WHERE clause shared by the list, the search and its count, so the three can never disagree
    /// about which rows are in scope. Only references the comment alias <c>c</c> unless a query is
    /// given, in which case the caller must join Posts as <c>p</c> (the search does; the plain list's
    /// count does not need to).
    /// </summary>
    private static (string Where, DynamicParameters Params) BuildWhere(CommentFilter filter, string? q)
    {
        var where = new List<string> { "1=1" };
        var p = new DynamicParameters();

        if (filter.PostId.HasValue) { where.Add("c.PostId = @PostId"); p.Add("PostId", filter.PostId.Value); }
        if (filter.Status.HasValue) { where.Add("c.Status = @Status"); p.Add("Status", filter.Status.Value.ToString()); }
        if (filter.PostAuthorId.HasValue)
        {
            where.Add("EXISTS (SELECT 1 FROM Posts pa WHERE pa.Id = c.PostId AND pa.AuthorId = @PostAuthorId)");
            p.Add("PostAuthorId", filter.PostAuthorId.Value);
        }

        q = (q ?? string.Empty).Trim();
        if (q.Length > 0)
        {
            where.Add(@"(c.AuthorName LIKE @Like ESCAPE '\' OR c.AuthorEmail LIKE @Like ESCAPE '\'
                         OR c.Content LIKE @Like ESCAPE '\' OR p.Title LIKE @Like ESCAPE '\')");
            p.Add("Like", "%" + EscapeLike(q) + "%");
        }

        return (string.Join(" AND ", where), p);
    }

    // A search term is data, never a pattern: escape the LIKE metacharacters so "100%" finds "100%".
    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    public async Task<Comment?> GetByIdAsync(Guid id)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Comment>(
            "SELECT c.*, p.Title as PostTitle, p.Slug as PostSlug FROM Comments c LEFT JOIN Posts p ON p.Id = c.PostId WHERE c.Id = @Id",
            new { Id = id });
    }

    public async Task<Guid> CreateAsync(Comment comment)
    {
        using var conn = _ctx.CreateConnection();
        if (comment.Id == Guid.Empty) comment.Id = Guid.NewGuid();
        // MemberId is the signed-in user behind a Desk reply (null for a reader's comment).
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO Comments (Id, PostId, MemberId, AuthorName, AuthorEmail, AuthorUrl, AuthorIp, Content, Status, ParentId, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@Id, @PostId, @MemberId, @AuthorName, @AuthorEmail, @AuthorUrl, @AuthorIp, @Content, @Status, @ParentId, @CreatedAt)",
            new { comment.Id, comment.PostId, comment.MemberId, comment.AuthorName, comment.AuthorEmail, comment.AuthorUrl, comment.AuthorIp,
                  comment.Content, Status = comment.Status.ToString(), comment.ParentId,
                  CreatedAt = DateTime.UtcNow });
    }

    public async Task UpdateStatusAsync(Guid id, CommentStatus status)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("UPDATE Comments SET Status = @Status WHERE Id = @Id",
            new { Status = status.ToString(), Id = id });
    }

    public async Task DeleteAsync(Guid id)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM Comments WHERE Id = @Id", new { Id = id });
    }

    public async Task<int> GetPendingCountAsync(Guid? postAuthorId = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = "SELECT COUNT(*) FROM Comments c WHERE c.Status = 'Pending'";
        if (postAuthorId.HasValue)
        {
            sql += " AND EXISTS (SELECT 1 FROM Posts p WHERE p.Id = c.PostId AND p.AuthorId = @PostAuthorId)";
        }
        return await conn.ExecuteScalarAsync<int>(sql, new { PostAuthorId = postAuthorId });
    }

    public async Task<List<Comment>> GetApprovedForPostAsync(Guid postId)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Comment>(@"
            SELECT * FROM Comments WHERE PostId = @PostId AND Status = 'Approved'
            ORDER BY CreatedAt ASC", new { PostId = postId })).ToList();
    }

    // Both spam-filter lookups compare against the same clock CreateAsync stamps (DateTime.UtcNow), so the
    // window is measured consistently whatever the host's offset. The UTC convention item on the
    // roadmap moves all three together.

    public async Task<bool> HasRecentDuplicateAsync(Guid postOwnerId, string content, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        using var conn = _ctx.CreateConnection();
        var count = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM Comments c
            WHERE c.CreatedAt >= @Since AND c.Content = @Content
              AND EXISTS (SELECT 1 FROM Posts p WHERE p.Id = c.PostId AND p.AuthorId = @PostOwnerId)",
            new { Since = DateTime.UtcNow - window, Content = content.Trim(), PostOwnerId = postOwnerId });
        return count > 0;
    }

    public async Task<int> CountRecentFromAddressAsync(string authorIp, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(authorIp)) return 0;
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM Comments
            WHERE CreatedAt >= @Since AND AuthorIp = @AuthorIp",
            new { Since = DateTime.UtcNow - window, AuthorIp = authorIp });
    }
}
