using System.Text.RegularExpressions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The Desk uses one styled dialog component instead of the browser's confirm / alert / prompt.
/// This scans every admin view and script so a native popup cannot creep back in, and checks that
/// every declarative confirmation carries real copy rather than a bare question.
/// </summary>
public class AdminDialogTests
{
    private static readonly Regex Native = new(@"(?<![\w.])(?:window\.)?(confirm|alert|prompt)\s*\(", RegexOptions.Compiled);

    // Files where a match is a comment or a name, not a native popup.
    private static readonly string[] Allowed =
    {
        "Views/Shared/_SlashCommandMenu.cshtml", // comment: "replaces browser prompt()"
    };

    [Fact]
    public void No_admin_view_or_script_calls_a_native_browser_popup()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        var files = Directory.EnumerateFiles(Path.Combine(root, "Blog.Web", "Views"), "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "Blog.Web", "wwwroot", "js"), "*.js", SearchOption.AllDirectories));

        foreach (var path in files)
        {
            var rel = Path.GetRelativePath(Path.Combine(root, "Blog.Web"), path).Replace('\\', '/');
            if (Allowed.Contains(rel)) continue;
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                foreach (Match m in Native.Matches(line))
                {
                    // Our own API and DOM method names are not the browser popups.
                    var before = line[..m.Index];
                    if (before.EndsWith("InkwellDialog.") || before.EndsWith("Dialog.")) continue;
                    if (m.Groups[1].Value == "confirm" && before.TrimEnd().EndsWith("function")) continue;
                    if (m.Groups[1].Value == "confirm" && Regex.IsMatch(before, @"(?:window\.)?confirmPublish$")) continue;
                    offenders.Add($"{rel}:{i + 1}: {line.Trim()}");
                }
            }
        }
        Assert.True(offenders.Count == 0, "Native browser popup found:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Every_declarative_confirmation_has_a_title_body_and_action()
    {
        var root = RepoRoot();
        var attr = new Regex(@"data-confirm-title=""[^""]+""", RegexOptions.Compiled);
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Blog.Web", "Views"), "*.cshtml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            foreach (Match m in attr.Matches(text))
            {
                // Look at the surrounding tag for the companion attributes.
                var start = text.LastIndexOf('<', m.Index);
                var end = text.IndexOf('>', m.Index);
                var tag = text[start..end];
                if (!tag.Contains("data-confirm-body=\"") || !tag.Contains("data-confirm-action=\""))
                    offenders.Add(Path.GetFileName(path) + ": " + m.Value);
                if (tag.Contains("data-confirm-title=\"Ensure to delete?\"") || tag.Contains("data-confirm-title=\"Are you sure?\""))
                    offenders.Add(Path.GetFileName(path) + ": vague title " + m.Value);
            }
        }
        Assert.True(offenders.Count == 0, "Confirmation without complete copy:\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// Any POST form whose action is destructive (delete, remove, block, spam, purge, clear, or a
    /// 410 retirement) must open the Desk dialog. Discovered from the views, not listed, so a new
    /// screen cannot ship a one-click delete.
    /// </summary>
    [Fact]
    public void Every_destructive_form_opens_the_confirmation_dialog()
    {
        var root = RepoRoot();
        var form = new Regex("<form\\b[^>]*>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var destructiveAction = new Regex("action=\"[^\"]*/(delete|remove|block|spam|purge|clear|reject)\\b", RegexOptions.IgnoreCase);
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Blog.Web", "Views"), "*.cshtml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            foreach (Match m in form.Matches(text))
            {
                if (!m.Value.Contains("method=\"post\"", StringComparison.OrdinalIgnoreCase)) continue;
                var isDestructive = destructiveAction.IsMatch(m.Value) || m.Value.Contains("data-destructive");
                if (!isDestructive)
                {
                    // A 410 "retire" is destructive for search even though the route is a plain save.
                    var end = text.IndexOf("</form>", m.Index, StringComparison.OrdinalIgnoreCase);
                    var body = end > m.Index ? text[m.Index..end] : "";
                    isDestructive = body.Contains("name=\"statusCode\" value=\"410\"");
                }
                if (isDestructive && !m.Value.Contains("data-confirm-title="))
                    offenders.Add(Path.GetRelativePath(root, path) + ": " + Regex.Replace(m.Value, "\\s+", " ")[..Math.Min(120, m.Value.Length)]);
            }
        }
        Assert.True(offenders.Count == 0, "Destructive forms without a confirmation dialog:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void The_layout_ships_the_dialog_and_its_script()
    {
        var root = RepoRoot();
        var layout = File.ReadAllText(Path.Combine(root, "Blog.Web", "Views", "Shared", "_AdminLayout.cshtml"));
        Assert.Contains("id=\"inkwell-dialog\"", layout);
        Assert.Contains("js/inkwell-dialog.js", layout);
        Assert.True(File.Exists(Path.Combine(root, "Blog.Web", "wwwroot", "js", "inkwell-dialog.js")));
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
