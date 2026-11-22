using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Trash for posts: deleting takes a post off the site but keeps it; the Desk list of everything
/// leaves Trash out and the Trash filter shows it; restoring makes a draft; only a post already in
/// Trash is deleted for good.
/// </summary>
public class PostTrashTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public PostTrashTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void Trashed_is_appended_so_stored_statuses_keep_their_meaning()
    {
        Assert.Equal(new[] { "Draft", "Published", "Scheduled", "InReview", "NeedsChanges", "Trashed" }, Enum.GetNames<PostStatus>());
    }

    [Fact]
    public async Task A_trashed_post_leaves_the_site_and_the_all_list_and_shows_only_under_trash()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var scope = _factory.Services.CreateScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostRepository>();
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);
        var author = await conn.ExecuteScalarAsync<Guid?>("SELECT TOP 1 Id FROM Users WHERE Role = 'Admin' ORDER BY CreatedAt");
        if (author is null) return;
        var slug = "zz-trash-" + Guid.NewGuid().ToString("N")[..8];
        var id = Guid.NewGuid();
        try
        {
            await conn.ExecuteAsync(@"INSERT INTO Posts (Id, Title, Slug, Html, Plaintext, Status, Visibility, Type, AuthorId, PublishedAt, CreatedAt, UpdatedAt)
                                      VALUES (@id, @title, @slug, N'<p>Body</p>', N'Body', N'Published', N'public', N'post', @author, DATEADD(MINUTE, -5, SYSUTCDATETIME()), SYSUTCDATETIME(), SYSUTCDATETIME())",
                new { id, title = "Title " + slug, slug, author });
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/" + slug)).StatusCode);

            await posts.SetWorkflowAsync(id, PostStatus.Trashed, null, null, null);

            // Readers: gone from its address and from the sitemap.
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/" + slug)).StatusCode);
            Assert.DoesNotContain(slug, await client.GetStringAsync("/sitemap.xml"));

            // The Desk: not in the list of everything, there under Trash, and still in the database.
            var all = await posts.GetPostsAsync(new PostFilter { Search = slug, Page = 1, PageSize = 50, IncludeUnlisted = true });
            Assert.DoesNotContain(all.Items, p => p.Id == id);
            var trash = await posts.GetPostsAsync(new PostFilter { Status = PostStatus.Trashed, Search = slug, Page = 1, PageSize = 50 });
            Assert.Contains(trash.Items, p => p.Id == id);

            // Restored: a draft, still not on the site.
            await posts.SetWorkflowAsync(id, PostStatus.Draft, null, null, null);
            Assert.Equal("Draft", await conn.ExecuteScalarAsync<string>("SELECT Status FROM Posts WHERE Id = @id", new { id }));
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/" + slug)).StatusCode);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM Posts WHERE Id = @id", new { id });
        }
    }

    [Fact]
    public void Delete_moves_to_trash_first_everywhere_and_restore_is_audited()
    {
        var desk = Read("Blog.Web", "Controllers", "PostsController.cs");
        Assert.Contains("if (existingPost.Status != PostStatus.Trashed)", desk);
        Assert.Contains("AuditActions.PostTrashed", desk);
        Assert.Contains("[HttpPost(\"restore/{id}\")]", desk);
        Assert.Contains("AuditActions.PostRestored", desk);
        Assert.Contains("if (post.Status != PostStatus.Trashed) { await MoveToTrashAsync(post); done++; break; }", desk);   // bulk delete
        var api = Read("Blog.Web", "Controllers", "Api", "PostsApiController.cs");
        Assert.Contains("if (existing.Status != PostStatus.Trashed)", api);
        Assert.Contains("Moved to Trash through the API", api);
    }

    [Fact]
    public void The_list_offers_the_trash_filter_restore_and_honest_confirmation_copy()
    {
        var view = Read("Blog.Web", "Views", "Posts", "Index.cshtml");
        Assert.Contains("(\"trash\", \"Trash\")", view);
        Assert.Contains("action=\"/admin/posts/restore/@post.Id\"", view);
        Assert.Contains("Move this post to Trash?", view);
        Assert.Contains("Delete this post for good?", view);
        Assert.Contains("This cannot be undone.", view);
    }
}
