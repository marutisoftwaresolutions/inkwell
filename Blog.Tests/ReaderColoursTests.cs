using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Theme colours that reach the reader (GitHub issue #2): saved colours are mapped onto the tokens the
/// public layouts read, only when the site opts in, and never as anything but a plain hex colour.
/// </summary>
public class ReaderColoursTests
{
    private static Dictionary<string, string> Theme(bool on, string background = "#102030") => new()
    {
        [ReaderColours.SettingKey] = on ? "true" : "false",
        ["background"] = background, ["foreground"] = "#111111", ["primary"] = "#0a58ca",
        ["card"] = "#fafafa", ["border"] = "#dddddd", ["muted-foreground"] = "#666666",
    };

    [Fact]
    public void Off_by_default_so_an_upgrade_recolours_no_existing_site()
    {
        Assert.Equal("", ReaderColours.Css(Theme(on: false)));
        Assert.Equal("", ReaderColours.Css(new Dictionary<string, string> { ["background"] = "#102030" }));
    }

    [Fact]
    public void Saved_colours_land_on_the_tokens_the_reader_layouts_read()
    {
        var css = ReaderColours.Css(Theme(on: true));
        Assert.Contains("--bg:#102030;", css);
        Assert.Contains("--fg:#111111;", css);
        Assert.Contains("--fg-body:#111111;", css);
        Assert.Contains("--link:#0a58ca;", css);
        Assert.Contains("--surface:#fafafa;", css);
        Assert.Contains("--border:#dddddd;", css);
        Assert.Contains("--fg-meta:#666666;", css);
        Assert.Contains("body.inkwell-root", css);
    }

    [Fact]
    public void A_forced_dark_scheme_keeps_the_dark_palette()
    {
        var css = ReaderColours.Css(Theme(on: true));
        Assert.DoesNotContain("html[data-theme=\"dark\"] body", css);
        Assert.Contains("html[data-theme=\"light\"] body.inkwell-root", css);
        Assert.Contains("html:not([data-theme=\"dark\"]) body.inkwell-root", css);
    }

    [Theory]
    [InlineData("red;} body{display:none")]
    [InlineData("url(javascript:alert(1))")]
    [InlineData("#12")]
    [InlineData("")]
    public void A_value_that_is_not_a_hex_colour_is_dropped(string bad)
    {
        var css = ReaderColours.Css(Theme(on: true, background: bad));
        Assert.DoesNotContain("--bg:", css);
        Assert.DoesNotContain("display:none", css);
        Assert.Contains("--fg:#111111;", css);   // the rest still applies
    }

    [Fact]
    public void The_public_layout_emits_the_mapping_and_the_theme_screen_offers_the_switch()
    {
        var root = RepoRoot();
        var layout = File.ReadAllText(Path.Combine(root, "Blog.Web", "Views", "Shared", "_PublicLayout.cshtml"));
        Assert.Contains("ReaderColours.Css(themeDict)", layout);
        var screen = File.ReadAllText(Path.Combine(root, "Blog.Web", "Views", "ThemeSettings", "Index.cshtml"));
        Assert.Contains("ReaderColours.SettingKey", screen);
        Assert.Contains("Reader colours come from", screen);
        var controller = File.ReadAllText(Path.Combine(root, "Blog.Web", "Controllers", "ThemeSettingsController.cs"));
        Assert.Contains("ReaderColours.SettingKey", controller);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
