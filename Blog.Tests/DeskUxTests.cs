using System.Text.RegularExpressions;
using Blog.Web.Models;
using Xunit;

namespace Blog.Tests;

public class ListPagingTests
{
    private sealed record Row(string Name, string? Slug);

    private static readonly List<Row> Rows = Enumerable.Range(1, 60)
        .Select(i => new Row($"Item {i:00}", i % 10 == 0 ? null : $"item-{i}")).ToList();

    [Fact]
    public void Pages_at_the_default_size_and_clamps_out_of_range_pages()
    {
        var (rows, meta) = ListPaging.Page(Rows, null, 1, r => new[] { r.Name, r.Slug });
        Assert.Equal(ListPaging.DefaultPageSize, rows.Count);
        Assert.Equal(3, meta.TotalPages);
        Assert.Equal(60, meta.Total);

        var (last, lastMeta) = ListPaging.Page(Rows, null, 99, r => new[] { r.Name, r.Slug });
        Assert.Equal(3, lastMeta.Page);
        Assert.Equal(10, last.Count);

        var (first, firstMeta) = ListPaging.Page(Rows, null, 0, r => new[] { r.Name, r.Slug });
        Assert.Equal(1, firstMeta.Page);
        Assert.Equal("Item 01", first[0].Name);
    }

    [Fact]
    public void Search_is_case_insensitive_contains_across_the_named_fields_and_skips_null_fields()
    {
        var (rows, meta) = ListPaging.Page(Rows, "ITEM-3", 1, r => new[] { r.Name, r.Slug });
        Assert.Equal(new[] { "Item 03", "Item 31", "Item 32", "Item 33", "Item 34", "Item 35", "Item 36", "Item 37", "Item 38", "Item 39" }, rows.Select(r => r.Name));
        Assert.Equal("ITEM-3", meta.Query);
        Assert.Equal(1, meta.TotalPages);

        var (none, noneMeta) = ListPaging.Page(Rows, "zzz", 1, r => new[] { r.Name, r.Slug });
        Assert.Empty(none);
        Assert.Equal(1, noneMeta.TotalPages); // never zero pages — the pager still renders sanely
    }

    [Fact]
    public void Sql_paged_lists_describe_themselves_from_counts()
    {
        var meta = PagingMeta.FromCounts(" logo ", 3, 100, 48, 4);
        Assert.Equal("logo", meta.Query);
        Assert.Equal(3, meta.TotalPages);
        Assert.Equal(3, meta.Page);
        Assert.Equal(1, PagingMeta.FromCounts(null, 5, 0, 48, 0).TotalPages);
    }
}

/// <summary>The four critical Desk UX fixes cannot regress silently: every guarded view is scanned.</summary>
public class DeskUxSourceTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
    private static string View(string rel) => File.ReadAllText(Path.Combine(Root(), "Blog.Web", "Views", rel.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// Every editor in the tree, discovered rather than listed: any Create/Edit view plus the long
    /// settings form. A hard-coded list is how Series/Edit shipped untracked on 2026-09-15.
    /// </summary>
    [Fact]
    public void Every_editor_form_is_tracked_for_unsaved_changes_and_shows_a_pill()
    {
        var root = Path.Combine(Root(), "Blog.Web", "Views");
        var editors = Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p) is "Create.cshtml" or "Edit.cshtml")
            .Append(Path.Combine(root, "Settings", "Index.cshtml"))
            .ToList();
        Assert.True(editors.Count >= 6, "expected to discover the editors; found " + editors.Count);

        var offenders = new List<string>();
        foreach (var path in editors)
        {
            var html = File.ReadAllText(path);
            var forms = Regex.Matches(html, "<form\\b[^>]*method=\"post\"[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var untracked = forms.Count(f => !f.Value.Contains("data-track-changes"));
            if (forms.Count == 0 || untracked > 0 || !html.Contains("data-dirty-indicator"))
                offenders.Add($"{Path.GetRelativePath(root, path)}: {forms.Count} form(s), {untracked} untracked, pill={html.Contains("data-dirty-indicator")}");
        }
        Assert.True(offenders.Count == 0, "Editors without the unsaved-changes guard:\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// A scripted edit once replaced four button tags with a literal "$1" (an unexpanded regex
    /// backreference) and shipped to production, where the Settings Save button rendered as text.
    /// No Razor view has a legitimate reason to contain one.
    /// </summary>
    [Fact]
    public void No_view_contains_an_unexpanded_regex_backreference()
    {
        var root = Path.Combine(Root(), "Blog.Web", "Views");
        var offenders = Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories)
            .Select(p => (Path: Path.GetRelativePath(root, p), Text: File.ReadAllText(p)))
            .Where(f => Regex.IsMatch(f.Text, @"(^|[\s>""'])\$[1-9](?=[\s""'<>]|$)", RegexOptions.Multiline)) // $0 is a price on the landing page
            .Select(f => f.Path)
            .ToList();
        Assert.True(offenders.Count == 0, "Views containing a literal $N backreference:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Every_admin_table_sits_inside_a_horizontal_scroll_wrapper()
    {
        var root = Path.Combine(Root(), "Blog.Web", "Views");
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "Index.cshtml", SearchOption.AllDirectories))
        {
            var html = File.ReadAllText(path);
            var tables = Regex.Matches(html, "<table\\b").Count;
            var wrapped = Regex.Matches(html, "<div class=\"table-scroll\"><table\\b").Count;
            if (tables != wrapped) offenders.Add($"{Path.GetRelativePath(root, path)}: {tables} table(s), {wrapped} wrapped");
        }
        Assert.True(offenders.Count == 0, string.Join('\n', offenders));
    }

    [Theory]
    [InlineData("Pages/Index.cshtml")]
    [InlineData("Categories/Index.cshtml")]
    [InlineData("Tags/Index.cshtml")]
    [InlineData("Series/Index.cshtml")]
    [InlineData("Users/Index.cshtml")]
    [InlineData("Import/Index.cshtml")]
    [InlineData("Redirects/Index.cshtml")]
    [InlineData("Media/Index.cshtml")]
    public void Every_unbounded_list_has_search_and_a_pager(string view)
    {
        var html = View(view);
        Assert.Contains("_ListSearch", html);
        Assert.Contains("_ListPager", html);
    }

    [Fact]
    public void The_layout_ships_the_busy_bar_and_desk_script()
    {
        var layout = View("Shared/_AdminLayout.cshtml");
        Assert.Contains("id=\"desk-busy\"", layout);
        Assert.Contains("js/inkwell-desk.js", layout);
        Assert.True(File.Exists(Path.Combine(Root(), "Blog.Web", "wwwroot", "js", "inkwell-desk.js")));
        var css = File.ReadAllText(Path.Combine(Root(), "Blog.Web", "wwwroot", "css", "admin.css"));
        Assert.Contains(".table-scroll", css);
        Assert.Contains("#desk-busy", css);
    }
}
