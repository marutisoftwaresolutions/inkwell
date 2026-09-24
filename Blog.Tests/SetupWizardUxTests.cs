using System.Text.RegularExpressions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Source-scanning guard for the setup wizard (Views/Setup/*, _SetupLayout): the first screens a
/// new operator ever sees. Errors are placed at the field, the palette comes from tokens in both
/// colour schemes, every control is labelled, and the copy is calm — no emoji, no exclamation marks.
/// </summary>
public class SetupWizardUxTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Blog.Web"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Web(string relative) => File.ReadAllText(Path.Combine(Root(), "Blog.Web", relative.Replace('/', Path.DirectorySeparatorChar)));

    private static IEnumerable<(string Name, string Html)> SetupViews()
    {
        foreach (var name in new[] { "Index", "Step2", "Step3", "Review", "Complete" })
            yield return (name, Web($"Views/Setup/{name}.cshtml"));
    }

    [Fact]
    public void Layout_declares_a_colour_scheme_and_links_the_desk_tokens_before_the_admin_stylesheet()
    {
        var layout = Web("Views/Shared/_SetupLayout.cshtml");

        Assert.Matches(new Regex("<html[^>]*data-theme=\"system\""), layout);
        var tokens = layout.IndexOf("css/desk-tokens.css", StringComparison.Ordinal);
        var admin = layout.IndexOf("css/admin.css", StringComparison.Ordinal);
        Assert.True(tokens >= 0, "layout does not link css/desk-tokens.css");
        Assert.True(admin > tokens, "css/admin.css must load after css/desk-tokens.css");
        Assert.Contains("setup-body", layout);
    }

    [Fact]
    public void Wizard_palette_comes_from_tokens_that_exist_in_both_schemes()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "Blog.Web", "wwwroot", "css", "admin.css"));

        Assert.Contains("body.setup-body", css);
        Assert.Contains("/* setup tokens */", css);
        foreach (var token in new[] { "--success", "--warning" })
        {
            Assert.Matches(new Regex($@":root\s*\{{[^}}]*{Regex.Escape(token)}\s*:"), css);
            Assert.Matches(new Regex($@"html\[data-theme=""dark""\]\s*\{{[^}}]*{Regex.Escape(token)}\s*:"), css);
        }
        for (var i = 1; i <= 4; i++)
            Assert.Contains($".strength-fill.strength-{i}", css);
    }

    [Fact]
    public void Nothing_in_the_wizard_carries_a_hard_coded_colour()
    {
        var hex = new Regex(@"(?:fill|stroke|color|background)\s*[=:]\s*[""']?#[0-9a-fA-F]{3,8}\b");
        foreach (var (name, html) in SetupViews())
            Assert.False(hex.IsMatch(html), $"{name}: hard-coded colour");
    }

    [Fact]
    public void Every_step_shows_where_the_operator_is()
    {
        foreach (var (name, html) in SetupViews().Where(v => v.Name != "Complete"))
            Assert.Matches(new Regex(@"Step [1-4] of 4"), html);
    }

    [Fact]
    public void Form_steps_place_errors_at_the_field_and_summarise_them_with_role_alert()
    {
        foreach (var name in new[] { "Step2", "Step3" })
        {
            var html = Web($"Views/Setup/{name}.cshtml");
            Assert.Contains("role=\"alert\"", html);
            Assert.Contains("class=\"field-error\"", html);
            Assert.Contains("aria-invalid=", html);
            Assert.Contains("aria-describedby=", html);
        }
    }

    [Fact]
    public void Every_label_points_at_a_control_that_exists()
    {
        foreach (var (name, html) in SetupViews())
        {
            foreach (Match m in Regex.Matches(html, @"<label[^>]*\bfor=""([^""]+)"""))
                Assert.True(Regex.IsMatch(html, $@"\bid=""{Regex.Escape(m.Groups[1].Value)}"""), $"{name}: label for=\"{m.Groups[1].Value}\" has no control");
        }
    }

    [Fact]
    public void Copy_is_calm_no_emoji_no_exclamation_marks()
    {
        var emoji = new Regex(@"[\uD83C-\uDBFF][\uDC00-\uDFFF]|[☀-➿]");
        var visible = new Regex(@"<(h1|p|button|a|dt|dd|label)\b[^>]*>([^<]*)</\1>");
        foreach (var (name, html) in SetupViews())
        {
            Assert.False(emoji.IsMatch(html), $"{name}: emoji in copy");
            foreach (Match m in visible.Matches(html))
                Assert.DoesNotContain("!", m.Groups[2].Value);
        }
    }
}
