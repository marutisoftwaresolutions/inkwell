using Blog.Core.Domain;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Notes on a post: kept in the Desk for the people working on it, open until resolved, resolved once,
/// removed with the post, and never part of anything a reader is served.
/// </summary>
public class PostNoteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public PostNoteTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void A_note_is_trimmed_keeps_its_line_breaks_and_is_capped()
    {
        Assert.Equal("Check the price.\nAnd the date.", PostNote.Clean("  Check the price.\nAnd the date.\u0007  "));
        Assert.Equal(PostNote.MaxLength, PostNote.Clean(new string('x', 5000)).Length);
        Assert.Equal("", PostNote.Clean("   "));
        Assert.Equal("", PostNote.Clean(null));
    }

    [Fact]
    public async Task Notes_are_listed_open_first_resolved_once_shown_to_no_reader_and_go_with_the_post()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var scope = _factory.Services.CreateScope();
        var notes = scope.ServiceProvider.GetRequiredService<IPostNoteRepository>();
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);
        var author = await conn.ExecuteScalarAsync<Guid?>("SELECT TOP 1 Id FROM Users WHERE Role = 'Admin' ORDER BY CreatedAt");
        if (author is null) return;

        var postId = Guid.NewGuid();
        var slug = "zz-notes-" + postId.ToString("N")[..8];
        const string secret = "zznote-figure-to-check-before-publishing";
        try
        {
            await conn.ExecuteAsync(@"INSERT INTO Posts (Id, Title, Slug, Html, Plaintext, Status, Visibility, Type, AuthorId, PublishedAt, CreatedAt, UpdatedAt)
                                      VALUES (@postId, N'Notes check', @slug, N'<p>Body</p>', N'Body', N'Published', N'public', N'post', @author, DATEADD(MINUTE, -5, SYSUTCDATETIME()), SYSUTCDATETIME(), SYSUTCDATETIME())",
                new { postId, slug, author });

            var first = await notes.AddAsync(new PostNote { PostId = postId, UserId = author.Value, UserName = "Ann", Body = secret });
            await Task.Delay(20);
            var second = await notes.AddAsync(new PostNote { PostId = postId, UserId = author.Value, UserName = "Bob", Body = "Second note" });

            Assert.True(await notes.ResolveAsync(first, postId, "Bob"));
            Assert.False(await notes.ResolveAsync(first, postId, "Bob"));              // already resolved
            Assert.False(await notes.ResolveAsync(second, Guid.NewGuid(), "Bob"));     // not through another post

            var list = await notes.ListAsync(postId);
            Assert.Equal(new[] { second, first }, list.Select(n => n.Id).ToArray());    // open first
            Assert.True(list[0].IsOpen);
            Assert.Equal("Bob", list[1].ResolvedByName);

            // No reader-facing surface carries a note.
            var client = _factory.CreateClient();
            foreach (var path in new[] { "/" + slug, "/api/v1/posts/" + slug, "/feed" })
                Assert.DoesNotContain(secret, await client.GetStringAsync(path));

            await conn.ExecuteAsync("DELETE FROM Posts WHERE Id = @postId", new { postId });
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM PostNotes WHERE PostId = @postId", new { postId }));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM Posts WHERE Id = @postId", new { postId });
        }
    }

    [Fact]
    public void Only_someone_who_can_open_the_post_in_the_editor_reaches_its_notes_and_both_actions_are_audited()
    {
        var controller = Read("Blog.Web", "Controllers", "PostNotesController.cs");
        Assert.Contains("[Authorize(Policy = \"CanEditPosts\")]", controller);
        Assert.Equal(2, controller.Split("[ValidateAntiForgeryToken]").Length - 1);
        Assert.Equal(2, controller.Split("var post = await EditablePostAsync(postId);").Length - 1);
        Assert.Contains("post.AuthorId == Me || User.IsInRole(\"Admin\") || User.IsInRole(\"Editor\")", controller);
        Assert.Contains("AuditActions.PostNoteAdded", controller);
        Assert.Contains("AuditActions.PostNoteResolved", controller);
        // The note's text is not copied into the permanent audit trail.
        Assert.DoesNotContain("newValues: text", controller);
    }

    [Fact]
    public void The_panel_sits_outside_the_tracked_editor_form_and_says_readers_never_see_notes()
    {
        var edit = Read("Blog.Web", "Views", "Posts", "Edit.cshtml");
        Assert.True(edit.IndexOf("<partial name=\"_PostNotes\"", StringComparison.Ordinal) < edit.IndexOf("id=\"postForm\"", StringComparison.Ordinal));
        var panel = Read("Blog.Web", "Views", "Posts", "_PostNotes.cshtml");
        Assert.Contains("Readers never see them.", panel);
        Assert.Contains("No notes yet. Add the first one below.", panel);
    }
}
