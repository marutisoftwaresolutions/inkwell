using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// End-to-end proof that the spam gate is wired: a rendered post page carries the hidden fields, and
/// submissions that trip the filter are answered like a real one but never reach the database.
/// Runs against the dev database; skips when it is unreachable. Never stores a genuine comment.
/// </summary>
public class CommentSpamGateTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public CommentSpamGateTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    [Fact]
    public async Task Post_page_renders_the_hidden_fields_and_spam_is_discarded_before_storage()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostRepository>();
        var comments = scope.ServiceProvider.GetRequiredService<ICommentRepository>();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var ownerId = (await users.GetFirstAdminAsync())?.Id ?? Guid.Empty;
        if (!(await settings.GetSettingsAsync(ownerId)).CommentsEnabled)
        {
            _out.WriteLine("Comments are disabled for this tenant — skipping.");
            return;
        }

        var post = (await posts.GetPostsAsync(new PostFilter { Status = PostStatus.Published, Page = 1, PageSize = 1 })).Items.FirstOrDefault();
        if (post == null) { _out.WriteLine("No published post — skipping."); return; }

        // The default client keeps cookies, which the anti-forgery check needs.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var page = await client.GetStringAsync($"/{post.Slug}");

        // The page carries the fields with empty slots (ADR-0031); the tokens come from the token endpoint.
        Assert.Contains("name=\"commentToken\" value=\"\"", page);
        Assert.Contains("name=\"__RequestVerificationToken\" value=\"\"", page);
        Assert.Contains("name=\"contact_ref\"", page);
        Assert.Contains($"data-comment-token-url=\"/{post.Slug}/comment-token\"", page);

        using var tokens = JsonDocument.Parse(await client.GetStringAsync($"/{post.Slug}/comment-token"));
        var antiForgery = tokens.RootElement.GetProperty("request").GetString() ?? "";
        var formToken = tokens.RootElement.GetProperty("form").GetString() ?? "";
        Assert.False(string.IsNullOrEmpty(antiForgery), "anti-forgery token not found on the page");
        Assert.False(string.IsNullOrEmpty(formToken), "comment form token not found on the page");

        var before = (await comments.GetCommentsAsync(new CommentFilter { PostId = post.Id, PageSize = 1 })).TotalItems;

        // 1. Honeypot filled — the classic form-filler.
        var r1 = await Submit(client, post.Slug, antiForgery, formToken,
            content: "Great read, thanks for the detailed comparison of these systems.",
            honeypot: "https://example.test");
        await AssertAnsweredAsSent(r1);

        // 2. Raw HTML anchor for a backlink.
        var r2 = await Submit(client, post.Slug, antiForgery, formToken,
            content: "<a href=\"https://example.test/car-rental\">cheap car hire</a>");
        await AssertAnsweredAsSent(r2);

        // 3. Never fetched the form (no timing token at all).
        var r3 = await Submit(client, post.Slug, antiForgery, formToken: null,
            content: "Great read, thanks for the detailed comparison of these systems.");
        await AssertAnsweredAsSent(r3);

        var after = (await comments.GetCommentsAsync(new CommentFilter { PostId = post.Id, PageSize = 1 })).TotalItems;
        Assert.Equal(before, after);
    }

    private static Task<HttpResponseMessage> Submit(HttpClient client, string slug, string antiForgery,
        string? formToken, string content, string honeypot = "")
    {
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgery,
            ["authorName"] = "Gate Test",
            ["authorEmail"] = "gate-test@example.com",
            ["content"] = content,
            ["contact_ref"] = honeypot,
        };
        if (formToken != null) fields["commentToken"] = formToken;
        var request = new HttpRequestMessage(HttpMethod.Post, $"/{slug}/comment") { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("X-Requested-With", "fetch");
        return client.SendAsync(request);
    }

    /// <summary>
    /// Spam gets exactly the reply a moderated comment gets, so the sender learns nothing. When reCAPTCHA is configured (the
    /// dev site may have it) a submission without one is refused first, which also stores nothing.
    /// </summary>
    private async Task AssertAnsweredAsSent(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrEmpty(_factory.Services.GetRequiredService<IConfiguration>()["ReCaptcha:SiteKey"]))
        {
            Assert.Contains("reCAPTCHA", text);
            return;
        }
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        using var body = JsonDocument.Parse(text);
        Assert.True(body.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Your comment is awaiting moderation.", body.RootElement.GetProperty("message").GetString());
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
