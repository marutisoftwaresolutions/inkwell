using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// HTTP smoke test — boots the real application via WebApplicationFactory and asserts the public
/// SEO/AEO surface end-to-end (this is the layer that would have caught the /search 500 regression).
///
/// The data layer is SQL-Server-only (DapperContext → SqlConnection), so these tests need the app's
/// configured SQL Server. When it is unreachable (e.g. CI without a DB) each test SKIPS itself as a
/// no-op so the suite stays green; when a DB is present the assertions run for real.
/// </summary>
public class IntegrationSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public IntegrationSmokeTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
        if (!_dbAvailable) _out.WriteLine("[skip] SQL Server not reachable — integration assertions skipped.");
    }

    private bool ProbeDb()
    {
        try
        {
            var cfg = _factory.Services.GetRequiredService<IConfiguration>();
            var cs = cfg.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs)) return false;
            var b = new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 };
            using var conn = new SqlConnection(b.ConnectionString);
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> GetAsync(string path)
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync(path);
        Assert.True(resp.IsSuccessStatusCode, $"GET {path} → {(int)resp.StatusCode}");
        return await resp.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Home_EmitsCanonicalOpenGraphWebSiteSchemaAndLang()
    {
        if (!_dbAvailable) return;
        var html = await GetAsync("/");
        Assert.Contains("rel=\"canonical\"", html);
        Assert.Contains("property=\"og:title\"", html);
        Assert.Contains("\"@type\":\"WebSite\"", html);
        Assert.Contains("\"@type\":\"Organization\"", html);
        Assert.Matches("<html lang=\"[^\"]+\"", html);
    }

    [Fact]
    public async Task RobotsTxt_HasCorrectAiTokenAndSitemap()
    {
        if (!_dbAvailable) return;
        var robots = await GetAsync("/robots.txt");
        Assert.Contains("User-agent: Google-Extended", robots);
        Assert.DoesNotContain("Googlebot-Extended", robots);
        Assert.Contains("Sitemap:", robots);
    }

    [Fact]
    public async Task Sitemap_IsWellFormedUrlsetOrIndex()
    {
        if (!_dbAvailable) return;
        var xml = await GetAsync("/sitemap.xml");
        Assert.True(xml.Contains("<urlset") || xml.Contains("<sitemapindex"), "sitemap root element missing");
    }

    [Fact]
    public async Task LlmsTxt_IsGeneratedWithIdentity()
    {
        if (!_dbAvailable) return;
        var txt = await GetAsync("/llms.txt");
        Assert.Contains("## Identity", txt);
    }

    [Fact]
    public async Task Search_ReturnsResultsPageNotServerError()
    {
        if (!_dbAvailable) return;
        // Regression guard: /search once 500'd because the reused Index action resolved the wrong view.
        var html = await GetAsync("/search?q=test");
        Assert.Contains("name=\"robots\" content=\"noindex,follow\"", html);
    }

    [Fact]
    public async Task Post_HasBlogPostingSchemaAndExactlyOneCanonical()
    {
        if (!_dbAvailable) return;

        // Find a real published post URL from the sitemap (skip home/section pages).
        var sitemap = await GetAsync("/sitemap.xml");
        var baseUri = _factory.CreateClient().BaseAddress!;
        var postLoc = Regex.Matches(sitemap, "<loc>([^<]+)</loc>")
            .Select(m => m.Groups[1].Value)
            .FirstOrDefault(u => !u.Contains("/category/") && !u.Contains("/tag/") &&
                                 !u.Contains("/author/") && u.TrimEnd('/') != baseUri.ToString().TrimEnd('/'));
        if (postLoc is null) return; // no published posts to assert against

        var path = new Uri(postLoc).PathAndQuery;
        var html = await GetAsync(path);
        Assert.Contains("\"@type\":\"BlogPosting\"", html);
        Assert.Single(Regex.Matches(html, "rel=\"canonical\""));
    }
}
