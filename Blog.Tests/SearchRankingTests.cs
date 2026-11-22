using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Site search: every word typed has to be found, wildcard characters are matched as typed, and the
/// reader gets the best match first (the phrase in a title, then words in a title, then the text).
/// </summary>
public class SearchRankingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public SearchRankingTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public void Words_are_split_trimmed_deduplicated_and_capped()
    {
        Assert.Equal(new[] { "Lens", "coating" }, SearchTerms.Parse("  Lens,  coating  lens "));   // as typed; the database compares without case
        Assert.Equal(SearchTerms.MaxTerms, SearchTerms.Parse("aa bb cc dd ee ff gg hh").Count);
        Assert.Equal(new[] { "a" }, SearchTerms.Parse("a"));          // nothing long enough: the text itself
        Assert.Empty(SearchTerms.Parse("   "));
        Assert.Equal("lens coating", SearchTerms.Phrase("  lens   coating "));
    }

    [Theory]
    [InlineData("100%", @"%100\%%")]
    [InlineData("a_b", @"%a\_b%")]
    [InlineData("[x]", @"%\[x]%")]
    [InlineData(@"back\slash", @"%back\\slash%")]
    public void Wildcards_a_reader_types_are_matched_literally(string term, string pattern) =>
        Assert.Equal(pattern, SearchTerms.Like(term));

    [Fact]
    public async Task The_best_match_comes_first_and_every_word_must_be_found()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var scope = _factory.Services.CreateScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostRepository>();
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);
        var author = await conn.ExecuteScalarAsync<Guid?>("SELECT TOP 1 Id FROM Users WHERE Role = 'Admin' ORDER BY CreatedAt");
        if (author is null) return;

        var w = "zq" + Guid.NewGuid().ToString("N")[..6];   // two made-up words nothing else contains
        var a = w + "alpha"; var b = w + "beta";
        var ids = new List<Guid>();
        async Task<Guid> Add(string title, string body, int daysAgo)
        {
            var id = Guid.NewGuid(); ids.Add(id);
            await conn.ExecuteAsync(@"INSERT INTO Posts (Id, Title, Slug, Html, Plaintext, Status, Visibility, Type, AuthorId, PublishedAt, CreatedAt, UpdatedAt)
                                      VALUES (@id, @title, @slug, @body, @body, N'Published', N'public', N'post', @author, DATEADD(DAY, -@daysAgo, SYSUTCDATETIME()), SYSUTCDATETIME(), SYSUTCDATETIME())",
                new { id, title, slug = "zz-search-" + id.ToString("N")[..10], body, author, daysAgo });
            return id;
        }
        try
        {
            var bodyOnly = await Add("An unrelated heading", $"Text that mentions {a} and later {b}.", 1);      // newest, weakest
            var wordsInTitle = await Add($"{b} notes about {a}", "Nothing else.", 5);
            var phraseInTitle = await Add($"Guide to {a} {b}", "Nothing else.", 9);                             // oldest, strongest
            var oneWordOnly = await Add($"Only {a} here", "No second word.", 2);
            var percent = await Add("Discount day", $"Save 50% on {a} today.", 3);

            var ranked = await posts.GetPostsAsync(new PostFilter { Status = PostStatus.Published, Search = $"{a} {b}", RankBySearch = true, Page = 1, PageSize = 20 });
            Assert.Equal(new[] { phraseInTitle, wordsInTitle, bodyOnly }, ranked.Items.Select(p => p.Id).ToArray());
            Assert.Equal(3, ranked.TotalItems);                                                               // the one-word post is not a match

            var byDate = await posts.GetPostsAsync(new PostFilter { Status = PostStatus.Published, Search = $"{a} {b}", Page = 1, PageSize = 20 });
            Assert.Equal(bodyOnly, byDate.Items[0].Id);                                                         // without ranking: newest first, as the Desk lists

            // A percent sign is a character to find, not "anything".
            var literal = await posts.GetPostsAsync(new PostFilter { Status = PostStatus.Published, Search = $"50% {a}", Page = 1, PageSize = 20 });
            Assert.Equal(new[] { percent }, literal.Items.Select(p => p.Id).ToArray());
            Assert.DoesNotContain(oneWordOnly, literal.Items.Select(p => p.Id));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM Posts WHERE Id IN @ids", new { ids });
        }
    }

    [Fact]
    public void The_readers_search_is_ranked()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.Contains("RankBySearch = true", File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Controllers", "BlogController.cs")));
    }
}
