using System.Net;
using Blog.Core.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Listing pages used to answer any query string and any page number with 200, an unbounded URL
/// space crawlers kept fetching. These pin the two rules that close it — unknown parameters 301 to
/// the clean URL, pages past the end 404 — and, just as important, that nothing the site's own links
/// or campaign links produce is ever redirected.
/// </summary>
public class ListingQueryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public ListingQueryTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    private static (bool Changed, string Kept) Run(params (string Key, string? Value)[] pairs)
    {
        var (changed, kept) = ListingQuery.Canonicalize(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));
        return (changed, string.Join("&", kept.Select(k => $"{k.Key}={k.Value}")));
    }

    // ── Pure rules ────────────────────────────────────────────────────────────

    [Fact]
    public void Parameters_the_site_itself_emits_are_kept_untouched()
    {
        // Pager links carry search/category/tags on every listing, and page=1 appears in "first" links.
        Assert.Equal((false, "page=2&search=lens&category=ehr,pos&tags=cloud"),
            Run(("page", "2"), ("search", "lens"), ("category", "ehr,pos"), ("tags", "cloud")));
        Assert.Equal((false, "q=oct&page=1"), Run(("q", "oct"), ("page", "1")));
    }

    [Fact]
    public void Campaign_and_click_ids_survive_so_attribution_is_not_lost()
    {
        Assert.Equal((false, "utm_source=news&utm_medium=email&utm_campaign=sep&gclid=abc&fbclid=def&msclkid=ghi"),
            Run(("utm_source", "news"), ("utm_medium", "email"), ("utm_campaign", "sep"),
                ("gclid", "abc"), ("fbclid", "def"), ("msclkid", "ghi")));
    }

    [Theory]
    [InlineData("p", "123")]            // WordPress-style id
    [InlineData("page_id", "7")]
    [InlineData("random", "abc")]
    [InlineData("s", "lens")]            // WordPress search, not ours
    public void Unknown_parameters_are_dropped(string key, string value)
    {
        Assert.Equal((true, "page=2"), Run((key, value), ("page", "2")));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("2.5")]
    [InlineData(" 2")]
    [InlineData("99999999999999")]       // overflows int
    public void An_invalid_page_is_dropped(string value) => Assert.Equal((true, ""), Run(("page", value)));

    [Fact]
    public void Empty_repeated_and_miscased_keys_are_normalised()
    {
        Assert.Equal((true, ""), Run(("search", "")));
        Assert.Equal((true, "page=2"), Run(("page", "2"), ("page", "3")));
        Assert.Equal((true, "page=2"), Run(("Page", "2")));
        Assert.Equal((false, ""), Run());
    }

    [Theory]
    [InlineData(1, 0, 10, false)]        // an empty first page is a real page with an empty state
    [InlineData(1, 5, 10, false)]
    [InlineData(2, 10, 10, true)]        // exactly one full page
    [InlineData(2, 11, 10, false)]
    [InlineData(3, 11, 10, true)]
    [InlineData(99999, 58, 10, true)]
    [InlineData(2, 0, 10, true)]
    [InlineData(0, 0, 10, false)]
    public void Past_the_last_page_is_detected(int page, int total, int size, bool expected) =>
        Assert.Equal(expected, ListingQuery.IsPastLastPage(page, total, size));

    // ── Real routes (dev database) ────────────────────────────────────────────

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/?p=123", "/")]
    [InlineData("/?random=abc", "/")]
    [InlineData("/?page=0", "/")]
    [InlineData("/?page=abc", "/")]
    [InlineData("/?Page=2&x=1", "/?page=2")]
    [InlineData("/?utm_source=news&junk=1", "/?utm_source=news")]
    [InlineData("/search?q=lens&s=lens", "/search?q=lens")]
    public async Task Unknown_or_invalid_parameters_301_to_the_clean_url(string requested, string expected)
    {
        var response = await Client().GetAsync(requested);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/?utm_source=news&utm_medium=email")]
    [InlineData("/?gclid=abc123")]
    public async Task Clean_and_campaign_urls_are_never_redirected(string requested)
    {
        if (!ProbeDb(out _)) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var response = await Client().GetAsync(requested);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Pages_past_the_end_are_404_on_every_listing()
    {
        if (!ProbeDb(out var cs)) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        await using var conn = new SqlConnection(cs);
        var category = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT TOP 1 c.Slug FROM Categories c WHERE EXISTS (SELECT 1 FROM PostCategories pc WHERE pc.CategoryId = c.Id)");
        var tag = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT TOP 1 t.Slug FROM Tags t WHERE EXISTS (SELECT 1 FROM PostTags pt WHERE pt.TagId = t.Id)");

        var client = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/?page=99999")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/?page=1")).StatusCode);
        if (category is not null)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/category/{category}?page=99999")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/category/{category}")).StatusCode);
        }
        if (tag is not null)
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/tag/{tag}?page=99999")).StatusCode);
    }

    [Fact]
    public async Task Search_pager_links_keep_the_query()
    {
        if (!ProbeDb(out _)) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        // Every layout's pager emits "?page=N&search=…"; on /search that must still be a search.
        var viaQ      = await (await Client().GetAsync("/search?q=optometry")).Content.ReadAsStringAsync();
        var viaPager  = await (await Client().GetAsync("/search?page=1&search=optometry")).Content.ReadAsStringAsync();

        Assert.Contains("id=\"search-results-heading\"", viaQ);
        Assert.Contains("id=\"search-results-heading\"", viaPager);
        Assert.Contains("for “optometry”", viaPager);
        Assert.Equal(Heading(viaQ), Heading(viaPager));   // same result count either way

        static string Heading(string html) =>
            System.Text.RegularExpressions.Regex.Match(html, "<h1 id=\"search-results-heading\">([^<]*)</h1>").Groups[1].Value;
    }

    [Theory]
    [InlineData("//evil.example.com?x=1")]
    [InlineData("/\\evil.example.com?x=1")]
    public async Task A_crafted_path_is_never_turned_into_an_offsite_redirect(string requested)
    {
        var response = await Client().GetAsync(requested);
        var location = response.Headers.Location?.OriginalString ?? "";
        Assert.DoesNotContain("evil.example.com", location);
    }

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
