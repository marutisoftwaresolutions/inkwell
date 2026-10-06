using System.Collections.Concurrent;
using System.Net;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services.Jobs;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Newsletter sending (ADR-0028): the email renderer, the batched send job with a recording mail service, and
/// the unsubscribe flow. Database tests run on dev, create their own subscribers and remove them afterwards.
/// </summary>
public class NewsletterTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public NewsletterTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    // ── Renderer ────────────────────────────────────────────────────────────────

    private static NewsletterContent Content(params NewsletterPost[] posts) => new(
        "Optical <Software>", "https://www.example.com/", "October: “the best” EHRs & more", "A short <teaser>",
        "Hello **readers**. See [our guide](/guide) and <script>alert(1)</script>.", posts);

    [Fact]
    public void The_email_is_encoded_absolute_and_carries_a_per_recipient_unsubscribe_link()
    {
        var (html, text) = NewsletterEmail.Render(Content(
            new NewsletterPost("A <b>bold</b> title", "/best-ehr", "Short & sweet", "/uploads/features/x.jpg", "October 5, 2026")));

        Assert.Contains("October: “the best” EHRs &amp; more", html);
        Assert.Contains("Optical &lt;Software&gt;", html);
        Assert.Contains("A &lt;b&gt;bold&lt;/b&gt; title", html);
        Assert.DoesNotContain("<b>bold</b>", html);
        Assert.Contains("href=\"https://www.example.com/best-ehr\"", html);
        Assert.Contains("src=\"https://www.example.com/uploads/features/x.jpg\"", html);
        Assert.Contains("href=\"https://www.example.com/guide\"", html);             // intro links become absolute
        Assert.Contains("A short &lt;teaser&gt;", html);                               // preheader, hidden but present
        Assert.Contains(NewsletterEmail.UnsubscribePlaceholder, html);
        Assert.Contains("Read the article: https://www.example.com/best-ehr", text);
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("name=\"color-scheme\" content=\"light\"", html);
        Assert.Contains("alt=\"A &lt;b&gt;bold&lt;/b&gt; title\"", html);                // the image link has a name
        Assert.Contains("Read “A &lt;b&gt;bold&lt;/b&gt; title”", html);
        Assert.Contains("\n-- \n", text.Replace("\r\n", "\n"));                          // a signature separator mail apps recognise
        Assert.Contains("Unsubscribe: " + NewsletterEmail.UnsubscribePlaceholder, text);
        Assert.DoesNotContain("<strong>", text);                                       // plain text is plain
        Assert.DoesNotContain("<script>", html);                                       // raw HTML in the intro is never passed through
        Assert.Contains("&lt;script&gt;", html);

        var url = NewsletterEmail.UnsubscribeUrl("https://www.example.com/", "abc&123");
        Assert.Equal("https://www.example.com/newsletter/unsubscribe/abc%26123", url);
        var mine = NewsletterEmail.Personalize(html, url, html: true);
        Assert.Contains("href=\"https://www.example.com/newsletter/unsubscribe/abc%26123\"", mine);
        Assert.DoesNotContain(NewsletterEmail.UnsubscribePlaceholder, mine);
    }

    [Fact]
    public void An_intro_only_issue_renders_without_cards()
    {
        var (html, text) = NewsletterEmail.Render(Content());
        Assert.DoesNotContain("Read the article", html);
        Assert.Contains("<strong>readers</strong>", html);
        Assert.Contains("Hello readers.", text);
    }

    [Fact]
    public void Retries_back_off_and_stop()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), NewsletterSendJob.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(30), NewsletterSendJob.RetryDelay(2));
        Assert.Equal(3, NewsletterSendJob.MaxAttempts);
    }

    // ── Job (dev database, recording mail service) ─────────────────────────────

    private sealed class RecordingEmail : IEmailService
    {
        public ConcurrentBag<EmailMessage> Sent { get; } = new();
        public bool IsConfigured => true;
        public Task SendAsync(string toEmail, string toName, string subject, string htmlBody) => Task.CompletedTask;
        public Task<EmailSendResult> SendMessageAsync(EmailMessage m, CancellationToken ct = default)
        {
            if (m.ToEmail.StartsWith("nl-fail-", StringComparison.Ordinal)) return Task.FromResult(EmailSendResult.Failed("550 mailbox unavailable"));
            Sent.Add(m);
            return Task.FromResult(EmailSendResult.Ok);
        }
    }

    private bool TryConnection(out string cs)
    {
        cs = "";
        try
        {
            cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection") ?? "";
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            probe.Open();
            return true;
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return false; }
    }

    [Fact]
    public async Task A_queued_issue_goes_to_every_confirmed_subscriber_once_with_their_own_link()
    {
        if (!TryConnection(out var cs)) return;
        var mail = new RecordingEmail();
        using var app = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Newsletter:BatchSize"] = "500" }));
            b.ConfigureTestServices(s => s.AddScoped<IEmailService>(_ => mail));
        });

        var tag = Guid.NewGuid().ToString("N")[..8];
        var members = new[] { $"nl-ok1-{tag}@example.test", $"nl-ok2-{tag}@example.test", $"nl-left-{tag}@example.test", $"nl-fail-{tag}@example.test" };
        var issueId = Guid.NewGuid();
        await using var conn = new SqlConnection(cs);
        try
        {
            foreach (var e in members)
                await conn.ExecuteAsync(@"INSERT INTO Members (Id, Uuid, Email, Status, Subscribed, ConfirmToken, UnsubscribeToken, ConfirmedAt, CreatedAt)
                    VALUES (NEWID(), NEWID(), @e, 'confirmed', 1, REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), SYSUTCDATETIME(), SYSUTCDATETIME())", new { e });

            using var scope = app.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<INewsletterRepository>();
            await repo.CreateAsync(new NewsletterIssue { Id = issueId, OwnerId = Guid.Empty, Subject = "Test issue " + tag, IntroMarkdown = "Hi", CreatedBy = Guid.Empty });
            var (html, text) = NewsletterEmail.Render(new NewsletterContent("Site", "https://www.example.com", "Test issue " + tag, null, "Hi", Array.Empty<NewsletterPost>()));
            Assert.True(await repo.QueueAsync(issueId, Guid.Empty, html, text, "https://www.example.com", Guid.Empty, "test", DateTime.UtcNow));
            Assert.False(await repo.QueueAsync(issueId, Guid.Empty, html, text, "https://www.example.com", Guid.Empty, "test", DateTime.UtcNow));   // not twice

            // One subscriber leaves after the issue was queued but before it went out.
            var job = scope.ServiceProvider.GetServices<IScheduledJob>().OfType<NewsletterSendJob>().Single();
            await repo.ExpandRecipientsAsync(issueId, DateTime.UtcNow);
            await conn.ExecuteAsync("UPDATE Members SET Status = 'unsubscribed', Subscribed = 0 WHERE Email = @e", new { e = members[2] });

            _out.WriteLine(await job.RunAsync(CancellationToken.None));
            var mine = mail.Sent.Where(m => m.ToEmail.EndsWith(tag + "@example.test", StringComparison.Ordinal)).ToList();
            Assert.Equal(new[] { members[0], members[1] }.OrderBy(x => x), mine.Select(m => m.ToEmail).OrderBy(x => x));
            foreach (var m in mine)
            {
                var token = await conn.ExecuteScalarAsync<string>("SELECT UnsubscribeToken FROM Members WHERE Email = @e", new { e = m.ToEmail });
                Assert.Contains($"/newsletter/unsubscribe/{token}", m.Html);
                Assert.Contains($"/newsletter/unsubscribe/{token}", m.Text);
                Assert.Equal($"<https://www.example.com/newsletter/unsubscribe/{token}>", m.Headers!["List-Unsubscribe"]);
                Assert.Equal("List-Unsubscribe=One-Click", m.Headers["List-Unsubscribe-Post"]);
            }
            Assert.Equal("Skipped", await conn.ExecuteScalarAsync<string>("SELECT d.Status FROM NewsletterDeliveries d JOIN Members m ON m.Id = d.MemberId WHERE d.IssueId = @issueId AND m.Email = @e", new { issueId, e = members[2] }));

            // The failing address is retried, then given up on; only then is the issue finished and audited.
            for (var i = 0; i < 2; i++)
            {
                Assert.Equal("Sending", await conn.ExecuteScalarAsync<string>("SELECT Status FROM NewsletterIssues WHERE Id = @issueId", new { issueId }));
                await conn.ExecuteAsync("UPDATE NewsletterDeliveries SET NextAttemptAt = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE IssueId = @issueId AND Status = 'Pending'", new { issueId });
                await job.RunAsync(CancellationToken.None);
            }
            var failed = await conn.QuerySingleAsync<(string Status, int Attempts, string Error)>(
                "SELECT d.Status, d.Attempts, d.Error FROM NewsletterDeliveries d JOIN Members m ON m.Id = d.MemberId WHERE d.IssueId = @issueId AND m.Email = @e", new { issueId, e = members[3] });
            Assert.Equal(("Failed", 3), (failed.Status, failed.Attempts));
            Assert.Contains("550", failed.Error);
            var issue = await conn.QuerySingleAsync<(string Status, int SentCount, int FailedCount)>("SELECT Status, SentCount, FailedCount FROM NewsletterIssues WHERE Id = @issueId", new { issueId });
            Assert.Equal("Sent", issue.Status);
            Assert.True(issue.SentCount >= 2 && issue.FailedCount >= 1);
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Newsletter.Sent' AND EntityId = @id", new { id = issueId.ToString() }));

            // Running again sends nothing more.
            var before = mail.Sent.Count;
            await job.RunAsync(CancellationToken.None);
            Assert.Equal(before, mail.Sent.Count);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM NewsletterIssues WHERE Id = @issueId", new { issueId });
            await conn.ExecuteAsync("DELETE FROM Members WHERE Email LIKE @p", new { p = "nl-%-" + tag + "@example.test" });
        }
    }

    // ── Unsubscribe ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Opening_the_link_only_asks_and_one_click_unsubscribes()
    {
        if (!TryConnection(out var cs)) return;
        var email = $"nl-unsub-{Guid.NewGuid():N}@example.test";
        var token = Guid.NewGuid().ToString("N");
        await using var conn = new SqlConnection(cs);
        try
        {
            await conn.ExecuteAsync(@"INSERT INTO Members (Id, Uuid, Email, Status, Subscribed, ConfirmToken, UnsubscribeToken, ConfirmedAt, CreatedAt)
                VALUES (NEWID(), NEWID(), @email, 'confirmed', 1, REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), @token, SYSUTCDATETIME(), SYSUTCDATETIME())", new { email, token });
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            string Status() => conn.ExecuteScalar<string>("SELECT Status FROM Members WHERE Email = @email", new { email })!;

            var page = await client.GetAsync($"/newsletter/unsubscribe/{token}");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("Unsubscribe?", await page.Content.ReadAsStringAsync());
            Assert.Equal("confirmed", Status());                                    // a link scanner changes nothing

            var oneClick = await client.PostAsync($"/newsletter/unsubscribe/{token}",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["List-Unsubscribe"] = "One-Click" }));
            Assert.Equal(HttpStatusCode.OK, oneClick.StatusCode);
            Assert.Equal("unsubscribed", Status());
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/newsletter/unsubscribe/not-a-token",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["List-Unsubscribe"] = "One-Click" }))).StatusCode);
            Assert.Contains("already unsubscribed", await (await client.GetAsync($"/newsletter/unsubscribe/{token}")).Content.ReadAsStringAsync());
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM Members WHERE Email = @email", new { email });
        }
    }

    [Fact]
    public void The_desk_screen_is_admin_only_audited_and_absent_in_cloud_mode()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        var ctrl = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Controllers", "NewsletterAdminController.cs"));
        Assert.Contains("[Authorize(Policy = \"AdminOnly\")]", ctrl);
        Assert.Equal(ctrl.Split("[HttpPost(").Length - 1, ctrl.Split("[ValidateAntiForgeryToken]").Length - 1);
        foreach (var action in new[] { "NewsletterDrafted", "NewsletterUpdated", "NewsletterDeleted", "NewsletterQueued", "NewsletterCancelled", "NewsletterTestSent" })
            Assert.Contains("AuditActions." + action, ctrl);
        Assert.Equal(ctrl.Split("public async Task<IActionResult>").Length - 1, ctrl.Split("if (_tenant.IsCloudMode) return NotFound();").Length - 1);
        Assert.Contains("AuditActions.NewsletterSent", File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Services", "Jobs", "NewsletterSendJob.cs")));
        Assert.Contains("AuditActions.SubscriberDeleted", File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Controllers", "SubscribersController.cs")));
        Assert.Contains("HtmlEncode(name)", File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Controllers", "NewsletterController.cs")));

        // Sending uses the saved draft: both send forms stop for unsaved edits, before the shared confirmation.
        var actions = File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Views", "NewsletterAdmin", "_NewsletterActions.cshtml"));
        Assert.Equal(2, actions.Split("data-requires-saved>").Length - 1 + actions.Split("data-requires-saved\n").Length - 1 + actions.Split("data-requires-saved\r\n").Length - 1);
        Assert.Contains("window.addEventListener('submit'", actions);
        Assert.Contains("InkwellDesk.isDirty()", actions);
        // Progress is polled as a fragment that pauses while a dialog is open, not a whole-page reload.
        var detail = File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Views", "NewsletterAdmin", "Detail.cshtml"));
        Assert.DoesNotContain("location.reload", detail);
        Assert.Contains("#inkwell-dialog[open]", File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Views", "NewsletterAdmin", "_Progress.cshtml")));
    }
}
