using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services.Jobs;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The welcome sequence: emails go out in order, once each, on their day, only to people who confirmed
/// after it was switched on; a hard bounce leaves the list; and the real job does all of that against
/// the database.
/// </summary>
public class WelcomeSequenceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private readonly WebApplicationFactory<Program> _factory;
    public WelcomeSequenceTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static WelcomeEmail Step(int days, bool on = true, string subject = "Hello", string body = "Body") =>
        new() { Enabled = on, DelayDays = days, Subject = subject, BodyMarkdown = body };

    [Fact]
    public void There_are_always_three_slots_and_values_are_clamped()
    {
        var steps = WelcomeSequence.Normalize([Step(-5), Step(500), Step(3), Step(4)]);
        Assert.Equal(3, steps.Count);
        Assert.Equal(0, steps[0].DelayDays);
        Assert.Equal(WelcomeSequence.MaxDelayDays, steps[1].DelayDays);
        Assert.Equal(3, WelcomeSequence.Normalize(null).Count);
        Assert.False(WelcomeSequence.AnyEnabled(WelcomeSequence.Normalize(null)));
    }

    [Fact]
    public void An_enabled_email_needs_a_subject_a_message_and_a_later_day_than_the_one_before()
    {
        Assert.Empty(WelcomeSequence.Validate([Step(1), Step(3), Step(7)]));
        Assert.Empty(WelcomeSequence.Validate([Step(1, on: false, subject: "", body: ""), Step(3), Step(7)]));   // an off slot may be empty
        var errors = WelcomeSequence.Validate([Step(3, subject: ""), Step(3), Step(7, body: "")]);
        Assert.True(errors.ContainsKey("0:subject"));
        Assert.True(errors.ContainsKey("1:delay"));
        Assert.True(errors.ContainsKey("2:body"));
    }

    [Fact]
    public void Steps_go_in_order_and_a_switched_off_step_is_passed_over()
    {
        var steps = new List<WelcomeEmail> { Step(1), Step(3, on: false), Step(7) };
        Assert.Equal(1, WelcomeSequence.NextStep(steps, 0));
        Assert.Equal(3, WelcomeSequence.NextStep(steps, 1));
        Assert.Null(WelcomeSequence.NextStep(steps, 3));
    }

    [Fact]
    public void A_step_is_sent_on_its_day_not_before_not_a_week_late_and_never_to_someone_who_confirmed_before_the_sequence_began()
    {
        var step = Step(3);
        var started = Now.AddDays(-30);
        Assert.Equal(WelcomeSequence.Decision.NotYet, WelcomeSequence.Decide(step, Now.AddDays(-2), started, Now));
        Assert.Equal(WelcomeSequence.Decision.Send, WelcomeSequence.Decide(step, Now.AddDays(-3), started, Now));
        Assert.Equal(WelcomeSequence.Decision.Send, WelcomeSequence.Decide(step, Now.AddDays(-9), started, Now));
        Assert.Equal(WelcomeSequence.Decision.TooLate, WelcomeSequence.Decide(step, Now.AddDays(-11), started, Now));
        Assert.Equal(WelcomeSequence.Decision.NotEligible, WelcomeSequence.Decide(step, Now.AddDays(-40), started, Now));
        Assert.Equal(WelcomeSequence.Decision.NotEligible, WelcomeSequence.Decide(step, Now.AddDays(-3), null, Now));
    }

    private sealed class RecordingEmail : IEmailService
    {
        public readonly List<EmailMessage> Sent = new();
        public string? BounceAddress;
        public bool IsConfigured => true;
        public Task SendAsync(string toEmail, string toName, string subject, string htmlBody) => Task.CompletedTask;
        public Task<EmailSendResult> SendMessageAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (message.ToEmail == BounceAddress) return Task.FromResult(EmailSendResult.Bounced("550 no such user"));
            Sent.Add(message);
            return Task.FromResult(EmailSendResult.Ok);
        }
    }

    [Fact]
    public async Task The_job_sends_each_due_email_once_in_order_and_skips_everyone_it_should()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        var mail = new RecordingEmail();
        using var app = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IEmailService>(_ => mail)));
        using var scope = app.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        if (admin is null) return;
        var cs = app.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);
        var original = await conn.ExecuteScalarAsync<string?>("SELECT JsonPayload FROM Settings WHERE UserId = @id", new { id = admin.Id });
        if (original is null) return;

        var tag = Guid.NewGuid().ToString("N")[..8];
        string Address(string who) => $"zz-welcome-{who}-{tag}@example.invalid";
        var utcNow = DateTime.UtcNow;
        async Task Add(string who, double confirmedDaysAgo, string status = "confirmed", int stepsSent = 0) =>
            await conn.ExecuteAsync(@"INSERT INTO Members (Id, Uuid, Email, Name, Status, Subscribed, ConfirmToken, UnsubscribeToken, ConfirmedAt, CreatedAt, WelcomeStepsSent)
                                      VALUES (NEWID(), NEWID(), @email, @who, @status, 1, REPLACE(NEWID(),'-',''), REPLACE(NEWID(),'-',''), @at, @at, @stepsSent)",
                new { email = Address(who), who, status, at = utcNow.AddDays(-confirmedDaysAgo), stepsSent });

        try
        {
            // The sequence started 20 days ago: email 1 on day 1, email 2 switched off, email 3 on day 5.
            var settings = scope.ServiceProvider.GetRequiredService<ISettingRepository>();
            var site = await settings.GetSettingsAsync(admin.Id);
            site.WelcomeEmails = [Step(1, subject: "First " + tag, body: "Read [this](https://example.invalid/a)."), Step(3, on: false), Step(5, subject: "Third " + tag)];
            site.WelcomeSequenceStartedAt = utcNow.AddDays(-20);
            site.WelcomeSiteUrl = "https://example.invalid";
            await settings.SaveSettingsAsync(admin.Id, site);

            await Add("fresh", 0.5);                       // confirmed 12 hours ago: nothing yet
            await Add("day2", 2);                          // due email 1
            await Add("day6", 6, stepsSent: 1);            // had email 1; email 2 is off; due email 3
            await Add("old", 40);                          // confirmed before the sequence began: never
            await Add("stale", 15);                        // email 1 was due 14 days ago: passed over, not sent
            await Add("gone", 2, status: "unsubscribed");  // left: never
            await Add("bouncer", 2);                       // their server refuses them
            mail.BounceAddress = Address("bouncer");

            var job = scope.ServiceProvider.GetServices<IScheduledJob>().OfType<WelcomeSequenceJob>().Single();
            var summary = await job.RunAsync(CancellationToken.None);

            var ours = mail.Sent.Where(m => m.ToEmail.Contains(tag)).ToList();
            Assert.Equal(new[] { Address("day2"), Address("day6") }.OrderBy(x => x), ours.Select(m => m.ToEmail).OrderBy(x => x));
            Assert.Equal("First " + tag, ours.Single(m => m.ToEmail == Address("day2")).Subject);
            Assert.Equal("Third " + tag, ours.Single(m => m.ToEmail == Address("day6")).Subject);

            // Each copy carries the reader's own unsubscribe link, in the body and as one-click headers.
            var copy = ours.Single(m => m.ToEmail == Address("day2"));
            Assert.Contains("https://example.invalid/newsletter/unsubscribe/", copy.Html);
            Assert.Contains("https://example.invalid/a", copy.Html);
            Assert.Equal("List-Unsubscribe=One-Click", copy.Headers!["List-Unsubscribe-Post"]);
            Assert.DoesNotContain(NewsletterEmail.UnsubscribePlaceholder, copy.Html);

            async Task<(int Steps, string Status)> State(string who) =>
                await conn.QuerySingleAsync<(int, string)>("SELECT WelcomeStepsSent, Status FROM Members WHERE Email = @e", new { e = Address(who) });
            Assert.Equal((1, "confirmed"), await State("day2"));
            Assert.Equal((3, "confirmed"), await State("day6"));
            Assert.Equal((0, "confirmed"), await State("fresh"));
            Assert.Equal((0, "confirmed"), await State("old"));
            Assert.Equal(3, (await State("stale")).Steps);                // both emails long overdue: passed over, none sent
            Assert.Equal((0, "bounced"), await State("bouncer"));
            Assert.Contains("sent 2", summary);

            // A second run sends nothing more to anyone.
            var before = mail.Sent.Count(m => m.ToEmail.Contains(tag));
            await job.RunAsync(CancellationToken.None);
            Assert.Equal(before, mail.Sent.Count(m => m.ToEmail.Contains(tag)));
        }
        finally
        {
            await conn.ExecuteAsync("UPDATE Settings SET JsonPayload = @original WHERE UserId = @id", new { original, id = admin.Id });
            await conn.ExecuteAsync("DELETE FROM Members WHERE Email LIKE @like", new { like = $"zz-welcome-%-{tag}@example.invalid" });
        }
    }

    [Fact]
    public void Switching_the_sequence_on_counts_from_that_moment_and_the_save_is_audited()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        var controller = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Controllers", "WelcomeEmailsController.cs"));
        Assert.Contains("if (isOn && !wasOn) site.WelcomeSequenceStartedAt = DateTime.UtcNow;", controller);
        Assert.Contains("AuditActions.NewsletterWelcomeUpdated", controller);
        Assert.Contains("[Authorize(Policy = \"AdminOnly\")]", controller);
        Assert.Equal(2, controller.Split("[ValidateAntiForgeryToken]").Length - 1);
        var import = File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Controllers", "SubscribersController.cs"));
        Assert.Contains("SetWelcomeStepsSentAsync(imported.Id, Blog.Core.Services.WelcomeSequence.MaxSteps)", import);
    }
}
