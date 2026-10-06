using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services.Jobs;
using Blog.Web.Services.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Webhooks: the SSRF guard, the signature, the retry schedule, which event a save produces, and —
/// against the dev database with the network replaced — queueing, de-duplication and delivery.
/// </summary>
public class WebhookTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public WebhookTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    // ── Target validation (SSRF) ──────────────────────────────────────────────

    [Theory]
    [InlineData("https://hooks.example.com/inkwell")]
    [InlineData("https://api.netlify.com/build_hooks/abc123")]
    [InlineData("http://8.8.8.8/hook")]
    [InlineData("https://example.com:8443/hook")]
    public void Public_https_addresses_are_accepted(string url) => Assert.Null(WebhookRules.ValidateUrl(url));

    [Theory]
    [InlineData("")]
    [InlineData("hooks.example.com/no-scheme")]
    [InlineData("ftp://example.com/hook")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@example.com/hook")]
    [InlineData("http://localhost/hook")]
    [InlineData("http://app.localhost/hook")]
    [InlineData("http://printer.local/hook")]
    [InlineData("http://intranet/hook")]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://10.0.0.5/hook")]
    [InlineData("http://192.168.1.10/hook")]
    [InlineData("http://172.16.0.1/hook")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]   // cloud metadata
    [InlineData("http://[::1]/hook")]
    [InlineData("http://0.0.0.0/hook")]
    public void Local_private_and_malformed_addresses_are_refused(string url) => Assert.NotNull(WebhookRules.ValidateUrl(url));

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.0.1", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]    // IPv4-mapped loopback
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("2606:4700:4700::1111", false)]
    public void Forbidden_addresses_are_recognised(string ip, bool forbidden) =>
        Assert.Equal(forbidden, WebhookRules.IsForbiddenAddress(IPAddress.Parse(ip)));

    [Fact]
    public void One_private_answer_refuses_the_whole_host()
    {
        Assert.True(WebhookRules.AreAllPublic([IPAddress.Parse("8.8.8.8"), IPAddress.Parse("1.1.1.1")]));
        Assert.False(WebhookRules.AreAllPublic([IPAddress.Parse("8.8.8.8"), IPAddress.Parse("127.0.0.1")]));   // DNS rebinding set-up
        Assert.False(WebhookRules.AreAllPublic([]));
    }

    // ── Signature, secret, retries ────────────────────────────────────────────

    [Fact]
    public void The_signature_is_hmac_sha256_of_timestamp_dot_body()
    {
        const string secret = "s3cret-s3cret-s3cret";
        const string body = """{"event":"post.published"}""";
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes("1700000000." + body))).ToLowerInvariant();

        Assert.Equal($"t=1700000000,v1={expected}", WebhookRules.Sign(secret, 1700000000, body));
        Assert.NotEqual(WebhookRules.Sign(secret, 1700000000, body), WebhookRules.Sign(secret, 1700000001, body));
    }

    [Fact]
    public void Generated_secrets_are_long_random_and_url_safe()
    {
        var a = WebhookRules.NewSecret();
        var b = WebhookRules.NewSecret();
        Assert.Equal(43, a.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Failures_are_retried_four_times_then_given_up()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), WebhookRules.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(5), WebhookRules.RetryDelay(2));
        Assert.Equal(TimeSpan.FromMinutes(30), WebhookRules.RetryDelay(3));
        Assert.Equal(TimeSpan.FromHours(2), WebhookRules.RetryDelay(4));
        Assert.Null(WebhookRules.RetryDelay(WebhookRules.MaxAttempts));
        Assert.True(WebhookRules.IsSuccess(204));
        Assert.False(WebhookRules.IsSuccess(301));   // redirects are not followed
        Assert.False(WebhookRules.IsSuccess(500));
    }

    // ── Which event a save produces ───────────────────────────────────────────

    [Theory]
    [InlineData(false, true, WebhookEvents.PostPublished)]
    [InlineData(true, true, WebhookEvents.PostUpdated)]
    [InlineData(true, false, WebhookEvents.PostUnpublished)]
    [InlineData(false, false, null)]
    public void A_post_save_maps_to_one_event(bool wasLive, bool isLive, string? expected) =>
        Assert.Equal(expected, WebhookRules.EventForPostSave(wasLive, isLive));

    [Fact]
    public void A_scheduled_post_is_live_only_once_its_time_has_passed()
    {
        var now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(WebhookRules.IsLive(PostStatus.Published, null, now));
        Assert.True(WebhookRules.IsLive(PostStatus.Scheduled, now.AddMinutes(-1), now));
        Assert.False(WebhookRules.IsLive(PostStatus.Scheduled, now.AddMinutes(1), now));
        Assert.False(WebhookRules.IsLive(PostStatus.Draft, null, now));
    }

    // ── Queueing and delivery (dev database, network replaced) ────────────────

    private sealed class FakeSender : IWebhookSender
    {
        public readonly List<(string Url, string Secret, string Event, Guid EventId, string Body)> Calls = [];
        public bool Succeed { get; set; } = true;

        public Task<WebhookSendResult> SendAsync(string url, string secret, string eventType, Guid eventId, string body, CancellationToken ct)
        {
            Calls.Add((url, secret, eventType, eventId, body));
            return Task.FromResult(Succeed
                ? new WebhookSendResult(true, 204, null, null)
                : new WebhookSendResult(false, 503, "busy", "The endpoint answered HTTP 503."));
        }
    }

    [Fact]
    public async Task A_scheduled_post_is_announced_once_signed_and_retried_on_failure()
    {
        if (!ProbeDb()) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var fake = new FakeSender();
        var app = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IWebhookSender>(fake)));
        using var scope = app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWebhookRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<WebhookPublisher>();
        var job = scope.ServiceProvider.GetRequiredService<WebhookDeliveryJob>();

        const string secret = "test-secret-0123456789";
        var webhook = new Webhook
        {
            Id = Guid.NewGuid(), OwnerId = Guid.Empty, Name = "test-" + Guid.NewGuid().ToString("N")[..6],
            Url = "https://hooks.example.com/inkwell", SecretProtected = publisher.ProtectSecret(secret),
            Events = WebhookEvents.PostPublished, IsEnabled = true, SiteBaseUrl = "https://blog.example.com",
            CreatedAt = DateTime.UtcNow.AddHours(-2), UpdatedAt = DateTime.UtcNow,
        };
        await repo.CreateAsync(webhook);

        try
        {
            var post = new Post
            {
                Id = Guid.NewGuid(), Title = "Scheduled post", Slug = "scheduled-post", Status = PostStatus.Scheduled,
                ScheduledAt = DateTime.UtcNow.AddMinutes(-1), AuthorId = Guid.Empty, UpdatedAt = DateTime.UtcNow,
            };

            Assert.Equal(1, await publisher.ScheduledPostReachedAsync(post));
            Assert.Equal(0, await publisher.ScheduledPostReachedAsync(post));   // the event key makes it once-only

            // First attempt fails: queued again one minute later.
            fake.Succeed = false;
            await job.DeliverDueAsync(CancellationToken.None);
            var afterFailure = await repo.SearchDeliveriesAsync(webhook.Id, Guid.Empty, null, 1, 10);
            var pending = Assert.Single(afterFailure);
            Assert.Equal(WebhookDeliveryStatus.Pending, pending.Status);
            Assert.Equal(1, pending.Attempts);
            Assert.Equal(503, pending.ResponseCode);
            Assert.True(pending.NextAttemptAt > DateTime.UtcNow.AddSeconds(30));

            // What was sent: the right endpoint, secret, event and a payload with an absolute URL.
            var call = Assert.Single(fake.Calls);
            Assert.Equal(webhook.Url, call.Url);
            Assert.Equal(secret, call.Secret);
            Assert.Equal(WebhookEvents.PostPublished, call.Event);
            using var body = JsonDocument.Parse(call.Body);
            Assert.Equal("post.published", body.RootElement.GetProperty("event").GetString());
            var sentPost = body.RootElement.GetProperty("data").GetProperty("post");
            Assert.Equal("scheduled-post", sentPost.GetProperty("slug").GetString());
            Assert.EndsWith("/scheduled-post", sentPost.GetProperty("url").GetString());

            // Not due yet: nothing more is sent until the retry time.
            await job.DeliverDueAsync(CancellationToken.None);
            Assert.Single(fake.Calls);
        }
        finally
        {
            await repo.DeleteAsync(webhook.Id, Guid.Empty);   // cascades to its deliveries
        }
    }

    [Fact]
    public async Task Publishing_a_post_queues_only_for_subscribed_enabled_webhooks()
    {
        if (!ProbeDb()) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWebhookRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<WebhookPublisher>();

        Webhook Make(string events, bool enabled) => new()
        {
            Id = Guid.NewGuid(), OwnerId = Guid.Empty, Name = "test-" + Guid.NewGuid().ToString("N")[..6],
            Url = "https://hooks.example.com/x", SecretProtected = publisher.ProtectSecret("test-secret-0123456789"),
            Events = events, IsEnabled = enabled, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var subscribed = Make(WebhookEvents.PostPublished, true);
        var otherEvent = Make(WebhookEvents.CommentCreated, true);
        var paused = Make(WebhookEvents.PostPublished, false);
        foreach (var w in new[] { subscribed, otherEvent, paused }) await repo.CreateAsync(w);

        try
        {
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("blog.example.com");
            var draft = new Post { Id = Guid.NewGuid(), Title = "T", Slug = "t", Status = PostStatus.Draft };
            var live = new Post { Id = draft.Id, Title = "T", Slug = "t", Status = PostStatus.Published, PublishedAt = DateTime.UtcNow };

            await publisher.PostSavedAsync(context, draft, draft);   // draft → draft: no event
            await publisher.PostSavedAsync(context, draft, live);    // draft → live: post.published

            Assert.Equal(1, await repo.CountDeliveriesAsync(subscribed.Id, Guid.Empty, null));
            Assert.Equal(0, await repo.CountDeliveriesAsync(otherEvent.Id, Guid.Empty, null));
            Assert.Equal(0, await repo.CountDeliveriesAsync(paused.Id, Guid.Empty, null));
        }
        finally
        {
            foreach (var w in new[] { subscribed, otherEvent, paused }) await repo.DeleteAsync(w.Id, Guid.Empty);
        }
    }

    private bool ProbeDb()
    {
        try
        {
            var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs)) return false;
            using var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            conn.Open();
            return true;
        }
        catch (Exception ex) { _out.WriteLine($"DB probe failed: {ex.Message}"); return false; }
    }
}
