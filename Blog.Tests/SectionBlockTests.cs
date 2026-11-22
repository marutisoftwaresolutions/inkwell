using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Section blocks for pages: every block the script registers is offered in the palette, the
/// stylesheet takes its colours only from theme tokens (so a section follows every layout, preset and
/// the dark scheme), and a page that uses a section loads the stylesheet while other pages do not.
/// </summary>
public class SectionBlockTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] Blocks = ["columns", "features", "cta", "testimonials", "pricing", "faqsection", "contact", "logos"];
    private readonly WebApplicationFactory<Program> _factory;
    public SectionBlockTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void Every_registered_section_has_a_palette_button_and_every_button_a_block()
    {
        var script = Read("Blog.Web", "wwwroot", "js", "inkwell-sections.js");
        var registered = Regex.Matches(script, @"InkwellBuilder\.register\('([a-z]+)'").Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();
        Assert.Equal(Blocks.OrderBy(x => x).ToArray(), registered);

        var palette = Read("Blog.Web", "Views", "Shared", "_BuilderPalette.cshtml");
        var sectionPart = palette[palette.IndexOf("@if (withSections)", StringComparison.Ordinal)..];
        var offered = Regex.Matches(sectionPart, "data-component=\"([a-z]+)\"").Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();
        Assert.Equal(registered, offered);
        // Icon-only buttons carry a name (UI/UX rule 5).
        Assert.Equal(offered.Length, Regex.Matches(sectionPart, "aria-label=\"[^\"]+\"").Count);
    }

    [Fact]
    public void The_stylesheet_takes_every_colour_from_a_theme_token()
    {
        var css = Regex.Replace(Read("Blog.Web", "wwwroot", "css", "inkwell-sections.css"), @"/\*.*?\*/", "", RegexOptions.Singleline);
        Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b", css);
        Assert.DoesNotMatch(@"\b(rgb|rgba|hsl|hsla)\(", css);
        Assert.DoesNotMatch(@":\s*(white|black|red|blue|green|gray|grey|silver)\b", css);
        // Both namespaces are named, so the Desk canvas and the reader page show the same section.
        Assert.Contains("var(--fg, var(--foreground))", css);
        Assert.Contains("var(--bg, var(--background))", css);
        Assert.Contains("var(--surface, var(--card))", css);
    }

    [Fact]
    public void Sections_stack_on_a_phone()
    {
        var css = Read("Blog.Web", "wwwroot", "css", "inkwell-sections.css");
        Assert.Contains("@media (max-width: 600px)", css);
        Assert.Contains("grid-template-columns: minmax(0, 1fr)", css);
    }

    [Fact]
    public void Saved_markup_carries_no_inline_colour_and_placeholders_say_so()
    {
        var script = Read("Blog.Web", "wwwroot", "js", "inkwell-sections.js");
        Assert.DoesNotMatch(@"style=\\?""[^""]*(color|background)", script);
        Assert.Contains("Placeholder", script);
        // Testimonials are the owner's to write: the block ships no invented quote, name or company.
        var testimonial = script[script.IndexOf("register('testimonials'", StringComparison.Ordinal)..script.IndexOf("register('pricing'", StringComparison.Ordinal)];
        Assert.Contains("real quote from a real customer", testimonial);
        Assert.Contains("invented testimonial misleads", testimonial);
    }

    [Theory]
    [InlineData("Create.cshtml")]
    [InlineData("Edit.cshtml")]
    public void The_page_editors_load_sections_after_the_builder(string view)
    {
        var html = Read("Blog.Web", "Views", "Pages", view);
        var builder = html.IndexOf("~/js/inkwell-builder.js", StringComparison.Ordinal);
        var sections = html.IndexOf("~/js/inkwell-sections.js", StringComparison.Ordinal);
        Assert.True(builder > 0 && sections > builder);
        Assert.Contains("~/css/inkwell-sections.css", html);
        Assert.Contains("ViewData[\"BuilderSections\"] = true", html);
    }

    [Fact]
    public void The_post_editor_does_not_offer_section_blocks()
    {
        Assert.DoesNotContain("BuilderSections", Read("Blog.Web", "Views", "Posts", "Edit.cshtml"));
    }

    [Fact]
    public async Task A_page_with_a_section_renders_it_and_loads_the_stylesheet_and_other_pages_do_not()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);
        var author = await conn.ExecuteScalarAsync<Guid?>("SELECT TOP 1 Id FROM Users WHERE Role = 'Admin' ORDER BY CreatedAt");
        if (author is null) return;

        var tag = Guid.NewGuid().ToString("N")[..8];
        var withSection = "zz-section-check-" + tag;
        var plain = "zz-plain-check-" + tag;
        const string section = "<section class=\"sec-section sec-cta\"><h2>A clear next step</h2><p>Why.</p><p class=\"sec-actions\"><a class=\"sec-btn\" href=\"/contact\">Talk to us</a></p></section>";
        try
        {
            foreach (var (slug, content) in new[] { (withSection, section), (plain, "<p>Plain page.</p>") })
                await conn.ExecuteAsync(@"INSERT INTO Pages (Id, Title, Slug, Content, AuthorId, IsPublished, IsInNav, SortOrder, CreatedAt, UpdatedAt, PublishedAt)
                                          VALUES (NEWID(), @title, @slug, @content, @author, 1, 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME())",
                    new { title = "Section check", slug, content, author });

            var client = _factory.CreateClient();
            var html = await client.GetStringAsync("/" + withSection);
            Assert.Contains("sec-cta", html);
            Assert.Contains("href=\"/contact\"", html);
            Assert.Contains("/css/inkwell-sections.css", html);

            Assert.DoesNotContain("/css/inkwell-sections.css", await client.GetStringAsync("/" + plain));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM Pages WHERE Slug IN @slugs", new { slugs = new[] { withSection, plain } });
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
