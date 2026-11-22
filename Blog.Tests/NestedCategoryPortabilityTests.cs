using System.Text;
using System.Xml.Linq;
using Blog.Core.Domain;
using Blog.Core.Services.Export;
using Blog.Web.Services.Import;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Nested categories survive the WordPress file in both directions: an export names each category's
/// parent and lists parents first; an import rebuilds the tree, bringing along a parent no post is
/// filed under. Ghost has no category tree (categories become flat tags), so nothing is claimed there.
/// </summary>
public class NestedCategoryPortabilityTests
{
    private static readonly XNamespace Wp = "http://wordpress.org/export/1.2/";
    private static readonly Guid AuthorId = Guid.NewGuid();
    private static readonly Guid Root = Guid.NewGuid(), Mid = Guid.NewGuid(), Leaf = Guid.NewGuid(), Other = Guid.NewGuid();

    private static async IAsyncEnumerable<T> Seq<T>(params T[] items)
    {
        foreach (var i in items) { await Task.Yield(); yield return i; }
    }

    private static async Task<string> ExportAsync()
    {
        // Deliberately listed child-first, as an ORDER BY Name would give it.
        var catalog = new ExportCatalog(
            new[]
            {
                new ExportTerm(Leaf, "Appointments", "appointments", Mid),
                new ExportTerm(Mid, "Front desk", "front-desk", Root),
                new ExportTerm(Other, "News", "news"),
                new ExportTerm(Root, "Running a practice", "running-a-practice"),
            },
            Array.Empty<ExportTerm>(),
            new[] { new ExportAuthor(AuthorId, "jane", "Jane Doe", "jane-doe", null, null, null, null, "Admin") },
            new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase));
        var post = new ExportPost
        {
            Id = Guid.NewGuid(), Title = "Booking screens compared", Slug = "booking-screens", Html = "<p>Body</p>",
            Status = PostStatus.Published, AuthorId = AuthorId, CategoryIds = new[] { Leaf },
            CreatedAtUtc = new DateTime(2026, 9, 1), UpdatedAtUtc = new DateTime(2026, 9, 1), PublishedAtUtc = new DateTime(2026, 9, 1),
            Seo = new ExportSeo(null, null, null, null, null, null, null, null, null),
        };
        var ms = new MemoryStream();
        await WxrExportWriter.WriteAsync(ms, new ExportSite("Example", "Guides", "https://blog.example.com", "en", TimeZoneInfo.Utc),
            catalog, Seq(post), Seq<ExportPage>(), new ExportOptions(), CancellationToken.None);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [Fact]
    public async Task An_export_names_each_parent_and_lists_parents_before_their_children()
    {
        var doc = XDocument.Parse(await ExportAsync());
        var categories = doc.Descendants(Wp + "category")
            .Select(c => (Slug: c.Element(Wp + "category_nicename")!.Value, Parent: c.Element(Wp + "category_parent")!.Value)).ToList();

        Assert.Equal(4, categories.Count);
        Assert.Equal("", categories.Single(c => c.Slug == "running-a-practice").Parent);
        Assert.Equal("running-a-practice", categories.Single(c => c.Slug == "front-desk").Parent);
        Assert.Equal("front-desk", categories.Single(c => c.Slug == "appointments").Parent);
        Assert.Equal("", categories.Single(c => c.Slug == "news").Parent);

        int At(string slug) => categories.FindIndex(c => c.Slug == slug);
        Assert.True(At("running-a-practice") < At("front-desk") && At("front-desk") < At("appointments"));
    }

    [Fact]
    public void A_missing_parent_or_a_loop_never_drops_or_repeats_a_category()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var orphan = Guid.NewGuid();
        var ordered = WxrExportWriter.ParentsFirst(new[]
        {
            new ExportTerm(a, "A", "a", b), new ExportTerm(b, "B", "b", a),          // a loop
            new ExportTerm(orphan, "Orphan", "orphan", Guid.NewGuid()),               // parent not in the list
        });
        Assert.Equal(3, ordered.Count);
        Assert.Equal(3, ordered.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public async Task An_import_rebuilds_the_tree_and_brings_along_parents_with_no_posts()
    {
        var file = Path.Combine(Path.GetTempPath(), "inkwell-nested-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            await File.WriteAllTextAsync(file, await ExportAsync(), new UTF8Encoding(false));
            var terms = new WordPressImporter().ReadRecords(file)
                .Where(r => r.Type == ImportItemType.Category).Select(r => (ParsedTerm)r.Payload).ToList();

            // The post is filed under the leaf only; its two ancestors come with it, topmost first. "News" has no post and no child, so it is not imported.
            Assert.Equal(new[] { "running-a-practice", "front-desk", "appointments" }, terms.Select(t => t.Slug).ToArray());
            Assert.Equal(new string?[] { null, "running-a-practice", "front-desk" }, terms.Select(t => t.ParentSlug).ToArray());
            Assert.Equal("Running a practice", terms[0].Name);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Ancestors_stop_at_a_missing_parent_and_never_circle()
    {
        var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["a"] = "b", ["b"] = "a", ["c"] = "gone" };
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["a"] = "A", ["b"] = "B" };
        Assert.Equal(new[] { "b", "a" }, WordPressImporter.WithAncestors("a", "A", parents, names).Select(t => t.Slug).ToArray());
        var withMissing = WordPressImporter.WithAncestors("c", "C", parents, names);
        Assert.Equal(new[] { "gone", "c" }, withMissing.Select(t => t.Slug).ToArray());   // named by its slug when the file never described it
        Assert.Equal("gone", withMissing[0].Name);
    }

    [Fact]
    public void An_import_places_only_categories_it_creates_and_checks_the_move()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        var processor = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Services", "Import", "ImportProcessor.cs"));
        var category = processor[processor.IndexOf("case ImportItemType.Category:", StringComparison.Ordinal)..processor.IndexOf("case ImportItemType.Tag:", StringComparison.Ordinal)];
        Assert.True(category.IndexOf("if (existing != null) return", StringComparison.Ordinal) < category.IndexOf("ParentSlug", StringComparison.Ordinal));   // an existing category is returned before any parent is looked at
        Assert.Contains("CategoryTree.CanPlaceUnder", category);
    }
}
