using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class TagRepository : ITagRepository
{
    private readonly DapperContext _ctx;
    public TagRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<List<Tag>> GetAllAsync(Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        var sql = @"
            SELECT t.Id, t.Name, t.Slug, t.AuthorId, COUNT(pt.PostId) as PostCount
            FROM Tags t
            LEFT JOIN PostTags pt ON pt.TagId = t.Id
            " + (authorId == Guid.Empty ? "" : "WHERE t.AuthorId = @AuthorId ") + @"
            GROUP BY t.Id, t.Name, t.Slug, t.AuthorId
            ORDER BY t.Name";
        return (await conn.QueryAsync<Tag>(sql, new { AuthorId = authorId })).ToList();
    }

    /// <summary>
    /// Tags carrying at least one PUBLISHED post, with that published count. Backs the public
    /// filter chips and any other reader-facing tag list. <see cref="GetAllAsync"/> is the admin
    /// view: it keeps zero-post tags and counts drafts, so using it publicly links readers — and
    /// crawlers — to archives that render empty.
    /// </summary>
    public async Task<List<Tag>> GetPublicAsync(Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        var sql = @"
            SELECT t.Id, t.Name, t.Slug, t.AuthorId, COUNT(p.Id) as PostCount
            FROM Tags t
            INNER JOIN PostTags pt ON pt.TagId = t.Id
            INNER JOIN Posts p ON p.Id = pt.PostId AND p.Status = 'Published'
            " + (authorId == Guid.Empty ? "" : "WHERE t.AuthorId = @AuthorId ") + @"
            GROUP BY t.Id, t.Name, t.Slug, t.AuthorId
            ORDER BY t.Name";
        return (await conn.QueryAsync<Tag>(sql, new { AuthorId = authorId })).ToList();
    }

    public async Task<Tag?> GetByIdAsync(Guid id, Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Tag>(
            "SELECT * FROM Tags WHERE Id = @Id AND AuthorId = @AuthorId", new { Id = id, AuthorId = authorId });
    }

    public async Task<Tag?> GetBySlugAsync(string slug, Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Tag>(
            "SELECT * FROM Tags WHERE Slug = @Slug AND AuthorId = @AuthorId", new { Slug = slug, AuthorId = authorId });
    }

    public async Task<Tag> GetOrCreateAsync(string name, Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        var slug = SlugHelper.Generate(name);
        var existing = await conn.QueryFirstOrDefaultAsync<Tag>(
            "SELECT * FROM Tags WHERE Slug = @Slug AND AuthorId = @AuthorId", new { Slug = slug, AuthorId = authorId });
        if (existing != null) return existing;

        var id = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (@Id, @Name, @Slug, @AuthorId)",
            new { Id = id, Name = name, Slug = slug, AuthorId = authorId });
        return new Tag { Id = id, Name = name, Slug = slug, AuthorId = authorId };
    }

    public async Task<Guid> CreateAsync(Tag tag)
    {
        using var conn = _ctx.CreateConnection();
        if (tag.Id == Guid.Empty) tag.Id = Guid.NewGuid();
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO Tags (Id, Name, Slug, AuthorId)
            OUTPUT INSERTED.Id 
            VALUES (@Id, @Name, @Slug, @AuthorId)",
            new { tag.Id, tag.Name, tag.Slug, tag.AuthorId });
    }

    public async Task DeleteAsync(Guid id, Guid authorId)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM Tags WHERE Id = @Id AND AuthorId = @AuthorId", new { Id = id, AuthorId = authorId });
    }

    public async Task<List<Tag>> GetForPostAsync(Guid postId)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Tag>(@"
            SELECT t.* FROM Tags t
            INNER JOIN PostTags pt ON pt.TagId = t.Id
            WHERE pt.PostId = @PostId", new { PostId = postId })).ToList();
    }
}
