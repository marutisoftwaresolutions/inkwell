using Blog.Core.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// An unlisted post is published and opens at its own address, but appears in no list a reader or a
/// crawler can browse, and asks not to be indexed. A listed post published beside it shows everywhere,
/// which proves the lists were working.
/// </summary>
public class UnlistedPostTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public UnlistedPostTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData("unlisted", true)]
    [InlineData(" Unlisted ", true)]
    [InlineData("public", false)]
    [InlineData(null, false)]
    [InlineData("private", false)]   // not a state Inkwell has: treated as listed, never as hidden
    public void Only_the_word_unlisted_unlists(string? value, bool unlisted)
    {
        Assert.Equal(unlisted, PostVisibility.IsUnlisted(value));
        Assert.Equal(unlisted ? "unlisted" : "public", PostVisibility.Normalize(value));
    }

    [Fact]
    public async Task An_unlisted_post_opens_by_its_address_and_appears_in_no_list()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);
        var author = await conn.ExecuteScalarAsync<Guid?>("SELECT TOP 1 Id FROM Users WHERE Role = 'Admin' ORDER BY CreatedAt");
        if (author is null) return;

        var tag = Guid.NewGuid().ToString("N")[..8];
        var hidden = "zz-unlisted-" + tag;
        var shown = "zz-listed-" + tag;
        var tagId = Guid.NewGuid();
        var tagSlug = "zz-vis-" + tag;
        try
        {
            await conn.ExecuteAsync("INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (@tagId, @name, @tagSlug, @author)", new { tagId, name = "ZZ Vis " + tag, tagSlug, author });
            foreach (var (slug, visibility) in new[] { (hidden, "unlisted"), (shown, "public") })
            {
                var id = Guid.NewGuid();
                await conn.ExecuteAsync(@"INSERT INTO Posts (Id, Title, Slug, Html, Plaintext, Status, Visibility, Type, AuthorId, PublishedAt, CreatedAt, UpdatedAt, MetaDescription)
                                          VALUES (@id, @title, @slug, N'<p>Body zzvis</p>', N'Body zzvis', N'Published', @visibility, N'post', @author, DATEADD(MINUTE, -5, SYSUTCDATETIME()), SYSUTCDATETIME(), SYSUTCDATETIME(), N'Visibility check post, removed after the test.')",
                    new { id, title = "Title " + slug, slug, visibility, author });
                await conn.ExecuteAsync("INSERT INTO PostTags (PostId, TagId) VALUES (@id, @tagId)", new { id, tagId });
            }

            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // Both open at their own address; only the unlisted one asks not to be indexed.
            var hiddenPage = await client.GetStringAsync("/" + hidden);
            Assert.Contains("Title " + hidden, hiddenPage);
            Assert.Contains("<meta name=\"robots\" content=\"noindex,follow\"", hiddenPage);
            Assert.DoesNotContain("noindex", await client.GetStringAsync("/" + shown));

            // Every list: the listed post is there, the unlisted one is not.
            foreach (var list in new[] { "/", "/sitemap.xml", "/feed", "/tag/" + tagSlug, "/search?q=zzvis", "/api/v1/posts?pageSize=100" })
            {
                var body = await client.GetStringAsync(list);
                Assert.True(body.Contains(shown), $"{list} should list the public post (otherwise this check proves nothing).");
                Assert.False(body.Contains(hidden), $"{list} lists the unlisted post.");
            }
            // llms.txt and llms-full.txt are cached for a while, so a post made seconds ago may not be in them yet;
            // whatever they hold, it is never the unlisted post.
            foreach (var list in new[] { "/llms.txt", "/llms-full.txt" })
                Assert.False((await client.GetStringAsync(list)).Contains(hidden), $"{list} lists the unlisted post.");

            // The unlisted post is not offered as "related" on its neighbour.
            Assert.DoesNotContain(hidden, await client.GetStringAsync("/" + shown));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM PostTags WHERE TagId = @tagId; DELETE FROM Posts WHERE Slug IN @slugs; DELETE FROM Tags WHERE Id = @tagId;", new { tagId, slugs = new[] { hidden, shown } });
        }
    }

    [Fact]
    public void The_desk_lists_unlisted_posts_and_both_editors_offer_the_choice()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        string Read(params string[] p) => File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(p).ToArray()));
        Assert.Contains("IncludeUnlisted = true", Read("Blog.Web", "Controllers", "PostsController.cs"));
        Assert.Equal(2, Read("Blog.Web", "Controllers", "PostsController.cs").Split("PostVisibility.Normalize(post.Visibility)").Length - 1);
        foreach (var view in new[] { "Edit.cshtml", "Create.cshtml" })
        {
            var html = Read("Blog.Web", "Views", "Posts", view);
            Assert.Contains("name=\"Visibility\"", html);
            Assert.Contains("It is not private.", html);
        }
        Assert.Contains("Unlisted</span>", Read("Blog.Web", "Views", "Posts", "Index.cshtml"));
    }
}
