using System.Text.RegularExpressions;
using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Every listing page carries exactly one h1. Until 2026-11-15 category and tag archives had none in any layout, the
/// home page had none in thirteen of the twenty layouts, and search results had two in four of them.
/// </summary>
public class ListingHeadingSourceTests
{
    private static readonly string[] HeroLayouts = { "Broadsheet", "Classic", "Modern", "Neutral", "Showcase", "Studio" };

    [Fact]
    public void A_layout_never_prints_a_fixed_h1_of_its_own_except_the_magazine_masthead()
    {
        var views = Path.Combine(RepoRoot(), "Blog.Web", "Views", "Blog");
        foreach (var file in Directory.EnumerateFiles(views, "_Index_*.cshtml"))
        {
            var count = Regex.Matches(File.ReadAllText(file), "<h1[\\s>]").Count;
            var allowed = Path.GetFileName(file) == "_Index_Magazine.cshtml" ? 1 : 0;   // the home masthead, first page only
            Assert.True(count == allowed, $"{Path.GetFileName(file)} has {count} literal h1 element(s); expected {allowed}");
        }
    }

    [Fact]
    public void A_hero_title_steps_down_when_the_page_has_its_own_heading()
    {
        var views = Path.Combine(RepoRoot(), "Blog.Web", "Views", "Blog");
        foreach (var name in HeroLayouts)
        {
            var source = File.ReadAllText(Path.Combine(views, $"_Index_{name}.cshtml"));
            Assert.Contains("var heroHeading = (ViewData[\"PageHasHeading\"] as bool? ?? false) ? \"h2\" : \"h1\";", source);
            Assert.Contains("@Html.Raw(\"</\" + heroHeading + \">\")", source);
        }
    }

    [Fact]
    public void The_listing_view_gives_archives_search_and_the_home_page_their_heading()
    {
        var index = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Views", "Blog", "Index.cshtml"));
        Assert.Contains("<h1>@archiveHeading</h1>", index);
        Assert.Contains("<h1 class=\"ink-visually-hidden\">", index);
        Assert.Contains("ViewData[\"PageHasHeading\"] = true;", index);
        // Only Magazine draws the home heading itself (its masthead tagline). A hero layout must not be listed: its
        // hero is the newest post, and a post title as the home page's h1 changes with every post (2026-11-21).
        var homeHeadingLine = index.Split('\n').Single(l => l.Contains("var layoutsWithHomeHeading"));
        Assert.Contains("\"Magazine\"", homeHeadingLine);
        foreach (var name in HeroLayouts) Assert.DoesNotContain($"\"{name}\"", homeHeadingLine);
        Assert.Contains("homeHeadingTagline", index);

        var controller = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Controllers", "BlogController.cs"));
        Assert.Contains("ViewBag.ArchiveKind = \"Category\"; ViewBag.ArchiveHeading = categoryName;", controller);
        Assert.Contains("ViewBag.ArchiveKind = \"Tag\"; ViewBag.ArchiveHeading = tagName;", controller);

        var css = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "wwwroot", "css", "inkwell.css"));
        Assert.Contains(".ink-visually-hidden {", css);
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Blog.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

/// <summary>The same rule on rendered pages, with whatever layout the dev site uses. Skips without a database.</summary>
public class ListingHeadingRenderTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public ListingHeadingRenderTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    [Fact]
    public async Task Home_search_category_and_tag_pages_each_have_one_h1()
    {
        if (!TestDatabase.Available(_factory)) { _out.WriteLine("Dev database unavailable; skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var ownerId = (await users.GetFirstAdminAsync())?.Id ?? Guid.Empty;
        var category = (await scope.ServiceProvider.GetRequiredService<ICategoryRepository>().GetPublicAsync(ownerId)).FirstOrDefault();

        var paths = new List<string> { "/", "/search?q=software" };
        if (category is not null) paths.Add("/category/" + category.Slug);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var home = await client.GetStringAsync("/");
        var tag = Regex.Match(home, "href=\"(/tag/[a-z0-9-]+)\"");
        if (tag.Success) paths.Add(tag.Groups[1].Value);

        foreach (var path in paths)
        {
            var response = await client.GetAsync(path);
            if (!response.IsSuccessStatusCode) { _out.WriteLine($"{path}: {(int)response.StatusCode}; skipped."); continue; }
            var html = await response.Content.ReadAsStringAsync();
            var count = Regex.Matches(html, "<h1[\\s>]").Count;
            Assert.True(count == 1, $"{path} has {count} h1 element(s)");
        }

        // The home page's h1 names the site, whatever the layout; it is never the lead post's title.
        var settings = await scope.ServiceProvider.GetRequiredService<ISettingRepository>().GetSettingsAsync(ownerId);
        var homeH1 = Regex.Match(home, "<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline);
        if (homeH1.Success && !string.IsNullOrWhiteSpace(settings.SiteName) && !home.Contains("ink-masthead"))
        {
            var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(homeH1.Groups[1].Value, "<[^>]+>", "")).Trim();
            Assert.StartsWith(settings.SiteName.Trim(), text);
        }

        if (category is not null)
        {
            var html = await client.GetStringAsync("/category/" + category.Slug);
            Assert.Contains("ink-archive-head", html);
            Assert.Matches("<h1>\\s*" + Regex.Escape(System.Net.WebUtility.HtmlEncode(category.Name)) + "\\s*</h1>", html);
        }
    }
}
