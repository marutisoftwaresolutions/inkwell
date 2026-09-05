using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class LinkAuditRepository : ILinkAuditRepository
{
    private readonly DapperContext _ctx;

    public LinkAuditRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<LinkAuditData> LoadAsync(Guid? ownerId = null)
    {
        using var conn = _ctx.CreateConnection();

        var posts = (await conn.QueryAsync<(string Slug, string Title, string Html)>(@"
            SELECT Slug, Title, ISNULL(Html, '') AS Html
              FROM Posts
             WHERE Status = 'Published' AND (@OwnerId IS NULL OR AuthorId = @OwnerId);",
            new { OwnerId = ownerId })).ToList();

        var pages = (await conn.QueryAsync<(string Slug, string Title, string Html)>(@"
            SELECT Slug, Title, ISNULL(Content, '') AS Html
              FROM Pages
             WHERE IsPublished = 1;")).ToList();

        var redirects = (await conn.QueryAsync<RedirectRule>(
            "SELECT [From], [To], StatusCode FROM Redirects;")).ToList();

        var documents = posts.Select(p => new LinkAuditDocument(p.Slug, p.Title, p.Html))
            .Concat(pages.Select(p => new LinkAuditDocument(p.Slug, p.Title, p.Html, IsPage: true)))
            .ToList();

        // A link may resolve to a published post or a published page.
        var slugs = new HashSet<string>(
            posts.Select(p => p.Slug).Concat(pages.Select(p => p.Slug)),
            StringComparer.OrdinalIgnoreCase);

        var rules = redirects
            .GroupBy(r => r.From, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return new LinkAuditData(documents, slugs, rules);
    }
}
