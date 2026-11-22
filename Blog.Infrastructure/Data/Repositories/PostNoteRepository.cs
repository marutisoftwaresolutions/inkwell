using Blog.Core.Domain;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class PostNoteRepository : IPostNoteRepository
{
    private readonly DapperContext _ctx;
    public PostNoteRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<List<PostNote>> ListAsync(Guid postId)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<PostNote>(@"
            SELECT Id, PostId, UserId, UserName, Body, CreatedAt, ResolvedAt, ResolvedByName
            FROM PostNotes WHERE PostId = @postId
            ORDER BY CASE WHEN ResolvedAt IS NULL THEN 0 ELSE 1 END, CreatedAt", new { postId })).ToList();
    }

    public async Task<Guid> AddAsync(PostNote note)
    {
        if (note.Id == Guid.Empty) note.Id = Guid.NewGuid();
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO PostNotes (Id, PostId, UserId, UserName, Body, CreatedAt)
            VALUES (@Id, @PostId, @UserId, @UserName, @Body, SYSUTCDATETIME())", note);
        return note.Id;
    }

    public async Task<bool> ResolveAsync(Guid id, Guid postId, string resolvedByName)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync(@"
            UPDATE PostNotes SET ResolvedAt = SYSUTCDATETIME(), ResolvedByName = @resolvedByName
            WHERE Id = @id AND PostId = @postId AND ResolvedAt IS NULL", new { id, postId, resolvedByName }) > 0;
    }
}
