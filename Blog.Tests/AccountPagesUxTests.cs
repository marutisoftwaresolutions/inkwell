using System.Text.RegularExpressions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Source-level guards for the account pages (sign in, register, forgot/reset password, first-run setup, profile):
/// real password fields with the right autocomplete, inline announced errors, bound labels, and the shared Desk
/// token stylesheet so they follow the colour scheme.
/// </summary>
public class AccountPagesUxTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
    private static string Web(string rel) => File.ReadAllText(Path.Combine(Root(), "Blog.Web", rel.Replace('/', Path.DirectorySeparatorChar)));

    private static IEnumerable<(string Name, string Html)> AccountViews()
    {
        var dir = Path.Combine(Root(), "Blog.Web", "Views", "Account");
        foreach (var path in Directory.EnumerateFiles(dir, "*.cshtml").OrderBy(p => p))
            yield return (Path.GetFileName(path), File.ReadAllText(path));
    }

    private static readonly Regex InputTag = new(@"<input\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TypeAttr = new(@"\btype=""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NameOrIdAttr = new(@"\b(?:name|id)=""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AutocompleteAttr = new(@"\bautocomplete=""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void Password_fields_are_native_password_inputs_with_the_right_autocomplete()
    {
        var offenders = new List<string>();
        foreach (var (name, html) in AccountViews())
        {
            foreach (Match m in InputTag.Matches(html))
            {
                var tag = m.Value;
                var type = TypeAttr.Match(tag).Groups[1].Value.ToLowerInvariant();
                var namesPassword = NameOrIdAttr.Matches(tag).Any(a => a.Groups[1].Value.Contains("password", StringComparison.OrdinalIgnoreCase));

                // No peek-then-mask display boxes: a text input standing in for a password field.
                if (type == "text" && namesPassword)
                    offenders.Add($"{name}: text input named/id'd as a password → {Trim(tag)}");

                if (type == "password")
                {
                    var ac = AutocompleteAttr.Match(tag).Groups[1].Value;
                    if (ac is not ("current-password" or "new-password"))
                        offenders.Add($"{name}: password input without autocomplete=current-password|new-password → {Trim(tag)}");
                }
            }
            Assert.DoesNotContain("password-real", html); // the old hidden mirror field
        }
        Assert.True(offenders.Count == 0, string.Join('\n', offenders));
    }

    [Fact]
    public void Every_account_page_renders_errors_inline_as_an_alert_and_never_auto_dismisses_them()
    {
        foreach (var (name, html) in AccountViews())
        {
            Assert.True(html.Contains("role=\"alert\"", StringComparison.Ordinal), $"{name}: no role=\"alert\" error markup");
            Assert.True(html.Contains("id=\"form-error\"", StringComparison.Ordinal), $"{name}: no #form-error inline error");
            Assert.False(html.Contains("toast-error", StringComparison.Ordinal), $"{name}: still renders the corner error toast");
            foreach (Match m in Regex.Matches(html, @"setTimeout\([^;]*?remove\(\)"))
                Assert.Fail($"{name}: an error/notice is removed on a timer → {Trim(m.Value)}");
        }
    }

    [Fact]
    public void Every_label_on_the_account_pages_is_bound_to_a_control()
    {
        var offenders = new List<string>();
        foreach (var (name, html) in AccountViews())
        {
            foreach (Match m in Regex.Matches(html, @"<label\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var forMatch = Regex.Match(m.Value, @"\bfor=""([^""]+)""");
                if (!forMatch.Success) { offenders.Add($"{name}: {Trim(m.Value)}"); continue; }
                var id = forMatch.Groups[1].Value;
                if (!Regex.IsMatch(html, $@"\bid=""{Regex.Escape(id)}"""))
                    offenders.Add($"{name}: label for=\"{id}\" has no matching id");
            }
        }
        Assert.True(offenders.Count == 0, "Label without a bound control:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Every_account_page_uses_the_shared_desk_tokens_and_declares_a_colour_scheme()
    {
        foreach (var (name, html) in AccountViews())
        {
            var standalone = Regex.IsMatch(html, @"Layout\s*=\s*null");
            if (standalone)
            {
                Assert.True(html.Contains("css/desk-tokens.css", StringComparison.Ordinal), $"{name}: standalone page does not link css/desk-tokens.css");
                Assert.Matches(new Regex(@"<html[^>]*data-theme=""@colorScheme"""), html);
                Assert.DoesNotContain("--background:", html); // no private light-only palette
            }
            else
            {
                Assert.Contains("_AdminLayout", html); // inherits the tokens through the layout
            }
            Assert.DoesNotMatch(new Regex(@"(?:color|background)\s*:\s*#[0-9a-fA-F]{3,8}\b"), html); // no hard-coded colours
        }
    }

    [Fact]
    public void Admin_layout_links_the_desk_tokens_instead_of_defining_them_inline()
    {
        var layout = Web("Views/Shared/_AdminLayout.cshtml");
        Assert.Contains("css/desk-tokens.css", layout);
        Assert.DoesNotContain("--background:", layout);

        var css = File.ReadAllText(Path.Combine(Root(), "Blog.Web", "wwwroot", "css", "desk-tokens.css"));
        Assert.Contains(":root {", css);
        Assert.Contains("--background:", css);
        Assert.Contains("@media (prefers-color-scheme: dark)", css);
        Assert.Contains("html:not([data-theme=\"light\"])", css);
        Assert.Contains("html[data-theme=\"dark\"]", css);
        Assert.DoesNotContain("@@media", css); // Razor escape must not leak into a plain stylesheet
    }

    private static string Trim(string s) => Regex.Replace(s, @"\s+", " ")[..Math.Min(120, Regex.Replace(s, @"\s+", " ").Length)];
}
