using Blog.Core.Interfaces;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Looking after the sender's reputation: an address a mail server refuses for good stops receiving
/// newsletters at once, anything temporary is still retried, and someone who left can join again.
/// </summary>
public class EmailDeliverabilityTests
{
    [Theory]
    [InlineData(550, null, true)]                                  // mailbox unavailable
    [InlineData(551, null, true)]                                  // user not local
    [InlineData(553, null, true)]                                  // mailbox name not allowed
    [InlineData(554, "5.1.1 The email account does not exist", true)]
    [InlineData(554, "Message rejected as spam", false)]           // a policy block is not the reader's fault
    [InlineData(450, "Mailbox busy", false)]
    [InlineData(451, "Greylisted, try again later", false)]
    [InlineData(452, "Mailbox full", false)]
    [InlineData(421, "Service not available", false)]
    [InlineData(535, "Authentication failed", false)]              // our own credentials: never blame the recipient
    public void Only_a_refusal_of_the_recipient_itself_is_a_hard_bounce(int code, string? message, bool permanent) =>
        Assert.Equal(permanent, SmtpBounce.IsPermanent(code, message));

    [Fact]
    public void A_bounced_result_is_a_failure_marked_permanent_and_an_ordinary_failure_is_not()
    {
        Assert.True(EmailSendResult.Bounced("x").Permanent);
        Assert.False(EmailSendResult.Bounced("x").Sent);
        Assert.False(EmailSendResult.Failed("x").Permanent);
        Assert.False(EmailSendResult.Ok.Permanent);
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void The_send_job_removes_a_hard_bounce_without_retrying_and_before_the_retry_branch()
    {
        var job = Read("Blog.Web", "Services", "Jobs", "NewsletterSendJob.cs");
        var permanent = job.IndexOf("else if (result.Permanent)", StringComparison.Ordinal);
        var retry = job.IndexOf("else if (attempts >= MaxAttempts)", StringComparison.Ordinal);
        Assert.True(permanent > 0 && permanent < retry);
        Assert.Contains("MarkMemberBouncedAsync(d.MemberId)", job);
    }

    [Fact]
    public void Only_a_confirmed_member_is_marked_bounced()
    {
        var repo = Read("Blog.Infrastructure", "Data", "Repositories", "NewsletterRepository.cs");
        Assert.Contains("SET Status = 'bounced', Subscribed = 0 WHERE Id = @memberId AND Status = 'confirmed'", repo);
    }

    [Fact]
    public void Someone_who_left_or_bounced_can_subscribe_again_and_must_confirm_afresh()
    {
        var controller = Read("Blog.Web", "Controllers", "NewsletterController.cs");
        Assert.Contains("Status: \"unsubscribed\" or Blog.Core.Services.SmtpBounce.BouncedStatus", controller);
        Assert.Contains("_members.RestartAsync(existing.Id)", controller);
        var repo = Read("Blog.Infrastructure", "Data", "Repositories", "MemberRepository.cs");
        Assert.Contains("SET Status = 'pending', Subscribed = 0, ConfirmToken = @token, ConfirmedAt = NULL", repo);
    }

    [Fact]
    public void The_subscribers_screen_shows_bounced_addresses()
    {
        var view = Read("Blog.Web", "Views", "Subscribers", "Index.cshtml");
        Assert.Contains("status=bounced", view);
        Assert.Contains("\"bounced\"      => \"pill-bad\"", view);
    }
}
