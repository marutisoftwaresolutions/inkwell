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
        //
        // The AEO signals below are counted in SQL for the same reason: the readiness score needs
        // heading, link and word counts from Html, and transferring every article body to count
        // three things would make the dashboard cost megabytes. Occurrences are counted with the
        // length-difference trick (remove the token, divide by its length), which is exact for a
        // fixed literal. `"</h2>"` is counted rather than `"<h2"` because the closing tag carries no
        // attributes and so has a constant length.
        var rows = await conn.QueryAsync<ContentHealthItem>(@"
            SELECT Id, Title, Slug, PublishedAt, UpdatedAt, LastVerifiedAt, NextReviewAt,
                   CASE WHEN LEN(ISNULL(KeyFactsJson, '')) > 10 THEN 1 ELSE 0 END AS HasKeyFacts,
                   CASE WHEN LEN(ISNULL(FaqJson, ''))      > 10 THEN 1 ELSE 0 END AS HasFaq,
                   LEN(ISNULL(MetaDescription, ''))                                AS MetaLength,
                   ISNULL(MetaDescription, '')                                     AS MetaDescription,
                   ISNULL(AnswerCapsule, '')                                       AS AnswerCapsule,
                   ISNULL(KeyFactsJson, '')                                        AS KeyFactsJson,
                   ISNULL(FaqJson, '')                                             AS FaqJson,
                   CASE WHEN LEN(ISNULL(FeatureImage, '')) > 0 THEN 1 ELSE 0 END   AS HasFeatureImage,
                   (LEN(REPLACE(LOWER(ISNULL(Html, '')), '</h2>', '123456')) - LEN(ISNULL(Html, ''))) AS H2Count,
                   (LEN(REPLACE(LOWER(ISNULL(Html, '')), 'href=""/', '123456789')) - LEN(ISNULL(Html, ''))) / 2 AS InternalLinks,
                   LEN(ISNULL(Html, ''))                                           AS HtmlLength
              FROM Posts
             WHERE Status = 'Published'
               AND (@OwnerId IS NULL OR AuthorId = @OwnerId)
             ORDER BY ISNULL(NextReviewAt, '9999-12-31') ASC, PublishedAt DESC;",
            new { OwnerId = ownerId });

        return rows.ToList();
    }
}
