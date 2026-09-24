using System.Net;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

public class TableOfContentsTests
{
    [Fact]
    public void Headings_get_stable_slug_ids_and_entries_in_document_order()
    {
        var html = "<h2>Pricing &amp; Plans</h2><p>x</p><h3>Free tier</h3><h2>Verdict: worth it?</h2><h4>ignored</h4>";
        var (rewritten, entries) = TableOfContents.Build(html);

        Assert.Equal(new[] { "pricing-plans", "free-tier", "verdict-worth-it" }, entries.Select(e => e.Id));
        Assert.Equal(new[] { 2, 3, 2 }, entries.Select(e => e.Level));
        Assert.Equal("Pricing & Plans", entries[0].Text);
        Assert.Contains("<h2 id=\"pricing-plans\">Pricing &amp; Plans</h2>", rewritten);
        Assert.Contains("<h3 id=\"free-tier\">Free tier</h3>", rewritten);
        Assert.Contains("<h4>ignored</h4>", rewritten);
    }

    [Fact]
    public void Existing_ids_are_kept_and_duplicates_are_made_unique()
    {
        var html = "<h2 id=\"custom\">Setup</h2><h2>Setup</h2><h2>Setup</h2>";
        var (rewritten, entries) = TableOfContents.Build(html);

        Assert.Equal(new[] { "custom", "setup", "setup-2" }, entries.Select(e => e.Id));
        Assert.Contains("<h2 id=\"custom\">Setup</h2>", rewritten);
        Assert.Contains("<h2 id=\"setup-2\">Setup</h2>", rewritten);
    }

    [Fact]
    public void Inline_markup_inside_headings_is_stripped_from_the_entry_text_but_kept_in_the_body()
    {
        var (rewritten, entries) = TableOfContents.Build("<h2 class=\"x\"><em>Compulink</em> review</h2>");
        Assert.Equal("Compulink review", entries[0].Text);
        Assert.Equal("compulink-review", entries[0].Id);
        Assert.Contains("<h2 id=\"compulink-review\" class=\"x\"><em>Compulink</em> review</h2>", rewritten);
    }

    [Fact]
    public void Worthwhile_needs_real_structure()
    {
        Assert.False(TableOfContents.Worthwhile(TableOfContents.Build("<h2>a</h2><h2>b</h2>").Entries));
        Assert.True(TableOfContents.Worthwhile(TableOfContents.Build("<h2>a</h2><h2>b</h2><h2>c</h2>").Entries));
        Assert.True(TableOfContents.Worthwhile(TableOfContents.Build("<h2>a</h2><h3>b</h3><h3>c</h3><h3>d</h3>").Entries));
        Assert.Empty(TableOfContents.Build(null).Entries);
        Assert.Equal("", TableOfContents.Build("").Html);
    }
}

public class ReaderPagesRenderTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public ReaderPagesRenderTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    [Fact]
    public async Task A_post_page_carries_the_reading_body_marker_and_progress_bar()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        using var scope = _factory.Services.CreateScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostRepository>();
        var post = (await posts.GetPostsAsync(new PostFilter { Status = PostStatus.Published, Page = 1, PageSize = 1 })).Items.FirstOrDefault();
        if (post == null) { _out.WriteLine("No published post — skipping."); return; }

        var html = await _factory.CreateClient().GetStringAsync($"/{post.Slug}");
        Assert.Contains("data-reading-body", html);
        Assert.Contains("ink-progress__bar", html);
        // A body with three or more section headings shows the table of contents.
        var headings = TableOfContents.Build(post.Html).Entries;
        if (TableOfContents.Worthwhile(headings)) Assert.Contains("ink-toc", html);
    }

    [Fact]
    public async Task The_404_page_offers_search_and_popular_posts()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/definitely-not-a-real-page-xyz");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("ink-404", html);
        Assert.Contains("role=\"search\"", html);
        Assert.Contains("definitely-not-a-real-page-xyz", html); // the path is echoed so the reader can spot a typo
        Assert.Contains("ink-404__popular", html);
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
