using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class SeriesRepository : ISeriesRepository
{
    private readonly DapperContext _ctx;
    public SeriesRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<List<Series>> GetAllAsync(Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        var sql = @"
            SELECT s.Id, s.Title, s.Slug, s.Description, s.AuthorId, s.CreatedAt, s.UpdatedAt,
                   COUNT(p.Id) as PostCount
            FROM Series s
            LEFT JOIN SeriesPosts sp ON sp.SeriesId = s.Id
            LEFT JOIN Posts p ON p.Id = sp.PostId AND p.Status = 'Published'
            " + (authorId == Guid.Empty ? "" : "WHERE s.AuthorId = @AuthorId ") + @"
            GROUP BY s.Id, s.Title, s.Slug, s.Description, s.AuthorId, s.CreatedAt, s.UpdatedAt
            ORDER BY s.Title";
        return (await conn.QueryAsync<Series>(sql, new { AuthorId = authorId })).ToList();
    }

    public async Task<Series?> GetByIdAsync(Guid id, Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Series>(
            "SELECT * FROM Series WHERE Id = @Id AND AuthorId = @AuthorId", new { Id = id, AuthorId = authorId });
    }

    public async Task<Series?> GetBySlugAsync(string slug, Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        var sql = "SELECT * FROM Series WHERE Slug = @Slug"
            + (authorId == Guid.Empty ? "" : " AND AuthorId = @AuthorId");
        return await conn.QueryFirstOrDefaultAsync<Series>(sql, new { Slug = slug, AuthorId = authorId });
    }

    public async Task<Guid> CreateAsync(Series series)
    {
        using var conn = _ctx.CreateConnection();
        if (series.Id == Guid.Empty) series.Id = Guid.NewGuid();
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO Series (Id, Title, Slug, Description, AuthorId)
            OUTPUT INSERTED.Id
            VALUES (@Id, @Title, @Slug, @Description, @AuthorId)",
            new { series.Id, series.Title, series.Slug, series.Description, series.AuthorId });
    }

    public async Task UpdateAsync(Series series)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE Series SET Title=@Title, Slug=@Slug, Description=@Description, UpdatedAt=GETUTCDATE()
            WHERE Id=@Id AND AuthorId=@AuthorId",
            new { series.Title, series.Slug, series.Description, series.Id, series.AuthorId });
    }

    public async Task DeleteAsync(Guid id, Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM Series WHERE Id = @Id AND AuthorId = @AuthorId", new { Id = id, AuthorId = authorId });
    }

    public async Task<bool> SlugExistsAsync(string slug, Guid authorId, Guid? excludeId = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = excludeId.HasValue
            ? "SELECT COUNT(1) FROM Series WHERE Slug = @Slug AND AuthorId = @AuthorId AND Id != @ExcludeId"
            : "SELECT COUNT(1) FROM Series WHERE Slug = @Slug AND AuthorId = @AuthorId";
        return await conn.ExecuteScalarAsync<int>(sql, new { Slug = slug, AuthorId = authorId, ExcludeId = excludeId }) > 0;
    }

    public async Task<List<Post>> GetPostsAsync(Guid seriesId, bool publishedOnly)
    {
        using var conn = _ctx.CreateConnection();
        var sql = @"
            SELECT p.*, u.DisplayName as AuthorName, COALESCE(u.ProfileImage, u.AvatarUrl) as AvatarUrl,
                   u.Credentials as AuthorCredentials, u.Specialty as AuthorSpecialty
            FROM Posts p
            INNER JOIN SeriesPosts sp ON sp.PostId = p.Id
            LEFT JOIN Users u ON u.Id = p.AuthorId
            WHERE sp.SeriesId = @SeriesId
            " + (publishedOnly ? "AND p.Status = 'Published' " : "") + @"
            ORDER BY sp.SortOrder, p.PublishedAt";
        return (await conn.QueryAsync<Post>(sql, new { SeriesId = seriesId })).ToList();
    }

    public async Task SetPostsAsync(Guid seriesId, IList<Guid> orderedPostIds)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM SeriesPosts WHERE SeriesId = @SeriesId", new { SeriesId = seriesId });
        for (var i = 0; i < orderedPostIds.Count; i++)
        {
            await conn.ExecuteAsync(
                "INSERT INTO SeriesPosts (SeriesId, PostId, SortOrder) VALUES (@SeriesId, @PostId, @SortOrder)",
                new { SeriesId = seriesId, PostId = orderedPostIds[i], SortOrder = i });
        }
    }

    public async Task<Series?> GetForPostAsync(Guid postId, Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        var sql = @"
            SELECT TOP 1 s.Id, s.Title, s.Slug, s.Description, s.AuthorId, s.CreatedAt, s.UpdatedAt
            FROM Series s
            INNER JOIN SeriesPosts sp ON sp.SeriesId = s.Id
            WHERE sp.PostId = @PostId
            " + (authorId == Guid.Empty ? "" : "AND s.AuthorId = @AuthorId ") + @"
            ORDER BY s.CreatedAt";
        return await conn.QueryFirstOrDefaultAsync<Series>(sql, new { PostId = postId, AuthorId = authorId });
    }
}
