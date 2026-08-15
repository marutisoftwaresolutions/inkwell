using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Home-feed filter URLs (<c>/?tags=x</c>, <c>/?category=y</c>) duplicate the canonical
/// <c>/tag/{slug}</c> and <c>/category/{slug}</c> archives. Search Console (2026-08-15) found ~45 of
/// them indexed, drawing ~1,400 impressions across URLs that should never have been separate pages.
///
/// Single-value filters are consolidated with a 301; multi-value combinations have no canonical
/// equivalent and must be left alone. The redirect target is built from user-supplied query text, so
/// the slug guard is a security control, not a nicety — these tests pin both behaviours.
/// </summary>
public class FilterUrlCanonicalizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public FilterUrlCanonicalizationTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    private HttpClient Client() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/?tags=cloud-based-ehr", "/tag/cloud-based-ehr")]
    [InlineData("/?category=practice-management-software", "/category/practice-management-software")]
    [InlineData("/?tags=Cloud-Based-EHR", "/tag/cloud-based-ehr")]   // case-normalised
    [InlineData("/?tags=optical-pos&page=2", "/tag/optical-pos?page=2")] // pagination preserved
    public async Task A_single_value_filter_301s_to_the_canonical_archive(string requested, string expected)
    {
        // The redirect is decided before any database work, so this holds even without a database.
        var response = await Client().GetAsync(requested);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/?tags=//evil.example.com")]
    [InlineData("/?tags=https://evil.example.com")]
    [InlineData("/?tags=../../admin/users")]
    [InlineData("/?category=%2f%2fevil.example.com")]
    [InlineData("/?tags=x%0d%0aSet-Cookie:+a=b")]
    public async Task A_crafted_filter_value_is_never_turned_into_a_redirect(string requested)
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var response = await Client().GetAsync(requested);

        // It renders the (noindex) filtered feed instead of redirecting anywhere.
        Assert.NotEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [Theory]
    [InlineData("/?tags=cloud-based-ehr,revolution-ehr")]                  // no canonical equivalent
    [InlineData("/?category=practice-management-software&tags=eyefinity")] // mixed filters
    [InlineData("/?search=compulink&tags=cloud-based-ehr")]                // search wins
    public async Task Multi_value_and_mixed_filters_are_left_on_the_home_feed(string requested)
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var response = await Client().GetAsync(requested);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("noindex", html);   // still kept out of the index
    }

    [Fact]
    public async Task The_unfiltered_home_page_is_untouched()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var response = await Client().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("noindex", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_sitemap_never_advertises_a_noindex_tag_archive()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var client = Client();
        var sitemap = await client.GetStringAsync("/sitemap.xml");

        var tagUrls = System.Text.RegularExpressions.Regex
            .Matches(sitemap, @"<loc>[^<]*/tag/([^<]+)</loc>")
            .Select(m => m.Groups[1].Value)
            .Take(10)   // spot-check; a full crawl would make this test slow and flaky
            .ToList();

        _out.WriteLine($"tag URLs in sitemap: {tagUrls.Count}");

        foreach (var slug in tagUrls)
        {
            var html = await client.GetStringAsync($"/tag/{slug}");
            Assert.DoesNotContain("noindex", html);
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
