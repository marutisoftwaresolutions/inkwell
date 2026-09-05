using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly DapperContext _ctx;
    public CategoryRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<List<Category>> GetAllAsync(Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        var sql = @"
            SELECT c.Id, c.Name, c.Slug, c.AuthorId, COUNT(pc.PostId) as PostCount
            FROM Categories c
            LEFT JOIN PostCategories pc ON pc.CategoryId = c.Id
            LEFT JOIN Posts p ON p.Id = pc.PostId AND p.Status = 'Published'
            " + (authorId == Guid.Empty ? "" : "WHERE c.AuthorId = @AuthorId ") + @"
            GROUP BY c.Id, c.Name, c.Slug, c.AuthorId
            ORDER BY c.Name";
        return (await conn.QueryAsync<Category>(sql, new { AuthorId = authorId })).ToList();
    }

    /// <summary>
    /// Categories carrying at least one PUBLISHED post, with that published count. Reader-facing
    /// counterpart to <see cref="GetAllAsync"/>, which keeps empty categories and counts drafts.
    /// </summary>
    public async Task<List<Category>> GetPublicAsync(Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        var sql = @"
            SELECT c.Id, c.Name, c.Slug, c.AuthorId, COUNT(p.Id) as PostCount
            FROM Categories c
            INNER JOIN PostCategories pc ON pc.CategoryId = c.Id
            INNER JOIN Posts p ON p.Id = pc.PostId AND p.Status = 'Published'
            " + (authorId == Guid.Empty ? "" : "WHERE c.AuthorId = @AuthorId ") + @"
            GROUP BY c.Id, c.Name, c.Slug, c.AuthorId
            ORDER BY c.Name";
        return (await conn.QueryAsync<Category>(sql, new { AuthorId = authorId })).ToList();
    }

    public async Task<Category?> GetByIdAsync(Guid id, Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Category>(
            "SELECT * FROM Categories WHERE Id = @Id AND AuthorId = @AuthorId", new { Id = id, AuthorId = authorId });
    }

    public async Task<Category?> GetBySlugAsync(string slug, Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Category>(
            "SELECT * FROM Categories WHERE Slug = @Slug AND AuthorId = @AuthorId", new { Slug = slug, AuthorId = authorId });
    }

    public async Task<Guid> CreateAsync(Category category)
    {
        using var conn = _ctx.CreateConnection();
        if (category.Id == Guid.Empty) category.Id = Guid.NewGuid();
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO Categories (Id, Name, Slug, AuthorId)
            OUTPUT INSERTED.Id
            VALUES (@Id, @Name, @Slug, @AuthorId)",
            new { category.Id, category.Name, category.Slug, category.AuthorId });
    }

    public async Task UpdateAsync(Category category)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE Categories SET Name=@Name, Slug=@Slug WHERE Id=@Id AND AuthorId=@AuthorId",
            new { category.Name, category.Slug, category.Id, category.AuthorId });
    }

    public async Task DeleteAsync(Guid id, Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM Categories WHERE Id = @Id AND AuthorId = @AuthorId", new { Id = id, AuthorId = authorId });
    }

    public async Task<bool> SlugExistsAsync(string slug, Guid authorId, Guid? excludeId = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = excludeId.HasValue
            ? "SELECT COUNT(1) FROM Categories WHERE Slug = @Slug AND AuthorId = @AuthorId AND Id != @ExcludeId"
            : "SELECT COUNT(1) FROM Categories WHERE Slug = @Slug AND AuthorId = @AuthorId";
        return await conn.ExecuteScalarAsync<int>(sql, new { Slug = slug, AuthorId = authorId, ExcludeId = excludeId }) > 0;
    }

    public async Task<List<Category>> GetForPostAsync(Guid postId)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Category>(@"
            SELECT c.* FROM Categories c
            INNER JOIN PostCategories pc ON pc.CategoryId = c.Id
            WHERE pc.PostId = @PostId", new { PostId = postId })).ToList();
    }
}
