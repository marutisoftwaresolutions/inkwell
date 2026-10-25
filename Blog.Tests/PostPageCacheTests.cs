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
/// Post pages can be cached at the edge (ADR-0031): an anonymous GET sets no cookie and is not marked no-store,
/// because the comment tokens are fetched on demand. A reader without JavaScript still comments: a post without
/// tokens lands on the respond page, which keeps the draft and carries fresh tokens. Skips without the dev database.
/// </summary>
public class PostPageCacheTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public PostPageCacheTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    private async Task<Post?> LivePostWithCommentsAsync()
    {
        try
        {
            var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            await conn.OpenAsync();
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return null; }
        using var scope = _factory.Services.CreateScope();
        var owner = (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync())?.Id ?? Guid.Empty;
        if (!(await scope.ServiceProvider.GetRequiredService<ISettingRepository>().GetSettingsAsync(owner)).CommentsEnabled)
        {
            _out.WriteLine("Comments are off on the dev site — skipping.");
            return null;
        }
        return (await scope.ServiceProvider.GetRequiredService<IPostRepository>()
            .GetPostsAsync(new PostFilter { Status = PostStatus.Published, Page = 1, PageSize = 1 })).Items.FirstOrDefault();
    }

    [Fact]
    public async Task A_post_page_sets_no_cookie_and_is_not_no_store()
    {
        var post = await LivePostWithCommentsAsync();
        if (post is null) return;
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        var res = await client.GetAsync("/" + post.Slug);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.False(res.Headers.Contains("Set-Cookie"), "the post page set a cookie: " + string.Join(" | ", res.Headers.TryGetValues("Set-Cookie", out var c) ? c : []));
        Assert.DoesNotContain("no-store", res.Headers.CacheControl?.ToString() ?? "");
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("name=\"__RequestVerificationToken\" value=\"\"", html);
        Assert.Contains("inkwell-comments.js", html);
        Assert.DoesNotMatch(new Regex("name=\"__RequestVerificationToken\" value=\"[^\"]+\""), html);
    }

    [Fact]
    public async Task The_token_endpoint_is_never_cached_or_indexed_and_unknown_posts_get_none()
    {
        var post = await LivePostWithCommentsAsync();
        if (post is null) return;
        var client = _factory.CreateClient();

        var res = await client.GetAsync($"/{post.Slug}/comment-token");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("no-store", res.Headers.CacheControl?.ToString() ?? "");
        Assert.Equal("noindex", string.Join("", res.Headers.GetValues("X-Robots-Tag")));
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("request").GetString()));
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("form").GetString()));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/no-such-post-zz9/comment-token")).StatusCode);
    }

    [Fact]
    public async Task Without_javascript_a_comment_lands_on_the_respond_page_with_the_draft_and_nothing_is_stored()
    {
        var post = await LivePostWithCommentsAsync();
        if (post is null) return;
        using var scope = _factory.Services.CreateScope();
        var comments = scope.ServiceProvider.GetRequiredService<ICommentRepository>();
        var before = (await comments.GetCommentsAsync(new CommentFilter { PostId = post.Id, PageSize = 1 })).TotalItems;
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // What a cached page without JavaScript sends: the fields, and empty token slots.
        var res = await client.PostAsync($"/{post.Slug}/comment", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = "", ["commentToken"] = "", ["contact_ref"] = "",
            ["authorName"] = "No Script", ["authorEmail"] = "noscript@example.com", ["content"] = "A comment typed — “without” JavaScript.",
        }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("no-store", res.Headers.CacheControl?.ToString() ?? "");
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("has not been sent yet", html);
        Assert.Contains("value=\"No Script\"", html);
        Assert.Contains("A comment typed — “without” JavaScript.</textarea>", WebUtility.HtmlDecode(html));
        Assert.Contains("noindex", html);
        Assert.Matches(new Regex("name=\"__RequestVerificationToken\" value=\"[^\"]+\""), html);   // ready to send on the next press
        Assert.Matches(new Regex("name=\"commentToken\" value=\"[^\"]+\""), html);
        Assert.Equal(before, (await comments.GetCommentsAsync(new CommentFilter { PostId = post.Id, PageSize = 1 })).TotalItems);

        var page = await client.GetAsync($"/{post.Slug}/respond?replyTo={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Leave a comment", await page.Content.ReadAsStringAsync());   // an unknown reply target is a plain comment
    }

    [Fact]
    public async Task A_missing_field_sends_the_reader_back_with_a_token_old_enough_to_press_post_at_once()
    {
        // The reader has written the comment and forgot their name; after fixing it they press Post within seconds. The
        // respond page's token is backdated so that press is never discarded as "too fast" (v1.0.9 review).
        var post = await LivePostWithCommentsAsync();
        if (post is null) return;
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var first = await client.GetStringAsync($"/{post.Slug}/respond");
        var anti = Regex.Match(first, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var timing = Regex.Match(first, "name=\"commentToken\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(anti); Assert.NotEmpty(timing);

        var res = await client.PostAsync($"/{post.Slug}/comment", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = anti, ["commentToken"] = timing, ["contact_ref"] = "",
            ["authorName"] = "", ["authorEmail"] = "forgot@example.com", ["content"] = "A comment with the name left out.",
        }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("id=\"respond-name\"", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.Contains("Enter your name.", html);
        var fresh = WebUtility.HtmlDecode(Regex.Match(html, "name=\"commentToken\" value=\"([^\"]+)\"").Groups[1].Value);
        var tokens = _factory.Services.GetRequiredService<Blog.Web.Services.CommentFormTokenService>();
        Assert.Equal(Blog.Core.Services.CommentFormTokenState.Valid, tokens.Validate(fresh, post.Id, out var age));
        Assert.True(age >= TimeSpan.FromSeconds(Blog.Core.Services.CommentSpamFilter.MinSecondsToWrite), $"token age {age}");
    }
}
