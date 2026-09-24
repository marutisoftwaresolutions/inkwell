using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Blog.Infrastructure.Data.Repositories;

public class RevisionRepository : IRevisionRepository
{
    private readonly DapperContext _ctx;
    public RevisionRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<Revision>> ListAsync(string entityType, Guid entityId)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Revision>(@"
            SELECT Id, EntityType, EntityId, Number, Title, Slug, '' AS Html, MetaTitle, MetaDescription,
                   NULL AS StructuredJson, Status, Reason, AuthorId, AuthorName, CreatedAt,
                   LEN(Html) AS HtmlLength
            FROM Revisions
            WHERE EntityType = @Type AND EntityId = @Id
            ORDER BY Number DESC",
            new { Type = entityType, Id = entityId })).ToList();
    }

    public async Task<Revision?> GetLatestAsync(string entityType, Guid entityId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Revision>(@"
            SELECT TOP 1 *, LEN(Html) AS HtmlLength FROM Revisions
            WHERE EntityType = @Type AND EntityId = @Id
            ORDER BY Number DESC",
            new { Type = entityType, Id = entityId });
    }

    public async Task<Revision?> GetByIdAsync(Guid id)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Revision>(
            "SELECT *, LEN(Html) AS HtmlLength FROM Revisions WHERE Id = @Id", new { Id = id });
    }

    /// <summary>
    /// The number is computed inside the INSERT itself, reading MAX(Number) for the entity under
    /// UPDLOCK/HOLDLOCK so two concurrent saves of the same post serialise on that key range instead
    /// of both reading the same maximum. UX_Revisions_Entity_Number (EntityType, EntityId, Number) is
    /// the hard guarantee: if a collision still slips through, the unique-key error (2601/2627) is
    /// retried exactly once with a fresh MAX.
    /// </summary>
    private const string InsertSql = @"
        INSERT INTO Revisions (Id, EntityType, EntityId, Number, Title, Slug, Html, MetaTitle, MetaDescription,
                               StructuredJson, Status, Reason, AuthorId, AuthorName, CreatedAt)
        OUTPUT INSERTED.Number
        SELECT @Id, @EntityType, @EntityId, ISNULL(MAX(Number), 0) + 1, @Title, @Slug, @Html, @MetaTitle, @MetaDescription,
               @StructuredJson, @Status, @Reason, @AuthorId, @AuthorName, @CreatedAt
        FROM Revisions WITH (UPDLOCK, HOLDLOCK)
        WHERE EntityType = @EntityType AND EntityId = @EntityId;";

    public async Task<int> CreateAsync(Revision r)
    {
        using var conn = _ctx.CreateConnection();
        if (r.Id == Guid.Empty) r.Id = Guid.NewGuid();
        var args = new
        {
            r.Id, r.EntityType, r.EntityId,
            Title = Clip(r.Title, 500), Slug = Clip(r.Slug, 500), r.Html,
            MetaTitle = r.MetaTitle is null ? null : Clip(r.MetaTitle, 500),
            MetaDescription = r.MetaDescription is null ? null : Clip(r.MetaDescription, 1000),
            r.StructuredJson, Status = Clip(r.Status, 50), Reason = Clip(r.Reason, 100),
            r.AuthorId, AuthorName = r.AuthorName is null ? null : Clip(r.AuthorName, 200),
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            return await conn.ExecuteScalarAsync<int>(InsertSql, args);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Another save of the same entity won the race; the failed statement rolled back, so the
            // same Id is free to try again and MAX(Number) now includes the winner.
            return await conn.ExecuteScalarAsync<int>(InsertSql, args);
        }
    }

    public async Task<int> PruneAsync(string entityType, Guid entityId, int keep)
    {
        if (keep < 1) keep = 1;
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync(@"
            DELETE FROM Revisions
            WHERE EntityType = @Type AND EntityId = @Id
              AND Id NOT IN (SELECT TOP (@Keep) Id FROM Revisions
                             WHERE EntityType = @Type AND EntityId = @Id ORDER BY Number DESC)",
            new { Type = entityType, Id = entityId, Keep = keep });
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
