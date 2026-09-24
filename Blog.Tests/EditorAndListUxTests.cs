using System.Text.RegularExpressions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Source guards for the 2026-09-19 editor and worklist pass. The post/page settings aside starts
/// hidden on phones behind a labelled toggle and opens as a full-screen sheet; the sidebar is grouped
/// into remembered <c>&lt;details&gt;</c> sections; placeholder-only fields carry a name; the two
/// read-only audit lists are searched and paged; the import screens keep every table scrollable,
/// confirm the run step (typed when anything is overwritten) and announce progress to screen readers.
/// </summary>
public class EditorAndListUxTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
    private static string Web(string rel) => File.ReadAllText(Path.Combine(Root(), "Blog.Web", rel.Replace('/', Path.DirectorySeparatorChar)));

    // ── Editors ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Views/Posts/Create.cshtml", "Post settings")]
    [InlineData("Views/Posts/Edit.cshtml", "Post settings")]
    [InlineData("Views/Pages/Create.cshtml", "Page settings")]
    [InlineData("Views/Pages/Edit.cshtml", "Page settings")]
    public void Editor_aside_starts_hidden_on_phones_behind_a_labelled_toggle(string view, string label)
    {
        var html = Web(view);
        var aside = Regex.Match(html, "<aside id=\"settingsPanel\"[^>]*>").Value;
        Assert.NotEmpty(aside);
        Assert.Contains("hidden md:flex", aside);           // desktop column unchanged, phones start closed
        Assert.Contains("fixed inset-0", aside);            // phones: a full-screen sheet, not a 100 px canvas
        Assert.Contains("md:static", aside);
        Assert.Contains("aria-labelledby=\"settingsPanelTitle\"", aside);

        var toggle = Regex.Match(html, "<button\\b[^>]*id=\"settingsToggle\"[^>]*>").Value;
        Assert.NotEmpty(toggle);
        Assert.Contains($"aria-label=\"{label}\"", toggle);
        Assert.Contains("aria-expanded=\"false\"", toggle);
        Assert.Contains("aria-controls=\"settingsPanel\"", toggle);
        Assert.Contains("md:hidden", toggle);

        var close = Regex.Match(html, "<button\\b[^>]*data-settings-close[^>]*>").Value;
        Assert.NotEmpty(close);
        Assert.Contains("aria-label=\"Close", close);
        Assert.Contains("window.toggleSettingsPanel = function", html);
        Assert.Contains("e.key !== 'Escape'", html);       // Escape closes the sheet
    }

    [Theory]
    [InlineData("Views/Posts/Create.cshtml")]
    [InlineData("Views/Posts/Edit.cshtml")]
    [InlineData("Views/Pages/Create.cshtml")]
    [InlineData("Views/Pages/Edit.cshtml")]
    public void Title_input_has_an_accessible_name(string view)
    {
        var title = Regex.Match(Web(view), "<input\\b[^>]*name=\"Title\"[^>]*>").Value;
        Assert.NotEmpty(title);
        Assert.Contains("aria-label=\"Title\"", title);
    }

    /// <summary>Posts group into Publishing · SEO and social · Answer blocks; pages have no answer blocks, so two.</summary>
    [Theory]
    [InlineData("Views/Posts/Create.cshtml", 3, "post")]
    [InlineData("Views/Posts/Edit.cshtml", 3, "post")]
    [InlineData("Views/Pages/Create.cshtml", 2, "page")]
    [InlineData("Views/Pages/Edit.cshtml", 2, "page")]
    public void Sidebar_is_grouped_into_remembered_details_sections(string view, int groups, string prefix)
    {
        var html = Web(view);
        var sections = Regex.Matches(html, "<details\\b[^>]*data-desk-section=\"([^\"]+)\"[^>]*>");
        Assert.Equal(groups, sections.Count);
        Assert.Equal(groups, Regex.Matches(html, "</details>").Count);
        Assert.Equal(groups, Regex.Matches(html, "<summary\\b").Count);

        var names = sections.Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Contains($"{prefix}-publishing", names);
        Assert.Contains($"{prefix}-seo", names);
        if (groups == 3) Assert.Contains("post-answers", names);

        // Publishing is the working section, open by default; the others start collapsed.
        Assert.Matches($"<details\\b[^>]*data-desk-section=\"{prefix}-publishing\"[^>]*\\bopen\\b", html);
        Assert.DoesNotMatch($"<details\\b[^>]*data-desk-section=\"{prefix}-seo\"[^>]*\\bopen\\b", html);

        // Open state is remembered per section, and storage access is guarded.
        Assert.Contains("details[data-desk-section]", html);
        Assert.Contains("localStorage.getItem", html);
        Assert.Contains("localStorage.setItem", html);
        Assert.Matches(new Regex("try \\{[^}]*localStorage\\.getItem"), html);
        Assert.Matches(new Regex("try \\{[^}]*localStorage\\.setItem"), html);

        // No leftover Alpine collapsible for the SEO block — the <details> replaced it.
        Assert.DoesNotContain("seoOpen", html);
    }

    [Fact]
    public void Every_roundup_entry_field_has_an_accessible_name()
    {
        var html = Web("Views/Posts/_RoundupEditor.cshtml");
        var offenders = Regex.Matches(html, "<(input|textarea|select)\\b[^>]*x-model(?:\\.number)?=\"e\\.[^\"]+\"[^>]*>")
            .Select(m => m.Value)
            .Where(t => !t.Contains("aria-label=") && !Regex.IsMatch(t, "x-model\\.number=\"e\\.scores\\.")) // sub-scores sit inside <label>
            .ToList();
        Assert.True(offenders.Count == 0, "Roundup entry fields without a name:\n" + string.Join('\n', offenders));
        Assert.Contains("aria-label=\"Move up\"", html);
        Assert.Contains("aria-label=\"Move down\"", html);
    }

    [Theory]
    [InlineData("Views/Posts/Create.cshtml")]
    [InlineData("Views/Posts/Edit.cshtml")]
    public void Answer_block_fields_have_accessible_names(string view)
    {
        var html = Web(view);
        foreach (var name in new[] { "Question", "Answer", "Fact label", "Fact value", "Step title", "What to do" })
            Assert.Contains($"aria-label=\"{name}\"", html);
        Assert.Contains("<label for=\"AnswerCapsule\"", html);
    }

    [Theory]
    [InlineData("Views/Posts/Create.cshtml")]
    [InlineData("Views/Posts/Edit.cshtml")]
    [InlineData("Views/Pages/Create.cshtml")]
    [InlineData("Views/Pages/Edit.cshtml")]
    public void Every_icon_only_editor_control_has_an_accessible_name(string view)
    {
        var html = Web(view);
        // Palette items and the block toolbar (a JS template literal) mirror their tooltip into aria-label.
        var titled = Regex.Matches(html, "<button\\b[^>]*\\btitle=\"[^\"]+\"[^>]*>").Select(m => m.Value).ToList();
        if (view != "Views/Posts/Create.cshtml") Assert.NotEmpty(titled); // the Quill editor has no block palette
        var unnamed = titled.Where(t => !t.Contains("aria-label=")).ToList();
        Assert.True(unnamed.Count == 0, "Icon-only buttons without aria-label:\n" + string.Join('\n', unnamed));
        if (view.StartsWith("Views/Posts/"))
        {
            Assert.Contains("aria-label=\"Remove category\"", html);
            Assert.Contains("aria-label=\"Remove tag\"", html);
        }
    }

    /// <summary>A literal "â€" is UTF-8 read as CP1252 — the same defect class the sqlcmd rule exists for.</summary>
    [Theory]
    [InlineData("Views/Posts/Create.cshtml")]
    [InlineData("Views/Posts/Edit.cshtml")]
    [InlineData("Views/Posts/_RoundupEditor.cshtml")]
    [InlineData("Views/Pages/Create.cshtml")]
    [InlineData("Views/Pages/Edit.cshtml")]
    public void No_editor_view_contains_mojibake(string view)
    {
        var html = Web(view);
        Assert.DoesNotContain("â€", html);
        Assert.DoesNotContain("â”", html);
    }

    // ── Read-only worklists ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Views/LinkAudit/Index.cshtml", "Controllers/LinkAuditController.cs")]
    [InlineData("Views/ContentHealth/Index.cshtml", "Controllers/ContentHealthController.cs")]
    public void Audit_lists_are_searched_paged_and_keep_their_filter(string view, string controller)
    {
        var html = Web(view);
        Assert.Contains("<partial name=\"_ListSearch\" />", html);
        Assert.Contains("<partial name=\"_ListPager\" />", html);
        Assert.Equal(Regex.Matches(html, "<table\\b").Count, Regex.Matches(html, "<div class=\"table-scroll\"><table\\b").Count);
        Assert.Contains("aria-current=\"@Current(", html);               // active filter tile is announced
        Assert.Contains("No matches for “@q”", html);                     // honest zero-result state

        var code = Web(controller);
        Assert.Contains("ListPaging.Apply(this,", code);
        Assert.Contains("string? q = null, int page = 1", code);
        Assert.Contains("ViewData[\"ListSearchKeep\"]", code);           // filter survives search and paging
        Assert.Contains("[\"filter\"] = filter", code);
    }

    // ── Import ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Views/Import/Preview.cshtml")]
    [InlineData("Views/Import/Summary.cshtml")]
    public void Import_tables_sit_inside_the_scroll_wrapper(string view)
    {
        var html = Web(view);
        var tables = Regex.Matches(html, "<table\\b").Count;
        Assert.True(tables > 0, view + " has no table");
        Assert.Equal(tables, Regex.Matches(html, "<div class=\"table-scroll\"><table\\b").Count);
    }

    [Fact]
    public void Import_run_is_a_confirmed_post_with_a_typed_guard_when_anything_is_overwritten()
    {
        var html = Web("Views/Import/Preview.cshtml");
        Assert.DoesNotContain("<a href=\"/admin/import/run/", html);        // no one-click GET into the run
        Assert.DoesNotContain("bg-emerald-600 text-white", html);            // tokens, not a hard-coded colour

        var forms = Regex.Matches(html, "<form\\b[^>]*action=\"/admin/import/run/@Model\\.Id\"[^>]*>", RegexOptions.Singleline)
            .Select(m => m.Value).ToList();
        Assert.True(forms.Count >= 1, "no POST form to the run action");
        foreach (var f in forms)
        {
            Assert.Contains("method=\"post\"", f);
            Assert.Contains("data-confirm-title=\"Run the import?\"", f);
            Assert.Contains("data-confirm-body=", f);
            Assert.Contains("data-confirm-action=\"Run import\"", f);
        }
        Assert.Contains(forms, f => f.Contains("data-confirm-typed=\"OVERWRITE\"") && f.Contains("data-confirm-tone=\"danger\""));
        Assert.Contains("p.Overwrites > 0", html);                           // the typed guard is conditional on overwrites
        Assert.Contains("@Html.AntiForgeryToken()", html);

        var controller = Web("Controllers/ImportController.cs");
        Assert.Matches(new Regex("\\[HttpPost\\(\"run/\\{id\\}\"\\)\\]\\s*\\[ValidateAntiForgeryToken\\]"), controller);
    }

    [Fact]
    public void Import_run_view_announces_progress_and_problems()
    {
        var html = Web("Views/Import/Run.cshtml");
        Assert.True(Regex.Matches(html, "aria-live=\"polite\"").Count >= 2, "phase/status and the problem log must both be live regions");
        var phaseRegion = Regex.Match(html, "<div\\b[^>]*aria-live=\"polite\"[^>]*>\\s*<span id=\"phase\"", RegexOptions.Singleline);
        Assert.True(phaseRegion.Success, "the phase/status line is not inside a live region");
        Assert.Matches(new Regex("<ul id=\"errLog\"[^>]*aria-live=\"polite\""), html);
        Assert.Contains(">Problems <", html);
        Assert.DoesNotContain("Exceptions", html);
    }
}
