using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Nested categories: the ordering and the rules for a move are pure and tolerate bad data; a parent's
/// public page lists the posts of the categories beneath it; deleting a parent moves its children up.
/// </summary>
public class CategoryTreeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public CategoryTreeTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static Category Cat(string name, Guid? parent = null) => new() { Id = Guid.NewGuid(), Name = name, Slug = name.ToLowerInvariant(), ParentId = parent };

    [Fact]
    public void Parents_come_before_their_children_and_each_level_is_by_name()
    {
        var tools = Cat("Tools"); var guides = Cat("Guides");
        var billing = Cat("Billing", tools.Id); var analytics = Cat("analytics", tools.Id);
        var invoices = Cat("Invoices", billing.Id);
        var ordered = CategoryTree.Order(new[] { invoices, tools, billing, guides, analytics });
        Assert.Equal(new[] { "Guides:0", "Tools:0", "analytics:1", "Billing:1", "Invoices:2" }, ordered.Select(n => $"{n.Category.Name}:{n.Depth}").ToArray());
    }

    [Fact]
    public void A_missing_parent_or_a_loop_never_loses_or_repeats_a_category()
    {
        var orphan = Cat("Orphan", Guid.NewGuid());
        var a = Cat("A"); var b = Cat("B", a.Id); a.ParentId = b.Id;      // a loop written straight into the database
        var self = Cat("Self"); self.ParentId = self.Id;
        var all = new[] { orphan, a, b, self };
        var ordered = CategoryTree.Order(all);
        Assert.Equal(4, ordered.Count);
        Assert.Equal(4, ordered.Select(n => n.Category.Id).Distinct().Count());
        Assert.Equal(0, ordered.First(n => n.Category.Id == orphan.Id).Depth);
        Assert.True(CategoryTree.Path(all, a.Id).Count <= 2);              // ends instead of circling
        Assert.DoesNotContain(a.Id, CategoryTree.DescendantIds(all, a.Id));
    }

    [Fact]
    public void Descendants_and_the_way_up_are_found_at_any_depth()
    {
        var root = Cat("Root"); var mid = Cat("Mid", root.Id); var leaf = Cat("Leaf", mid.Id); var other = Cat("Other");
        var all = new[] { root, mid, leaf, other };
        Assert.Equal(new[] { mid.Id, leaf.Id }.OrderBy(g => g), CategoryTree.DescendantIds(all, root.Id).OrderBy(g => g));
        Assert.Empty(CategoryTree.DescendantIds(all, leaf.Id));
        Assert.Equal(new[] { "Root", "Mid", "Leaf" }, CategoryTree.Path(all, leaf.Id).Select(c => c.Name).ToArray());
    }

    [Fact]
    public void A_move_is_refused_when_it_would_loop_point_nowhere_or_go_too_deep()
    {
        var root = Cat("Root"); var mid = Cat("Mid", root.Id); var leaf = Cat("Leaf", mid.Id); var other = Cat("Other");
        var all = new List<Category> { root, mid, leaf, other };

        Assert.True(CategoryTree.CanPlaceUnder(all, mid.Id, null));                 // to the top level: always
        Assert.True(CategoryTree.CanPlaceUnder(all, other.Id, leaf.Id));            // depth 3 is the limit
        Assert.False(CategoryTree.CanPlaceUnder(all, root.Id, root.Id));            // under itself
        Assert.False(CategoryTree.CanPlaceUnder(all, root.Id, leaf.Id));            // under its own descendant
        Assert.False(CategoryTree.CanPlaceUnder(all, other.Id, Guid.NewGuid()));    // a parent that does not exist

        var deep = Cat("Deep", leaf.Id); all.Add(deep);                             // Root > Mid > Leaf > Deep: depth 3
        Assert.False(CategoryTree.CanPlaceUnder(all, Guid.Empty, deep.Id));         // a new category beneath it would be depth 4
        Assert.False(CategoryTree.CanPlaceUnder(all, root.Id, other.Id));           // Root carries three levels: they would not fit
        Assert.True(CategoryTree.CanPlaceUnder(all, deep.Id, other.Id));
    }

    [Fact]
    public void The_desk_checks_every_move_and_audits_it()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        var controller = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Controllers", "CategoriesController.cs"));
        Assert.Equal(2, controller.Split("CategoryTree.CanPlaceUnder(").Length - 1);     // on create and on move
        Assert.Contains("AuditActions.CategoryMoved", controller);
        var view = File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Views", "Categories", "Index.cshtml"));
        Assert.Contains("Categories beneath it move up one level.", view);
        Assert.Contains("aria-label=\"Category that", view);
    }

    [Fact]
    public async Task A_parents_page_lists_its_subcategories_posts_and_deleting_it_moves_them_up()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var scope = _factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        if (admin is null) return;
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);

        var tag = Guid.NewGuid().ToString("N")[..8];
        var parent = new Category { Name = "zz parent " + tag, Slug = "zz-parent-" + tag, AuthorId = admin.Id };
        var postId = Guid.NewGuid();
        var title = "zz nested category post " + tag;
        try
        {
            parent.Id = await categories.CreateAsync(parent);
            var child = new Category { Name = "zz child " + tag, Slug = "zz-child-" + tag, AuthorId = admin.Id, ParentId = parent.Id };
            child.Id = await categories.CreateAsync(child);
            var grandchild = new Category { Name = "zz grandchild " + tag, Slug = "zz-grandchild-" + tag, AuthorId = admin.Id, ParentId = child.Id };
            grandchild.Id = await categories.CreateAsync(grandchild);

            await conn.ExecuteAsync(@"INSERT INTO Posts (Id, Title, Slug, Html, Plaintext, Status, Visibility, Type, AuthorId, PublishedAt, CreatedAt, UpdatedAt)
                                      VALUES (@postId, @title, @slug, N'<p>Body</p>', N'Body', N'Published', N'public', N'post', @author, DATEADD(MINUTE, -5, SYSUTCDATETIME()), SYSUTCDATETIME(), SYSUTCDATETIME());
                                      INSERT INTO PostCategories (PostId, CategoryId) VALUES (@postId, @cat);",
                new { postId, title, slug = "zz-nested-" + tag, author = admin.Id, cat = grandchild.Id });

            var client = _factory.CreateClient();
            var parentPage = await client.GetStringAsync("/category/" + parent.Slug);
            Assert.Contains(title, parentPage);                                    // filed two levels down, listed on the parent's page
            Assert.Contains("/category/" + child.Slug, parentPage);                // the category beneath is linked
            var leafPage = await client.GetStringAsync("/category/" + grandchild.Slug);
            Assert.Contains("/category/" + parent.Slug, leafPage);                 // and the way back up

            // The same trail as structured data: Home, parent, child, grandchild, in order, with absolute addresses.
            var crumbJson = System.Text.RegularExpressions.Regex.Matches(leafPage, "<script type=\"application/ld(?:[+]|&#x2B;)json\">(.*?)</script>", System.Text.RegularExpressions.RegexOptions.Singleline)
                .Select(m => m.Groups[1].Value).Single(j => j.Contains("\"BreadcrumbList\""));
            using var crumbDoc = System.Text.Json.JsonDocument.Parse(crumbJson);
            var crumbItems = crumbDoc.RootElement.GetProperty("itemListElement").EnumerateArray().ToList();
            Assert.Equal(new[] { 1, 2, 3, 4 }, crumbItems.Select(i => i.GetProperty("position").GetInt32()).ToArray());
            Assert.Equal(new[] { "Home", parent.Name, child.Name, grandchild.Name }, crumbItems.Select(i => i.GetProperty("name").GetString()).ToArray());
            Assert.All(crumbItems, i => Assert.StartsWith("http", i.GetProperty("item").GetString()));
            Assert.EndsWith("/category/" + grandchild.Slug, crumbItems[3].GetProperty("item").GetString());
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(parentPage, "\"BreadcrumbList\"").Count);   // exactly one per page

            Assert.Equal(parent.Id, (await categories.GetByIdAsync(child.Id, admin.Id))!.ParentId);
            await categories.DeleteAsync(child.Id, admin.Id);
            Assert.Equal(parent.Id, (await categories.GetByIdAsync(grandchild.Id, admin.Id))!.ParentId);   // moved up to where its parent was
            await categories.DeleteAsync(parent.Id, admin.Id);
            Assert.Null((await categories.GetByIdAsync(grandchild.Id, admin.Id))!.ParentId);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM Posts WHERE Id = @postId; DELETE FROM Categories WHERE Slug LIKE @p", new { postId, p = "zz-%-" + tag });
        }
    }
}
