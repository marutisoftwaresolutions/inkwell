using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class PageRepository : IPageRepository
{
    private readonly DapperContext _ctx;
    public PageRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<List<Page>> GetAllAsync(Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Page>(@"
            SELECT p.*, u.DisplayName as AuthorName, m.Url as FeaturedImageUrl
            FROM Pages p
            LEFT JOIN Users u ON u.Id = p.AuthorId
            LEFT JOIN Media m ON m.Id = p.FeaturedImageId
            WHERE p.AuthorId = @AuthorId
            ORDER BY p.SortOrder, p.Title", new { AuthorId = authorId })).ToList();
    }

    public async Task<Page?> GetByIdAsync(Guid id, Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Page>(@"
            SELECT p.*, u.DisplayName as AuthorName, m.Url as FeaturedImageUrl
            FROM Pages p
            LEFT JOIN Users u ON u.Id = p.AuthorId
            LEFT JOIN Media m ON m.Id = p.FeaturedImageId
            WHERE p.Id = @Id AND p.AuthorId = @AuthorId", new { Id = id, AuthorId = authorId });
    }

    public async Task<Page?> GetBySlugAsync(string slug)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Page>(@"
            SELECT p.*, u.DisplayName as AuthorName, m.Url as FeaturedImageUrl
            FROM Pages p
            LEFT JOIN Users u ON u.Id = p.AuthorId
            LEFT JOIN Media m ON m.Id = p.FeaturedImageId
            WHERE p.Slug = @Slug AND p.IsPublished = 1", new { Slug = slug });
    }

    public async Task<Guid> CreateAsync(Page page)
    {
        using var conn = _ctx.CreateConnection();
        if (page.Id == Guid.Empty) page.Id = Guid.NewGuid();
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO Pages (Id, Title, Slug, Content, AuthorId, IsPublished, IsInNav, SortOrder, ParentId, FeaturedImageId, PublishedAt, CreatedAt, UpdatedAt,
                               MetaTitle, MetaDescription, CanonicalUrl,
                               OgImage, OgTitle, OgDescription,
                               TwitterImage, TwitterTitle, TwitterDescription)
            OUTPUT INSERTED.Id
            VALUES (@Id, @Title, @Slug, @Content, @AuthorId, @IsPublished, @IsInNav, @SortOrder, @ParentId, @FeaturedImageId, @PublishedAt, @CreatedAt, @UpdatedAt,
                    @MetaTitle, @MetaDescription, @CanonicalUrl,
                    @OgImage, @OgTitle, @OgDescription,
                    @TwitterImage, @TwitterTitle, @TwitterDescription)",
            new { 
                page.Id, page.Title, page.Slug, page.Content, page.AuthorId, page.IsPublished, page.IsInNav, page.SortOrder,
                page.ParentId, page.FeaturedImageId, page.PublishedAt,
                page.MetaTitle, page.MetaDescription, page.CanonicalUrl,
                page.OgImage, page.OgTitle, page.OgDescription,
                page.TwitterImage, page.TwitterTitle, page.TwitterDescription,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow 
            });
    }

    public async Task UpdateAsync(Page page)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE Pages SET Title=@Title, Slug=@Slug, Content=@Content, IsPublished=@IsPublished, IsInNav=@IsInNav,
            SortOrder=@SortOrder, ParentId=@ParentId, FeaturedImageId=@FeaturedImageId, PublishedAt=@PublishedAt,
            MetaTitle=@MetaTitle, MetaDescription=@MetaDescription, CanonicalUrl=@CanonicalUrl,
            OgImage=@OgImage, OgTitle=@OgTitle, OgDescription=@OgDescription,
            TwitterImage=@TwitterImage, TwitterTitle=@TwitterTitle, TwitterDescription=@TwitterDescription,
            UpdatedAt=@UpdatedAt WHERE Id=@Id AND AuthorId=@AuthorId",
            new { 
                page.Title, page.Slug, page.Content, page.IsPublished, page.IsInNav, page.SortOrder,
                page.ParentId, page.FeaturedImageId, page.PublishedAt,
                page.MetaTitle, page.MetaDescription, page.CanonicalUrl,
                page.OgImage, page.OgTitle, page.OgDescription,
                page.TwitterImage, page.TwitterTitle, page.TwitterDescription,
                UpdatedAt = DateTime.UtcNow, page.Id, page.AuthorId 
            });
    }

    public async Task DeleteAsync(Guid id, Guid? authorId = null)
    {
        using var conn = _ctx.CreateConnection();
        if (authorId.HasValue)
        {
            await conn.ExecuteAsync("DELETE FROM Pages WHERE Id = @Id AND AuthorId = @AuthorId", new { Id = id, AuthorId = authorId.Value });
        }
        else
        {
            await conn.ExecuteAsync("DELETE FROM Pages WHERE Id = @Id", new { Id = id });
        }
    }

    public async Task<bool> SlugExistsAsync(string slug, Guid authorId, Guid? excludeId = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = excludeId.HasValue
            ? "SELECT COUNT(1) FROM Pages WHERE Slug = @Slug AND AuthorId = @AuthorId AND Id != @ExcludeId"
            : "SELECT COUNT(1) FROM Pages WHERE Slug = @Slug AND AuthorId = @AuthorId";
        return await conn.ExecuteScalarAsync<int>(sql, new { Slug = slug, AuthorId = authorId, ExcludeId = excludeId }) > 0;
    }

    public async Task<List<Page>> GetNavPagesAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Page>(@"
            SELECT p.*
            FROM Pages p
            WHERE p.IsPublished = 1 AND p.IsInNav = 1
            ORDER BY p.SortOrder, p.Title")).ToList();
    }
}
