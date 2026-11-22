using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services.Newsletter;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// "Prepare a newsletter when a post is published": off by default, a draft and never a send, once per
/// post, never for an unlisted post, and wired at the one place that decides a post went live.
/// </summary>
public class PublishNewsletterDraftTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public PublishNewsletterDraftTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void The_setting_is_off_until_someone_switches_it_on()
    {
        Assert.False(new UserSettings().NewsletterDraftOnPublish);
    }

    [Theory]
    [InlineData("Choosing a practice system", "Choosing a practice system")]
    [InlineData("  Two\r\n lines\tjoined  ", "Two lines joined")]
    [InlineData("", "New post")]
    [InlineData(null, "New post")]
    public void The_subject_is_the_title_on_one_line(string? title, string expected) =>
        Assert.Equal(expected, PublishNewsletterDrafter.SubjectFor(title));

    [Fact]
    public void A_long_title_is_cut_to_fit_the_newsletter_editor()
    {
        var subject = PublishNewsletterDrafter.SubjectFor(new string('x', 400));
        Assert.Equal(PublishNewsletterDrafter.MaxSubjectLength, subject.Length);
        Assert.EndsWith("…", subject);
        Assert.True(PublishNewsletterDrafter.MaxSubjectLength <= Blog.Web.Controllers.NewsletterAdminController.MaxSubjectLength);
    }

    [Fact]
    public void The_drafter_never_queues_or_sends_and_is_called_where_a_post_goes_live()
    {
        var drafter = Read("Blog.Web", "Services", "Newsletter", "PublishNewsletterDrafter.cs");
        Assert.DoesNotContain("QueueAsync", drafter);
        Assert.DoesNotContain("IEmailService", drafter);
        Assert.Contains("AuditActions.NewsletterDrafted", drafter);
        Assert.Contains("catch (Exception", drafter);                      // a publish never fails because of it

        var publisher = Read("Blog.Web", "Services", "Webhooks", "WebhookPublisher.cs");
        Assert.Contains("if (eventType == WebhookEvents.PostPublished) await _newsletterDraft.PostWentLiveAsync(after);", publisher);
        Assert.Equal(2, publisher.Split("_newsletterDraft.PostWentLiveAsync(").Length - 1);   // a save, and a scheduled post reaching its time
    }

    [Fact]
    public async Task A_draft_is_prepared_once_for_a_listed_post_and_only_when_the_setting_is_on()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var scope = _factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        if (admin is null) return;
        var drafter = scope.ServiceProvider.GetRequiredService<PublishNewsletterDrafter>();
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);

        var listed = new Post { Id = Guid.NewGuid(), Title = "zz draft on publish " + Guid.NewGuid().ToString("N")[..8], AuthorId = admin.Id, Status = PostStatus.Published, Visibility = "public" };
        var unlisted = new Post { Id = Guid.NewGuid(), Title = listed.Title + " unlisted", AuthorId = admin.Id, Status = PostStatus.Published, Visibility = "unlisted" };
        var missing = new Post { Id = Guid.NewGuid(), Title = listed.Title + " never saved", AuthorId = admin.Id, Status = PostStatus.Published, Visibility = "public" };
        // The once-per-post claim is a column on the post, so the posts are real rows.
        foreach (var p in new[] { listed, unlisted })
            await conn.ExecuteAsync(@"INSERT INTO Posts (Id, Title, Slug, Html, Plaintext, Status, Visibility, Type, AuthorId, PublishedAt, CreatedAt, UpdatedAt)
                                      VALUES (@Id, @Title, @Slug, N'<p>Body</p>', N'Body', N'Published', @Visibility, N'post', @AuthorId, DATEADD(MINUTE, -5, SYSUTCDATETIME()), SYSUTCDATETIME(), SYSUTCDATETIME())",
                new { p.Id, p.Title, Slug = "zz-dop-" + p.Id.ToString("N")[..12], p.Visibility, p.AuthorId });
        var before = await conn.ExecuteScalarAsync<string>("SELECT JSON_VALUE(JsonPayload, '$.NewsletterDraftOnPublish') FROM Settings WHERE UserId = @id", new { id = admin.Id });
        async Task Set(bool on)
        {
            await conn.ExecuteAsync("UPDATE Settings SET JsonPayload = JSON_MODIFY(JsonPayload, '$.NewsletterDraftOnPublish', CAST(@on AS BIT)) WHERE UserId = @id", new { on, id = admin.Id });
            scope.ServiceProvider.GetService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()?.Remove("settings:" + admin.Id);
        }
        Task<int> Issues(Guid postId) => conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM NewsletterIssues WHERE PostIds LIKE @p", new { p = "%" + postId + "%" });

        try
        {
            await Set(false);
            if ((await scope.ServiceProvider.GetRequiredService<ISettingRepository>().GetSettingsAsync(admin.Id)).NewsletterDraftOnPublish) return;   // settings cached elsewhere: nothing to prove here
            Assert.False(await drafter.PostWentLiveAsync(listed));
            Assert.Equal(0, await Issues(listed.Id));

            await Set(true);
            if (!(await scope.ServiceProvider.GetRequiredService<ISettingRepository>().GetSettingsAsync(admin.Id)).NewsletterDraftOnPublish) return;
            Assert.False(await drafter.PostWentLiveAsync(unlisted));
            Assert.True(await drafter.PostWentLiveAsync(listed));
            Assert.False(await drafter.PostWentLiveAsync(listed));          // once per post
            Assert.Equal(1, await Issues(listed.Id));
            Assert.False(await drafter.PostWentLiveAsync(missing));         // a post that was never saved has nothing to claim
            Assert.Equal(0, await Issues(unlisted.Id));

            var issue = await conn.QuerySingleAsync<NewsletterIssue>("SELECT * FROM NewsletterIssues WHERE PostIds LIKE @p", new { p = "%" + listed.Id + "%" });
            Assert.Equal(NewsletterStatus.Draft, issue.Status);
            Assert.Equal(listed.Title, issue.Subject);
            Assert.Null(issue.QueuedAt);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM NewsletterDeliveries WHERE IssueId = @id", new { id = issue.Id }));

            // The Admin deletes the prepared draft. The scheduled-post job revisits the post every minute for a day:
            // the draft must not come back (it did, before the claim was kept on the post).
            await conn.ExecuteAsync("DELETE FROM NewsletterIssues WHERE Id = @id", new { id = issue.Id });
            for (var visit = 0; visit < 3; visit++) Assert.False(await drafter.PostWentLiveAsync(listed));
            Assert.Equal(0, await Issues(listed.Id));
            Assert.NotNull(await conn.ExecuteScalarAsync<DateTime?>("SELECT NewsletterDraftedAt FROM Posts WHERE Id = @id", new { id = listed.Id }));
            Assert.Null(await conn.ExecuteScalarAsync<DateTime?>("SELECT NewsletterDraftedAt FROM Posts WHERE Id = @id", new { id = unlisted.Id }));   // never claimed: it was never eligible
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM NewsletterIssues WHERE PostIds LIKE @a OR PostIds LIKE @b", new { a = "%" + listed.Id + "%", b = "%" + unlisted.Id + "%" });
            await conn.ExecuteAsync("DELETE FROM Posts WHERE Id IN @ids", new { ids = new[] { listed.Id, unlisted.Id } });
            if (before is null) await conn.ExecuteAsync("UPDATE Settings SET JsonPayload = JSON_MODIFY(JsonPayload, '$.NewsletterDraftOnPublish', NULL) WHERE UserId = @id", new { id = admin.Id });
            else await Set(before == "true");
        }
    }
}
