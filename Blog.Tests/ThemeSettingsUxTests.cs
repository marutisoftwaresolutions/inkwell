using System.Text.RegularExpressions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Source-level guards for Admin → Theme (Views/ThemeSettings/Index.cshtml): the screen stacks on a
/// phone, renders in both colour schemes, is keyboard/screen-reader reachable, tracks unsaved fine-tune
/// edits, and does not describe the self-hosted font list as Google Fonts.
/// </summary>
public class ThemeSettingsUxTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Web(string rel) => File.ReadAllText(Path.Combine(Root(), "Blog.Web", rel.Replace('/', Path.DirectorySeparatorChar)));

    private static string View() => Web("Views/ThemeSettings/Index.cshtml");

    /// <summary>The view minus the two preset-swatch data blocks, which legitimately carry each preset's own hex colours.</summary>
    private static string ViewWithoutSwatchData()
    {
        var html = View();
        // Inkwell preset table: `var inkwellPresets = new[] { ... };`
        html = Regex.Replace(html, @"var inkwellPresets = new\[\]\s*\{.*?\};", "", RegexOptions.Singleline);
        Assert.DoesNotContain("var inkwellPresets", html);
        return html;
    }

    [Fact]
    public void No_hard_coded_ink_colour_outside_preset_swatch_data()
    {
        var html = ViewWithoutSwatchData();
        Assert.DoesNotContain("rgba(26,26,26", html);
        Assert.DoesNotContain("rgba(26, 26, 26", html);
        Assert.DoesNotContain("#1A1A1A", html, StringComparison.OrdinalIgnoreCase);
        // Layout thumbnails draw their marks in currentColor inside a text-foreground box, so they invert with the scheme.
        Assert.Contains("currentColor", html);
        Assert.Contains("bg-muted/60 text-foreground", html);
    }

    [Fact]
    public void Editor_panel_stacks_below_lg_and_the_preview_keeps_a_phone_height()
    {
        var html = View();
        Assert.Contains("lg:w-[420px]", html);
        Assert.DoesNotContain(" w-[420px]", html);
        Assert.DoesNotContain("\"w-[420px]", html);
        Assert.Contains("flex-col lg:flex-row", html);
        Assert.DoesNotContain("\"flex gap-6 h-[calc(100vh-8rem)]", html);
        Assert.Contains("lg:h-[calc(100vh-8rem)]", html);
        Assert.Matches(new Regex(@"<iframe[^>]*min-h-\[\d+vh\]"), html);
    }

    [Fact]
    public void Fine_tune_form_is_tracked_and_preset_tiles_ask_before_discarding_edits()
    {
        var html = View();
        Assert.Matches(new Regex(@"<form[^>]*asp-action=""Update""[^>]*data-track-changes"), html);
        Assert.Contains("data-dirty-indicator", html);
        // Every tile POST (layout, Inkwell preset, classic preset) is guarded by the shared dialog when the form is dirty.
        Assert.Matches(new Regex(@"<form[^>]*asp-action=""ApplyLayoutPreset""[^>]*data-theme-tile"), html);
        Assert.Matches(new Regex(@"<form[^>]*asp-action=""ApplyInkwellPreset""[^>]*data-theme-tile"), html);
        Assert.Matches(new Regex(@"<form[^>]*asp-action=""ApplyPreset""[^>]*data-theme-tile"), html);
        Assert.Contains("InkwellDesk.isDirty()", html);
        Assert.Contains("InkwellDialog.confirm(", html);
        Assert.Contains("tone: 'warning'", html);
        Assert.DoesNotContain("window.confirm(", html);
        Assert.DoesNotContain("confirm('", html);
    }

    [Fact]
    public void Font_picker_is_a_button_that_opens_a_keyboard_driven_listbox()
    {
        var html = View();
        Assert.Matches(new Regex(@"<button[^>]*aria-haspopup=""listbox"""), html);
        Assert.DoesNotContain("<div @@click=\"isOpen = !isOpen\"", html);
        Assert.Contains("role=\"listbox\"", html);
        Assert.Contains("role=\"option\"", html);
        Assert.Contains(":aria-expanded=", html);
        Assert.Contains(":aria-activedescendant=", html);
        foreach (var key in new[] { "arrow-down", "arrow-up", "enter", "escape" })
            Assert.Contains("@@keydown." + key, html);
    }

    [Fact]
    public void Every_colour_input_has_a_label_and_the_preview_iframe_has_a_title()
    {
        var html = View();
        var inputs = Regex.Matches(html, @"<input[^>]*type=""color""[^>]*>");
        Assert.True(inputs.Count > 0, "expected at least one colour input");
        foreach (Match m in inputs)
        {
            var id = Regex.Match(m.Value, @"\bid=""([^""]+)""").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(id), "colour input without id: " + m.Value);
            Assert.Contains($"<label for=\"{id}\"", html);
        }
        Assert.Matches(new Regex(@"<iframe[^>]*\btitle=""[^""]+"""), html);
    }

    [Fact]
    public void Preset_and_layout_tiles_expose_their_active_state()
    {
        var html = View();
        var count = Regex.Matches(html, @"<button type=""submit"" aria-pressed=").Count;
        Assert.True(count >= 3, $"expected aria-pressed on the layout, Inkwell-preset and classic-preset tiles, found {count}");
    }

    [Fact]
    public void Fonts_are_described_as_the_self_hosted_list_not_Google_Fonts()
    {
        var html = View();
        Assert.DoesNotContain("Google Fonts", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("googleFonts", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", html);
        Assert.Contains("/fonts/fonts.css", html);
        Assert.Contains("placeholder=\"Search fonts\"", html);
    }

    [Fact]
    public void Section_copy_names_the_layouts_each_colour_preset_governs()
    {
        var html = View();
        Assert.DoesNotContain("Applies to Magazine layout", html);
        Assert.Contains("Colour preset — Editorial, Review and Modern layouts", html);
        Assert.Contains("Colour preset — Classic layouts", html);
        Assert.Contains("Governs Neutral, Classic, Minimal, Modern and Grid", html);
    }

    [Fact]
    public void Preset_tile_frame_is_styled_from_tokens_in_admin_css()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "Blog.Web", "wwwroot", "css", "admin.css"));
        Assert.Contains(".theme-preset-tile", css);
        Assert.Contains(".theme-preset-tile.is-active", css);
        var block = css[css.IndexOf(".theme-preset-tile", StringComparison.Ordinal)..];
        Assert.Contains("var(--foreground)", block);
        Assert.DoesNotContain("#1A1A1A", block, StringComparison.OrdinalIgnoreCase);
    }
}
