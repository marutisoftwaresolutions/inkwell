using Blog.Core.Domain;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// "Hold comments for approval" used to change only the sentence a reader was shown: every comment was stored as
/// Pending whatever the setting said. These tests pin the rule that now decides it and that the controller uses it.
/// </summary>
public class CommentHoldingTests
{
    [Fact]
    public void A_site_that_holds_comments_stores_every_comment_as_pending()
    {
        Assert.Equal(CommentStatus.Pending, CommentHolding.InitialStatus(true, "A plain remark about the article.", false));
        Assert.Equal(CommentHolding.HeldMessage, CommentHolding.Message(CommentStatus.Pending));
    }

    [Fact]
    public void A_site_that_does_not_hold_comments_publishes_a_plain_comment_at_once()
    {
        Assert.Equal(CommentStatus.Approved, CommentHolding.InitialStatus(false, "A plain remark about the article.", false));
        Assert.Equal(CommentHolding.PostedMessage, CommentHolding.Message(CommentStatus.Approved));
    }

    [Theory]
    [InlineData("See https://example.com/page for more.")]
    [InlineData("More at www.example.com")]
    public void A_comment_with_a_link_waits_even_when_comments_are_not_held(string content)
    {
        Assert.Equal(CommentStatus.Pending, CommentHolding.InitialStatus(false, content, false));
    }

    [Fact]
    public void A_reply_to_a_comment_still_waiting_waits_too()
    {
        Assert.Equal(CommentStatus.Pending, CommentHolding.InitialStatus(false, "I agree with this.", parentHidden: true));
    }

    [Fact]
    public void Empty_text_is_not_a_reason_to_hold()
    {
        Assert.Equal(CommentStatus.Approved, CommentHolding.InitialStatus(false, null, false));
    }

    [Fact]
    public void A_settings_record_without_the_field_holds_comments()
    {
        Assert.True(new UserSettings().CommentsModeration);
        var old = System.Text.Json.JsonSerializer.Deserialize<UserSettings>("{\"SiteName\":\"Old site\",\"CommentsEnabled\":true}")!;
        Assert.True(old.CommentsModeration);
        var off = System.Text.Json.JsonSerializer.Deserialize<UserSettings>("{\"CommentsModeration\":false}")!;
        Assert.False(off.CommentsModeration);
    }

    [Fact]
    public void The_public_comment_action_takes_its_status_and_its_reply_from_the_rule()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Controllers", "BlogController.cs"));
        var start = source.IndexOf("public async Task<IActionResult> AddComment(", StringComparison.Ordinal);
        var end = source.IndexOf("[HttpGet(\"feed\")]", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        var action = source[start..end];

        Assert.Contains("CommentHolding.InitialStatus(moderation, content,", action);
        Assert.Contains("Status = initial,", action);
        Assert.DoesNotContain("Status = Blog.Core.Domain.CommentStatus.Pending", action);
        Assert.DoesNotContain("\"Comment posted.\"", action);
        // A reply is tied to a comment of the same post, and one that answers a hidden comment is held.
        Assert.Contains("parent?.PostId != post.Id", action);
        Assert.Contains("parentHidden: parent is not null && parent.Status != Blog.Core.Domain.CommentStatus.Approved", action);
    }

    [Fact]
    public void The_reader_script_reloads_only_when_the_comment_is_already_public()
    {
        var js = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "wwwroot", "js", "inkwell-comments.js"));
        Assert.Contains("if (r.data.posted) {", js);
        Assert.Contains("location.reload();", js);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Blog.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
