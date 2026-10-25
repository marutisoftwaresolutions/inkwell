using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Guards for header menu visibility across every reachable navbar variant. On 2026-10-13 the live Classic
/// "Navigate" menu rendered transparent over the hero image: the theme block emitted "-- background : …"
/// (an invalid declaration), so every Tailwind-namespace token was undefined in light mode.
/// </summary>
public class NavbarMenuVisibilitySourceTests
{
    // The variants NavbarViewComponent can render; Default.cshtml is not reachable (unknown values fall back to Neutral).
    private static readonly string[] Reachable = { "Neutral", "Magazine", "Grid", "Minimal", "Classic", "Modern", "Feed" };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
    private static string Navbar(string name) =>
        File.ReadAllText(Path.Combine(Root(), "Blog.Web", "Views", "Shared", "Components", "Navbar", name + ".cshtml"));

    private static readonly Regex OpenTag = new(@"<(div|nav|header|a|button)\b[^>]*>", RegexOptions.Compiled);
    private static readonly Regex Panel = new(@"<div\b[^>]*\bx-show=""[^""]*""[^>]*>", RegexOptions.Compiled);

    [Fact]
    public void Theme_colour_tokens_are_emitted_as_valid_custom_properties()
    {
        var layout = File.ReadAllText(Path.Combine(Root(), "Blog.Web", "Views", "Shared", "_PublicLayout.cshtml"));
        Assert.DoesNotContain("@:-- ", layout);
        Assert.Contains("@:--@(s.SettingKey): @(s.EffectiveValue);", layout);
        // Defaults render unconditionally, so a partial colour set never leaves --background undefined.
        Assert.DoesNotMatch(new Regex(@"else\s*\{\s*<style>\s*:root\s*\{\s*--background"), layout);
    }

    [Fact]
    public void Every_menu_panel_is_opaque_and_stacked_above_the_page()
    {
        var offenders = new List<string>();
        foreach (var name in Reachable)
        {
            foreach (Match m in Panel.Matches(Navbar(name)))
            {
                var tag = m.Value;
                if (!Regex.IsMatch(tag, @"style=""[^""]*background:\s*var\(--")) offenders.Add($"{name}: menu panel has no background");
                if (tag.Contains("absolute") && !Regex.IsMatch(tag, @"\bz-\d+")) offenders.Add($"{name}: dropdown has no z-index");
            }
        }
        Assert.True(offenders.Count == 0, string.Join('\n', offenders));
    }

    [Fact]
    public void No_navbar_tag_carries_two_style_attributes()
    {
        // The browser keeps the first and silently drops the second, so half the intended styling vanishes.
        var offenders = new List<string>();
        foreach (var name in Reachable)
            foreach (Match m in OpenTag.Matches(Navbar(name)))
                if (Regex.Matches(m.Value, @"\sstyle=""").Count > 1) offenders.Add(name);
        Assert.True(offenders.Count == 0, "Duplicate style attribute in: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Menus_open_on_click_and_every_mobile_toggle_has_a_drawer()
    {
        foreach (var name in Reachable)
        {
            var view = Navbar(name);
            Assert.False(view.Contains("group-hover:block"), name + " has a hover-only menu (unreachable by keyboard and touch)");
            var toggle = Regex.Match(view, @"<button\b[^>]*aria-label=""Open menu""[^>]*>");
            if (!toggle.Success) continue;
            Assert.True(toggle.Value.Contains("mobileOpen"), name + ": the mobile menu button has no handler");
            Assert.True(Regex.IsMatch(view, @"x-show=""mobileOpen"""), name + ": the mobile menu button opens nothing");
        }
    }
}

/// <summary>Rendered check against the dev database: the home page defines the tokens the menus paint with.</summary>
public class NavbarMenuVisibilityRenderTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public NavbarMenuVisibilityRenderTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    [Fact]
    public async Task Home_page_defines_the_menu_colour_tokens()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/");
        Assert.DoesNotMatch(new Regex(@"--\s+background\s*:"), html);
        Assert.Matches(new Regex(@"--background:\s*\S"), html);
        Assert.Matches(new Regex(@"--card:\s*\S"), html);
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
