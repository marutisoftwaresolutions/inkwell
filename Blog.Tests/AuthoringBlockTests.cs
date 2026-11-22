using System.Text.RegularExpressions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The callout and toggle blocks: registered, offered in the palette with a name, styled for readers
/// and for the editor from theme tokens only, and saved as markup that works without JavaScript.
/// </summary>
public class AuthoringBlockTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Theory]
    [InlineData("callout", "Callout")]
    [InlineData("toggle", "Toggle")]
    public void The_block_is_registered_and_offered_with_an_accessible_name(string type, string title)
    {
        Assert.Contains($"register('{type}'", Read("Blog.Web", "wwwroot", "js", "inkwell-builder.js"));
        Assert.Matches($"data-component=\"{type}\"[^>]*aria-label=\"{title}\"", Read("Blog.Web", "Views", "Shared", "_BuilderPalette.cshtml"));
    }

    [Fact]
    public void Every_builder_block_in_the_palette_is_registered()
    {
        var registered = Regex.Matches(Read("Blog.Web", "wwwroot", "js", "inkwell-builder.js") + Read("Blog.Web", "wwwroot", "js", "inkwell-sections.js"),
            @"register\('([a-z]+)'").Select(m => m.Groups[1].Value).ToHashSet();
        var offered = Regex.Matches(Read("Blog.Web", "Views", "Shared", "_BuilderPalette.cshtml"), "data-component=\"([a-z]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(offered);
        Assert.All(offered, o => Assert.Contains(o, registered));
    }

    [Theory]
    [InlineData("neutral-blog.css")]      // readers
    [InlineData("inkwell-builder.css")]   // the editor canvas
    public void Both_surfaces_style_the_blocks_from_tokens(string file)
    {
        var css = Read("Blog.Web", "wwwroot", "css", file);
        var block = css[css.IndexOf("/* Callout and toggle blocks.", StringComparison.Ordinal)..];
        Assert.Contains(".comp-callout", block);
        Assert.Contains(".comp-toggle", block);
        Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b", block);
        Assert.DoesNotMatch(@"\b(rgb|rgba|hsl)\(", block);
    }

    [Fact]
    public void A_callout_says_its_kind_in_words_and_a_toggle_is_a_native_details_element_saved_closed()
    {
        var js = Read("Blog.Web", "wwwroot", "js", "inkwell-builder.js");
        Assert.Contains("data-kind=\"note\"", js);
        Assert.Contains("<details class=\"comp-toggle\" open><summary", js);
        Assert.Contains("clone.querySelectorAll('details.comp-toggle').forEach(function (el) { el.removeAttribute('open'); });", js);
        var css = Read("Blog.Web", "wwwroot", "css", "neutral-blog.css");
        Assert.Contains("content: \"Note\"", css);
        Assert.Contains(".comp-callout[data-kind=\"warning\"]::before { content: \"Warning\"; }", css);
    }
}
