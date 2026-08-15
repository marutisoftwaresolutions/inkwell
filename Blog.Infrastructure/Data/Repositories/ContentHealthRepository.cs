using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class ContentHealthRepository : IContentHealthRepository
{
    private readonly DapperContext _ctx;

    public ContentHealthRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<ContentHealthItem>> GetPublishedAsync(Guid? ownerId = null)
    {
        using var conn = _ctx.CreateConnection();

        // Only the columns the dashboard needs — the post bodies are large and never used here.
        var rows = await conn.QueryAsync<ContentHealthItem>(@"
            SELECT Id, Title, Slug, PublishedAt, UpdatedAt, LastVerifiedAt, NextReviewAt,
                   CASE WHEN LEN(ISNULL(KeyFactsJson, '')) > 10 THEN 1 ELSE 0 END AS HasKeyFacts,
                   CASE WHEN LEN(ISNULL(FaqJson, ''))      > 10 THEN 1 ELSE 0 END AS HasFaq,
                   LEN(ISNULL(MetaDescription, ''))                                AS MetaLength
              FROM Posts
             WHERE Status = 'Published'
               AND (@OwnerId IS NULL OR AuthorId = @OwnerId)
             ORDER BY ISNULL(NextReviewAt, '9999-12-31') ASC, PublishedAt DESC;",
            new { OwnerId = ownerId });

        return rows.ToList();
    }
}
