using System.Net;
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

        Assert.Contains("name=\"commentToken\"", page);
        Assert.Contains("name=\"contact_ref\"", page);

        var antiForgery = Regex.Match(page, "__RequestVerificationToken[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var formToken = Regex.Match(page, "name=\"commentToken\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(antiForgery), "anti-forgery token not found on the page");
        Assert.False(string.IsNullOrEmpty(formToken), "comment form token not found on the page");

        var before = (await comments.GetCommentsAsync(new CommentFilter { PostId = post.Id, PageSize = 1 })).TotalItems;

        // 1. Honeypot filled — the classic form-filler.
        var r1 = await Submit(client, post.Slug, antiForgery, formToken,
            content: "Great read, thanks for the detailed comparison of these systems.",
            honeypot: "https://example.test");
        Assert.Equal(HttpStatusCode.Redirect, r1.StatusCode);

        // 2. Raw HTML anchor for a backlink.
        var r2 = await Submit(client, post.Slug, antiForgery, formToken,
            content: "<a href=\"https://example.test/car-rental\">cheap car hire</a>");
        Assert.Equal(HttpStatusCode.Redirect, r2.StatusCode);

        // 3. Never fetched the form (no timing token at all).
        var r3 = await Submit(client, post.Slug, antiForgery, formToken: null,
            content: "Great read, thanks for the detailed comparison of these systems.");
        Assert.Equal(HttpStatusCode.Redirect, r3.StatusCode);

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
        return client.PostAsync($"/{slug}/comment", new FormUrlEncodedContent(fields));
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
