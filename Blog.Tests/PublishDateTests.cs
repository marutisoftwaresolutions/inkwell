using Blog.Core.Domain;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// A scheduled post goes live by the clock with no further save. Until 2026-11-15 nothing gave it a publish date, so
/// once live it showed readers an empty date. These tests pin the rule and that every save path uses it.
/// </summary>
public class PublishDateTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_post_scheduled_for_later_is_dated_at_its_scheduled_time()
    {
        var at = Now.AddHours(3);
        Assert.Equal(at, EditorialWorkflow.PublishDate(PostStatus.Scheduled, at, null, Now));
        // Rescheduling moves the date with it.
        Assert.Equal(at, EditorialWorkflow.PublishDate(PostStatus.Scheduled, at, Now.AddHours(1), Now));
    }

    [Fact]
    public void A_scheduled_post_that_is_already_live_keeps_the_date_it_has()
    {
        var at = Now.AddHours(-2);
        Assert.Equal(at, EditorialWorkflow.PublishDate(PostStatus.Scheduled, at, null, Now));
        var chosen = Now.AddDays(-1);
        Assert.Equal(chosen, EditorialWorkflow.PublishDate(PostStatus.Scheduled, at, chosen, Now));
    }

    [Fact]
    public void A_published_post_keeps_its_date_or_gets_now()
    {
        Assert.Equal(Now, EditorialWorkflow.PublishDate(PostStatus.Published, null, null, Now));
        var earlier = Now.AddDays(-30);
        Assert.Equal(earlier, EditorialWorkflow.PublishDate(PostStatus.Published, null, earlier, Now));
    }

    [Theory]
    [InlineData(PostStatus.Draft)]
    [InlineData(PostStatus.InReview)]
    [InlineData(PostStatus.NeedsChanges)]
    public void A_post_that_is_not_going_live_gets_no_date(PostStatus status)
    {
        Assert.Null(EditorialWorkflow.PublishDate(status, Now.AddHours(3), null, Now));
        Assert.Null(EditorialWorkflow.PublishDate(PostStatus.Scheduled, null, null, Now));
    }

    [Fact]
    public void Every_save_path_and_the_upgrade_use_the_rule()
    {
        var root = RepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "Blog.Core", "Services", "PostService.cs"));
        Assert.Equal(2, service.Split("EditorialWorkflow.PublishDate(").Length - 1);
        var controller = File.ReadAllText(Path.Combine(root, "Blog.Web", "Controllers", "PostsController.cs"));
        Assert.Contains("EditorialWorkflow.PublishDate(status, post.ScheduledAt, post.PublishedAt, now)", controller);

        const string fix = "UPDATE Posts SET PublishedAt = ScheduledAt WHERE Status = 'Scheduled' AND PublishedAt IS NULL AND ScheduledAt IS NOT NULL";
        Assert.Contains(fix, File.ReadAllText(Path.Combine(root, "Blog.Infrastructure", "Data", "MigrationService.cs")));
        Assert.True(File.Exists(Path.Combine(root, "DBScripts", "2026-11-15_set-publish-date-on-scheduled-posts.sql")));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Blog.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
