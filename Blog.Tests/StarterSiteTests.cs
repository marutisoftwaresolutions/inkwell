using System.Text.RegularExpressions;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Starter sites offered by the setup wizard: each names a layout and preset that exist, builds its
/// pages from the section blocks' own markup, carries only placeholder copy that says so, and is
/// applied by a service that never overwrites a page.
/// </summary>
public class StarterSiteTests
{
    [Fact]
    public void The_wizard_offers_blog_business_portfolio_and_newsletter()
    {
        Assert.Equal(new[] { "blog", "business", "portfolio", "newsletter" }, StarterSites.All.Select(s => s.Key).ToArray());
        Assert.All(StarterSites.All, s => Assert.False(string.IsNullOrWhiteSpace(s.Description)));
    }

    [Fact]
    public void Every_starter_names_a_layout_with_a_view_and_a_preset_that_exists()
    {
        var root = RepoRoot();
        var controller = File.ReadAllText(Path.Combine(root, "Blog.Web", "Controllers", "ThemeSettingsController.cs"));
        foreach (var s in StarterSites.All)
        {
            Assert.Contains(s.Layout, StarterSites.KnownLayouts);
            Assert.Contains(s.Preset, StarterSites.KnownPresets);
            Assert.True(File.Exists(Path.Combine(root, "Blog.Web", "Views", "Blog", $"_Index_{s.Layout}.cshtml")), $"No index view for layout {s.Layout}");
            Assert.Contains($"\"{s.Layout}\"", controller);   // in validLayouts
            Assert.Contains($"\"{s.Preset}\"", controller);   // in validPresets
        }
    }

    [Fact]
    public void Pages_have_unique_plain_slugs_a_title_and_a_description()
    {
        foreach (var s in StarterSites.All)
        {
            Assert.NotEmpty(s.Pages);
            Assert.Equal(s.Pages.Count, s.Pages.Select(p => p.Slug).Distinct().Count());
            foreach (var p in s.Pages)
            {
                Assert.Matches("^[a-z][a-z0-9-]*$", p.Slug);
                Assert.False(string.IsNullOrWhiteSpace(p.Title));
                Assert.InRange(p.MetaDescription.Length, 40, 160);
            }
        }
    }

    [Fact]
    public void Starter_copy_is_placeholder_text_that_says_so_and_makes_no_claims()
    {
        foreach (var page in StarterSites.All.SelectMany(s => s.Pages))
        {
            Assert.Contains("Placeholder", page.Html);
            Assert.StartsWith("Placeholder", page.MetaDescription);
            // No invented proof: no testimonial or logo section, no currency amounts, no quoted people.
            Assert.DoesNotContain("sec-testimonial", page.Html);
            Assert.DoesNotContain("sec-logos", page.Html);
            Assert.DoesNotMatch(@"[$€£₹]\s?\d", page.Html);
            Assert.DoesNotContain("<blockquote", page.Html);
        }
    }

    [Fact]
    public void Starter_markup_is_what_the_builder_saves_balanced_and_free_of_inline_style_and_script()
    {
        var known = new[] { "sec-section", "sec-features", "sec-cta", "sec-pricing", "sec-faq", "sec-contact", "sec-columns", "sec-col", "sec-feature",
            "sec-grid", "sec-lede", "sec-actions", "sec-btn", "sec-plan", "sec-price", "sec-faq-item" };
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "wwwroot", "css", "inkwell-sections.css"));
        foreach (var page in StarterSites.All.SelectMany(s => s.Pages))
        {
            Assert.DoesNotContain("style=", page.Html);
            Assert.DoesNotContain("<script", page.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("contenteditable", page.Html);
            foreach (var tag in new[] { "section", "div", "h2", "h3", "p", "ul", "li", "a" })
                Assert.Equal(Regex.Matches(page.Html, $"<{tag}[ >]").Count, Regex.Matches(page.Html, $"</{tag}>").Count);
            foreach (Match m in Regex.Matches(page.Html, "class=\"([^\"]+)\""))
                foreach (var cls in m.Groups[1].Value.Split(' '))
                {
                    Assert.Contains(cls, known);
                    // sec-features and sec-pricing only identify the block to the editor; their look comes from sec-section.
                    if (cls is not ("sec-features" or "sec-pricing")) Assert.Contains("." + cls, css);   // every other class is styled
                }
        }
    }

    [Theory]
    [InlineData("business", "business")]
    [InlineData("BLOG", "blog")]
    [InlineData("none", "none")]
    [InlineData("", "none")]
    [InlineData(null, "none")]
    [InlineData("<script>", "none")]
    public void A_choice_from_the_wizard_is_a_known_starter_or_none(string? sent, string stored) =>
        Assert.Equal(stored, StarterSites.Normalize(sent));

    [Fact]
    public void Applying_a_starter_never_overwrites_a_page_or_a_chosen_theme()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Services", "StarterSiteService.cs"));
        Assert.Contains("GetBySlugAsync(page.Slug) is not null", service);
        Assert.Contains("skipped++", service);
        Assert.Contains("if (existing.Count > 0) return false;", service);
        Assert.DoesNotContain("UpdateAsync", service);
        Assert.DoesNotContain("DeleteAsync", service);
        // The wizard offers "Empty site" and applies the starter without letting it fail the install.
        var step = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Views", "Setup", "Step2.cshtml"));
        Assert.Contains("value=\"none\"", step);
        var setup = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Controllers", "SetupController.cs"));
        Assert.Contains("_starters.ApplyAsync(userId, starterKey)", setup);
        Assert.Contains("StarterFailed", setup);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
