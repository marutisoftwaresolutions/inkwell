using System.Net;
using System.Text.RegularExpressions;
using Blog.Core.Services;
using Blog.Web.Services;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Page views are counted from the page's own beacon, not from the server seeing a GET — so a
/// client that never runs JavaScript (the September 2026 homepage flood) is never counted.
/// </summary>
public class PageViewBeaconTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public PageViewBeaconTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    private static PageViewBeaconService NewService() =>
        new(new EphemeralDataProtectionProvider(), new MemoryCache(new MemoryCacheOptions()));

    // ── Which paths count ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("/", true)]
    [InlineData("/revolution-ehr-review", true)]
    [InlineData("/category/ehr", false)]
    [InlineData("/admin", false)]
    [InlineData("/feed", false)]
    [InlineData("/pv", false)]
    [InlineData("/logo.png", false)]
    [InlineData("/preview/abc", false)]
    [InlineData("", false)]
    [InlineData("no-leading-slash", false)]
    public void Only_home_posts_and_pages_count(string path, bool expected) =>
        Assert.Equal(expected, PageViewPolicy.IsTrackablePath(path));

    // ── The signed page token ─────────────────────────────────────────────────

    [Fact]
    public void A_token_is_valid_only_for_its_own_path_and_lifetime()
    {
        var svc = NewService();
        var issued = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        var token = svc.Issue("/some-post", issued);

        Assert.Equal("/some-post", svc.Validate(token, "/some-post", issued.AddDays(1)));
        Assert.Equal("/Some-Post", svc.Validate(token, "/Some-Post", issued.AddDays(1)));   // path case is not significant
        Assert.Null(svc.Validate(token, "/another-post", issued.AddDays(1)));                // replayed for another page
        Assert.Null(svc.Validate(token, "/some-post", issued + PageViewBeaconService.Lifetime + TimeSpan.FromMinutes(1)));
        Assert.Null(svc.Validate(token, "/some-post", issued.AddHours(-1)));                 // issued in the future
        Assert.Null(svc.Validate(token + "x", "/some-post", issued));                        // tampered
        Assert.Null(svc.Validate("", "/some-post", issued));
        Assert.Null(NewService().Validate(token, "/some-post", issued));                     // another key ring
    }

    [Fact]
    public void Each_address_is_capped_per_minute()
    {
        var svc = NewService();
        for (var i = 0; i < PageViewBeaconService.MaxPerAddressPerMinute; i++)
            Assert.True(svc.TryConsumeBudget("203.0.113.9"));

        Assert.False(svc.TryConsumeBudget("203.0.113.9"));
        Assert.True(svc.TryConsumeBudget("203.0.113.10"));   // another address is unaffected
    }

    // ── End to end against the dev database ───────────────────────────────────

    [Fact]
    public async Task A_page_load_without_the_beacon_is_not_counted_and_the_beacon_is()
    {
        if (!ProbeDb(out var cs)) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var marker = "https://referrer.example/" + Guid.NewGuid().ToString("N")[..12];
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var get = new HttpRequestMessage(HttpMethod.Get, "/");
        get.Headers.TryAddWithoutValidation("User-Agent", Browser);
        get.Headers.Referrer = new Uri(marker);
        var page = await client.SendAsync(get);
        var html = await page.Content.ReadAsStringAsync();
        if (page.StatusCode != HttpStatusCode.OK) { _out.WriteLine($"Home returned {(int)page.StatusCode} — skipping."); return; }

        var recorder = _factory.Services.GetRequiredService<TrafficRecorder>();
        await using var conn = new SqlConnection(cs);
        try
        {
            // The GET alone — what a flood tool does — records nothing.
            await recorder.FlushAsync();
            Assert.Equal(0, await CountAsync(conn, marker));

            var token = Regex.Match(html, "data-pv=\"([^\"]+)\"").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(token), "The home page should carry the view beacon.");
            Assert.Contains("inkwell-views.js", html);

            // A beacon for a different path than the token names is ignored.
            Assert.Equal(HttpStatusCode.NoContent, (await Beacon(client, token, "/another-page", marker)).StatusCode);
            await recorder.FlushAsync();
            Assert.Equal(0, await CountAsync(conn, marker));

            // The real beacon counts once.
            Assert.Equal(HttpStatusCode.NoContent, (await Beacon(client, WebUtility.HtmlDecode(token), "/", marker)).StatusCode);
            await recorder.FlushAsync();
            Assert.Equal(1, await CountAsync(conn, marker));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM PageViews WHERE Referrer = @marker", new { marker });
        }
    }

    [Fact]
    public async Task A_beacon_from_a_crawler_or_without_a_token_is_ignored()
    {
        if (!ProbeDb(out var cs)) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var marker = "https://referrer.example/" + Guid.NewGuid().ToString("N")[..12];
        var client = _factory.CreateClient();
        var token = _factory.Services.GetRequiredService<PageViewBeaconService>().Issue("/");

        await using var conn = new SqlConnection(cs);
        try
        {
            await Beacon(client, token, "/", marker, "Mozilla/5.0 (compatible; GPTBot/1.1; +https://openai.com/gptbot)");
            await Beacon(client, "", "/", marker);
            await _factory.Services.GetRequiredService<TrafficRecorder>().FlushAsync();
            Assert.Equal(0, await CountAsync(conn, marker));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM PageViews WHERE Referrer = @marker", new { marker });
        }
    }

    [Theory]
    [InlineData("/", null)]
    [InlineData("/My-Post", "my-post")]
    [InlineData("/category/news", null)]
    [InlineData("/a?b", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_single_segment_paths_name_a_post(string? path, string? slug) =>
        Assert.Equal(slug, PostViewTally.SlugOf(path));

    [Fact]
    public void Views_are_tallied_per_owner_and_post()
    {
        var tenant = Guid.NewGuid();
        var counts = PostViewTally.Count(new[]
        {
            (Guid.Empty, "/one"), (Guid.Empty, "/ONE"), (Guid.Empty, "/"), (Guid.Empty, "/two"),
            (tenant, "/one"), (Guid.Empty, "/tag/x"),
        });
        Assert.Equal(3, counts.Count);
        Assert.Contains(new PostViewCount(Guid.Empty, "one", 2), counts);
        Assert.Contains(new PostViewCount(Guid.Empty, "two", 1), counts);
        Assert.Contains(new PostViewCount(tenant, "one", 1), counts);
        Assert.Empty(PostViewTally.Count(Array.Empty<(Guid, string)>()));
    }

    [Fact]
    public async Task Most_read_counts_the_beacon_not_the_page_load()
    {
        if (!ProbeDb(out var cs)) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        await using var conn = new SqlConnection(cs);
        var slug = await conn.ExecuteScalarAsync<string?>("SELECT TOP 1 Slug FROM Posts WHERE Status = 'Published' ORDER BY NEWID()");
        if (slug is null) { _out.WriteLine("No published post — skipping."); return; }
        Task<int> ViewsAsync() => conn.ExecuteScalarAsync<int>("SELECT ViewCount FROM Posts WHERE Slug = @slug", new { slug });

        var marker = "https://referrer.example/" + Guid.NewGuid().ToString("N")[..12];
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var recorder = _factory.Services.GetRequiredService<TrafficRecorder>();
        var before = await ViewsAsync();
        var added = 0;
        try
        {
            // A reader's page load, a crawler and a revalidation all render the page; none of them counts.
            using var get = new HttpRequestMessage(HttpMethod.Get, "/" + slug);
            get.Headers.TryAddWithoutValidation("User-Agent", Browser);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(get)).StatusCode);
            await recorder.FlushAsync();
            Assert.Equal(before, await ViewsAsync());

            // The beacon from that page counts once, in the same flush that records the page view.
            var token = _factory.Services.GetRequiredService<PageViewBeaconService>().Issue("/" + slug);
            await Beacon(client, token, "/" + slug, marker);
            await recorder.FlushAsync();
            added = await ViewsAsync() - before;
            Assert.Equal(1, added);
            Assert.Equal(1, await CountAsync(conn, marker));
        }
        finally
        {
            await conn.ExecuteAsync("UPDATE Posts SET ViewCount = ViewCount - @added WHERE Slug = @slug", new { added, slug });
            await conn.ExecuteAsync("DELETE FROM PageViews WHERE Referrer = @marker", new { marker });
        }
    }

    private static Task<HttpResponseMessage> Beacon(HttpClient client, string token, string path, string referrer, string ua = Browser)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/pv")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["t"] = token, ["p"] = path, ["q"] = "", ["r"] = referrer
            })
        };
        req.Headers.TryAddWithoutValidation("User-Agent", ua);
        return client.SendAsync(req);
    }

    private static Task<int> CountAsync(SqlConnection conn, string marker) =>
        conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM PageViews WHERE Referrer = @marker", new { marker });

    private bool ProbeDb(out string connectionString)
    {
        connectionString = "";
        try
        {
            var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs)) return false;
            using var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            conn.Open();
            connectionString = cs;
            return true;
        }
        catch (Exception ex) { _out.WriteLine($"DB probe failed: {ex.Message}"); return false; }
    }
}
