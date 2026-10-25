using Blog.Core.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using R = Blog.Core.Services.AccessibilityCheck.Rules;

namespace Blog.Tests;

/// <summary>Advisory accessibility checks for the editors and Content Health (v1.0.9, MARKET-RESEARCH G13).</summary>
public class AccessibilityCheckTests
{
    private readonly ITestOutputHelper _out;
    public AccessibilityCheckTests(ITestOutputHelper output) => _out = output;

    private static Dictionary<string, int> Rules(string html) => AccessibilityCheck.Check(html).ToDictionary(f => f.Rule, f => f.Count);

    [Fact]
    public void A_clean_article_has_no_findings()
    {
        var html = "<h2>Pricing</h2><p>Plans start at <a href=\"/pricing-guide\">our pricing guide</a>.</p>" +
                   "<h3>Basic</h3><p>Short.</p><img src=\"/uploads/a.webp\" alt=\"The dashboard\">" +
                   "<img src=\"/uploads/divider.webp\" alt=\"\"><table><thead><tr><th>Plan</th></tr></thead><tbody><tr><td>Basic</td></tr></tbody></table>" +
                   "<h2>Next</h2><a href=\"/x\" aria-label=\"Read the full review\">Read more</a>";
        Assert.Empty(AccessibilityCheck.Check(html));
        Assert.Empty(AccessibilityCheck.Check(null));
    }

    [Fact]
    public void Images_without_alt_and_with_file_name_alt_are_flagged_but_decorative_ones_are_not()
    {
        var r = Rules("<img src=a.jpg><img src='b.jpg' alt='IMG_2041.JPG'><img src=c.png alt=\"\"><img src=d.png aria-hidden=\"true\"><img src=e.png alt=\"Front desk\">");
        Assert.Equal(1, r[R.MissingAlt]);
        Assert.Equal(1, r[R.FileNameAlt]);
    }

    [Theory]
    [InlineData("<h2>a</h2><h4>b</h4>", 1)]       // skips h3
    [InlineData("<h1>again</h1><h2>a</h2>", 1)]    // a second h1 in the body
    [InlineData("<h3>first</h3>", 1)]             // starts below h2
    [InlineData("<h2>a</h2><h3>b</h3><h2>c</h2><h3>d</h3><h4>e</h4>", 0)]
    public void Heading_levels_go_down_one_step_at_a_time(string html, int expected) =>
        Assert.Equal(expected, Rules(html).GetValueOrDefault(R.HeadingOrder));

    [Fact]
    public void Vague_and_empty_links_are_flagged()
    {
        var r = Rules("<a href=/a>Click here</a> <a href=/b>read more…</a> <a href=/c></a> <a href=/d><img src=x.png alt=\"Home\"></a> <a href=/e>our EHR comparison</a>");
        Assert.Equal(3, r[R.VagueLink]);
    }

    [Fact]
    public void Tables_without_a_header_row_and_long_paragraphs_are_flagged()
    {
        var longPara = "<p>" + string.Join(' ', Enumerable.Repeat("word", AccessibilityCheck.LongParagraphWords + 1)) + "</p>";
        var r = Rules("<table><tr><td>a</td></tr></table><table><tr><th>h</th></tr></table>" + longPara + "<p>Short one.</p>");
        Assert.Equal(1, r[R.TableHeader]);
        Assert.Equal(1, r[R.LongParagraph]);
        Assert.Equal(2, AccessibilityCheck.IssueCount(AccessibilityCheck.Check("<table><tr><td>a</td></tr></table>" + longPara)));
    }

    [Fact]
    public void The_editors_and_content_health_show_the_checks()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        string Read(params string[] p) => File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(p).ToArray()));
        Assert.Contains("_AccessibilityChecks\", Model.Html", Read("Blog.Web", "Views", "Posts", "Edit.cshtml"));
        Assert.Contains("_AccessibilityChecks\", Model.Content", Read("Blog.Web", "Views", "Pages", "Edit.cshtml"));
        Assert.Contains("?filter=a11y", Read("Blog.Web", "Views", "ContentHealth", "Index.cshtml"));
        // Bodies are loaded only for the accessibility view.
        var ctrl = Read("Blog.Web", "Controllers", "ContentHealthController.cs");
        Assert.Contains("if (filter == \"a11y\")", ctrl);
    }

    /// <summary>Not an assertion about content: prints what the checks find on the dev posts, so a false positive shows up.</summary>
    [Fact]
    public async Task Report_on_dev_posts()
    {
        using var factory = new WebApplicationFactory<Program>();
        string? cs;
        try
        {
            cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            await probe.OpenAsync();
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return; }
        await using var conn = new SqlConnection(cs);
        var posts = (await conn.QueryAsync<(string Slug, string Html)>("SELECT Slug, ISNULL(Html, '') FROM Posts WHERE Status = 'Published'")).ToList();
        var byRule = posts.SelectMany(p => AccessibilityCheck.Check(p.Html)).GroupBy(f => f.Rule)
            .Select(g => $"{g.Key}: {g.Count()} posts, {g.Sum(f => f.Count)} items");
        _out.WriteLine($"{posts.Count} published posts. " + string.Join("; ", byRule));
    }
}
