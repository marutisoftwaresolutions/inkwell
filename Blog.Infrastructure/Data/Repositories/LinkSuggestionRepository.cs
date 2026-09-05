using Blog.Core.Interfaces;
using Blog.Core.Services;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class LinkSuggestionRepository : ILinkSuggestionRepository
{
    private readonly DapperContext _ctx;

    public LinkSuggestionRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<LinkCandidate>> GetCandidatesAsync(Guid? ownerId = null)
    {
        using var conn = _ctx.CreateConnection();

        var posts = (await conn.QueryAsync<(Guid Id, string Slug, string Title, string Html)>(@"
            SELECT Id, Slug, Title, ISNULL(Html, '') AS Html
              FROM Posts
             WHERE Status = 'Published' AND (@OwnerId IS NULL OR AuthorId = @OwnerId);",
            new { OwnerId = ownerId })).ToList();

        var tags = (await conn.QueryAsync<(Guid PostId, string Slug)>(@"
            SELECT pt.PostId, t.Slug
              FROM PostTags pt JOIN Tags t ON t.Id = pt.TagId;")).ToList();

        var categories = (await conn.QueryAsync<(Guid PostId, string Slug)>(@"
            SELECT pc.PostId, c.Slug
              FROM PostCategories pc JOIN Categories c ON c.Id = pc.CategoryId;")).ToList();

        var tagsByPost = tags.GroupBy(t => t.PostId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<string>)g.Select(x => x.Slug).ToList());
        var catsByPost = categories.GroupBy(c => c.PostId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<string>)g.Select(x => x.Slug).ToList());

        return posts.Select(p => new LinkCandidate(
            p.Id, p.Slug, p.Title, p.Html,
            catsByPost.TryGetValue(p.Id, out var c) ? c : [],
            tagsByPost.TryGetValue(p.Id, out var t) ? t : [])).ToList();
    }
}
