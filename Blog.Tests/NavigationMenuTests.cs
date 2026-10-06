using System.Net;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Controllers;
using Blog.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Navigation menus: a site with no menu renders the links it always did; a menu resolves to the right
/// paths in the right order, skips content that is gone, nests one level, and refuses links that would
/// be dangerous on every page.
/// </summary>
public class NavigationMenuTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public NavigationMenuTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    private static MenuItem Item(string type, int order, string? slug = null, string? title = null, Guid? parent = null,
        bool available = true, string? label = null, string? url = null) => new()
    {
        Id = Guid.NewGuid(), LinkType = type, SortOrder = order, TargetId = slug is null ? null : Guid.NewGuid(),
        TargetSlug = slug, TargetTitle = title, ParentId = parent, TargetAvailable = available, Label = label, Url = url,
    };

    // ── Backward compatibility ────────────────────────────────────────────────

    [Fact]
    public void With_no_menu_the_links_are_the_nav_pages_as_before()
    {
        var pages = new List<Page>
        {
            new() { Title = "Contact", Slug = "contact", SortOrder = 2 },
            new() { Title = "About", Slug = "about", SortOrder = 1 },
            new() { Title = "Editorial policy", Slug = "editorial-policy", SortOrder = 2 },
        };

        var links = NavigationMenu.FromPages(pages);

        // Same order the views used (OrderBy SortOrder, stable), same "/{slug}" hrefs, no nesting.
        Assert.Equal(["About", "Contact", "Editorial policy"], links.Select(l => l.Title));
        Assert.Equal(["/about", "/contact", "/editorial-policy"], links.Select(l => l.Href));
        Assert.All(links, l => { Assert.Equal(0, l.Depth); Assert.False(l.OpenInNewTab); });
    }

    // ── Resolution ────────────────────────────────────────────────────────────

    [Fact]
    public void Items_resolve_to_paths_with_children_under_their_parent()
    {
        var guides = Item(MenuLinkTypes.Category, 1, "guides", "Guides");
        var items = new List<MenuItem>
        {
            Item(MenuLinkTypes.Tag, 0, "ehr", "EHR", parent: guides.Id),       // child listed before its parent in storage
            guides,
            Item(MenuLinkTypes.Page, 0, "about", "About"),
            Item(MenuLinkTypes.Series, 2, "switching", "Switching EHR"),
            Item(MenuLinkTypes.Custom, 3, label: "Docs", url: "https://docs.example.com"),
            Item(MenuLinkTypes.Home, 4),
        };

        var links = NavigationMenu.Resolve(items);

        Assert.Equal(["/about", "/category/guides", "/tag/ehr", "/series/switching", "https://docs.example.com", "/"], links.Select(l => l.Href));
        Assert.Equal(["About", "Guides", "EHR", "Switching EHR", "Docs", "Home"], links.Select(l => l.Title));   // titles stay clean; views mark depth
        Assert.Equal([0, 0, 1, 0, 0, 0], links.Select(l => l.Depth));
    }

    [Fact]
    public void Content_that_is_gone_or_unpublished_is_skipped_with_its_children()
    {
        var draftPage = Item(MenuLinkTypes.Page, 0, "draft", "Draft", available: false);
        var items = new List<MenuItem>
        {
            draftPage,
            Item(MenuLinkTypes.Page, 0, "child", "Child", parent: draftPage.Id),
            Item(MenuLinkTypes.Category, 1, slug: null, title: null, available: false),   // deleted category
            Item(MenuLinkTypes.Page, 2, "about", "About", label: "Who we are"),
        };

        var links = NavigationMenu.Resolve(items);

        var only = Assert.Single(links);
        Assert.Equal("Who we are", only.Title);   // a label overrides the target's title
        Assert.Equal("/about", only.Href);
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/about")]
    [InlineData("/search?q=oct")]
    [InlineData("https://example.com/page")]
    [InlineData("http://example.com")]
    public void Safe_custom_links_are_accepted(string url) => Assert.Null(NavigationMenu.ValidateCustomUrl(url));

    [Theory]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("//evil.example.com")]
    [InlineData("/\\evil.example.com")]
    [InlineData("vbscript:msgbox")]
    [InlineData("about")]
    public void Dangerous_or_ambiguous_custom_links_are_refused(string url) => Assert.NotNull(NavigationMenu.ValidateCustomUrl(url));

    [Fact]
    public void Validation_catches_each_kind_of_mistake()
    {
        var top = Item(MenuLinkTypes.Page, 0, "a", "A");
        var child = Item(MenuLinkTypes.Page, 1, "b", "B", parent: top.Id);
        var grandchild = Item(MenuLinkTypes.Page, 2, "c", "C", parent: child.Id);
        var noTarget = new MenuItem { Id = Guid.NewGuid(), LinkType = MenuLinkTypes.Tag, SortOrder = 3 };
        var home = Item(MenuLinkTypes.Home, 4);
        var unlabelled = Item(MenuLinkTypes.Custom, 5, url: "https://x.example");

        var errors = NavigationMenu.Validate(MenuLocations.Header, [top, child, grandchild, noTarget, home, unlabelled]);

        Assert.Contains(errors, e => e.Index == 2 && e.Message.Contains("one level"));
        Assert.Contains(errors, e => e.Index == 3 && e.Message.Contains("tag"));
        Assert.Contains(errors, e => e.Index == 4 && e.Message.Contains("Home"));   // header already has Home
        Assert.Contains(errors, e => e.Index == 5 && e.Message.Contains("label"));
        Assert.DoesNotContain(errors, e => e.Index is 0 or 1);

        Assert.Empty(NavigationMenu.Validate(MenuLocations.Footer, [Item(MenuLinkTypes.Home, 0)]));   // fine in the footer
        Assert.NotEmpty(NavigationMenu.Validate(MenuLocations.Footer,
            Enumerable.Range(0, NavigationMenu.MaxItemsPerMenu + 1).Select(i => Item(MenuLinkTypes.Home, i)).ToList()));
    }

    [Fact]
    public void Editor_rows_become_nested_items_and_back()
    {
        var rows = new List<MenuRow>
        {
            new() { Type = MenuLinkTypes.Page, TargetId = Guid.NewGuid() },
            new() { Type = MenuLinkTypes.Tag, TargetId = Guid.NewGuid(), Child = true },
            new() { Type = MenuLinkTypes.Tag, TargetId = Guid.NewGuid(), Child = true },
            new() { Type = MenuLinkTypes.Custom, Url = "  /about  ", Label = " About ", Child = false },
            new() { Type = MenuLinkTypes.Page, TargetId = Guid.NewGuid(), Child = true },
        };

        var items = MenusController.ToItems(Guid.Empty, MenuLocations.Header, rows);

        Assert.Null(items[0].ParentId);
        Assert.Equal(items[0].Id, items[1].ParentId);
        Assert.Equal(items[0].Id, items[2].ParentId);
        Assert.Null(items[3].ParentId);
        Assert.Equal(items[3].Id, items[4].ParentId);
        Assert.Equal("/about", items[3].Url);
        Assert.Equal("About", items[3].Label);
        Assert.Equal([0, 1, 2, 3, 4], items.Select(i => i.SortOrder));

        var roundTrip = MenusController.ToRows(items);
        Assert.Equal(rows.Select(r => r.Child), roundTrip.Select(r => r.Child));
        Assert.Equal(rows.Select(r => r.TargetId), roundTrip.Select(r => r.TargetId));
    }

    [Fact]
    public void A_target_that_is_not_this_sites_content_is_refused()
    {
        var ownPage = Guid.NewGuid();
        var siteTargets = new Dictionary<string, List<MenuTarget>>
        {
            [MenuLinkTypes.Page] = [new MenuTarget(ownPage, "About", "/about")],
            [MenuLinkTypes.Tag] = [],
        };
        var rows = new List<MenuRow>
        {
            new() { Type = MenuLinkTypes.Page, TargetId = ownPage },
            new() { Type = MenuLinkTypes.Page, TargetId = Guid.NewGuid() },      // another tenant's page id
            new() { Type = MenuLinkTypes.Tag, TargetId = Guid.NewGuid() },
            new() { Type = MenuLinkTypes.Custom, Url = "/x", Label = "X" },      // no target to check
        };

        Assert.Equal(2, MenusController.MarkForeignTargets(rows, siteTargets));
        Assert.Null(rows[0].Error);
        Assert.NotNull(rows[1].Error);
        Assert.NotNull(rows[2].Error);
        Assert.Null(rows[3].Error);
    }

    // ── End to end (dev database) ─────────────────────────────────────────────

    [Fact]
    public async Task A_saved_menu_replaces_the_header_links_and_removing_it_restores_them()
    {
        if (!ProbeDb(out var cs)) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var repo = scope.ServiceProvider.GetRequiredService<IMenuRepository>();
        var menus = scope.ServiceProvider.GetRequiredService<MenuService>();
        var ownerId = (await users.GetFirstAdminAsync())?.Id ?? Guid.Empty;

        var existingHeader = await repo.GetItemsAsync(ownerId, MenuLocations.Header);
        var existingFooter = await repo.GetItemsAsync(ownerId, MenuLocations.Footer);
        if (existingHeader.Count > 0 || existingFooter.Count > 0) { _out.WriteLine("Dev site already has menus — skipping to avoid touching them."); return; }

        var marker = "menu-test-" + Guid.NewGuid().ToString("N")[..8];
        var client = _factory.CreateClient();
        try
        {
            var now = DateTime.UtcNow;
            await repo.ReplaceAsync(ownerId, MenuLocations.Header,
            [
                new() { Id = Guid.NewGuid(), OwnerId = ownerId, Location = MenuLocations.Header, SortOrder = 0,
                        LinkType = MenuLinkTypes.Custom, Url = "/" + marker, Label = "Header " + marker, CreatedAt = now, UpdatedAt = now },
            ]);
            await repo.ReplaceAsync(ownerId, MenuLocations.Footer,
            [
                new() { Id = Guid.NewGuid(), OwnerId = ownerId, Location = MenuLocations.Footer, SortOrder = 0,
                        LinkType = MenuLinkTypes.Custom, Url = "https://example.com/" + marker, Label = "Footer " + marker,
                        OpenInNewTab = true, CreatedAt = now, UpdatedAt = now },
            ]);
            menus.Invalidate(ownerId, MenuLocations.Header);
            menus.Invalidate(ownerId, MenuLocations.Footer);

            var html = await client.GetStringAsync("/");
            Assert.Contains($"href=\"/{marker}\"", html);
            Assert.Contains("aria-label=\"Footer\"", html);
            Assert.Contains($"href=\"https://example.com/{marker}\" target=\"_blank\" rel=\"noopener\"", html);
        }
        finally
        {
            await repo.ReplaceAsync(ownerId, MenuLocations.Header, []);
            await repo.ReplaceAsync(ownerId, MenuLocations.Footer, []);
            menus.Invalidate(ownerId, MenuLocations.Header);
            menus.Invalidate(ownerId, MenuLocations.Footer);
        }

        var restored = await client.GetStringAsync("/");
        Assert.DoesNotContain(marker, restored);
        Assert.DoesNotContain("aria-label=\"Footer\"", restored);   // no footer menu: nothing rendered
    }

    private bool ProbeDb(out string connectionString)
    {
        connectionString = "";
        try
        {
            var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs)) return false;
            using var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            conn.Open();
            connectionString = cs;
            return true;
        }
        catch (Exception ex) { _out.WriteLine($"DB probe failed: {ex.Message}"); return false; }
    }
}
