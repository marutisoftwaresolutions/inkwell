using System.Net;
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
/// A public post page carries validators, answers 304 to a matching If-None-Match, and a preview
/// link with a bad token is a plain 404. Runs against the dev database; skips when unreachable.
/// </summary>
public class ConditionalGetTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public ConditionalGetTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    [Fact]
    public async Task Post_page_revalidates_with_etag_and_answers_304_when_unchanged()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostRepository>();
        var post = (await posts.GetPostsAsync(new PostFilter { Status = PostStatus.Published, Page = 1, PageSize = 1 })).Items.FirstOrDefault();
        if (post == null) { _out.WriteLine("No published post — skipping."); return; }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var first = await client.GetAsync($"/{post.Slug}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var etag = first.Headers.ETag?.ToString();
        Assert.False(string.IsNullOrEmpty(etag), "no ETag on the post page");
        Assert.NotNull(first.Content.Headers.LastModified);
        Assert.Contains("no-cache", first.Headers.CacheControl?.ToString() ?? "");

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/{post.Slug}");
        req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await client.SendAsync(req);
        if (second.StatusCode == HttpStatusCode.OK)
        {
            // The ETag folds in ViewCount/100 and the comment count; a parallel test class hitting the
            // same shared dev post can move either between our two requests. Revalidate once more
            // against the fresh validator — a genuine regression fails on this second attempt too.
            var fresh = second.Headers.ETag?.ToString();
            Assert.False(string.IsNullOrEmpty(fresh), "no ETag on the re-rendered post page");
            using var again = new HttpRequestMessage(HttpMethod.Get, $"/{post.Slug}");
            again.Headers.TryAddWithoutValidation("If-None-Match", fresh);
            second = await client.SendAsync(again);
            etag = fresh;
        }
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal(etag, second.Headers.ETag?.ToString());

        using var stale = new HttpRequestMessage(HttpMethod.Get, $"/{post.Slug}");
        stale.Headers.TryAddWithoutValidation("If-None-Match", "W/\"definitely-not-it\"");
        var third = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
    }

    [Fact]
    public async Task A_bad_preview_token_is_a_plain_404()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await client.GetAsync("/preview/not-a-real-token");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
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
