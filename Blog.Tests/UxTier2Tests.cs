using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>Source-level guards for the second UX tier: settings tabs, accessible names, bulk actions, comment moderation, dark scheme, site search.</summary>
public class UxTier2SourceTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
    private static string Web(string rel) => File.ReadAllText(Path.Combine(Root(), "Blog.Web", rel.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Settings_has_section_tabs_whose_anchors_all_exist()
    {
        var html = Web("Views/Settings/Index.cshtml");
        Assert.Contains("id=\"settings-tabs\"", html);
        foreach (var id in new[] { "s-general", "s-comments", "s-analytics", "s-social", "s-identity" })
        {
            Assert.Contains($"href=\"#{id}\"", html);
            Assert.Contains($"id=\"{id}\"", html);
        }
        Assert.Contains("scrollIntoView", html); // scroll-to-error
        Assert.DoesNotContain("rounded-xl shadow-sm overflow-hidden", html); // sticky needs no overflow clip on the card
    }

    [Fact]
    public void No_icon_only_button_or_link_lacks_an_accessible_name()
    {
        var root = Path.Combine(Root(), "Blog.Web", "Views");
        var iconOnly = new Regex(@"<(button|a)\b[^>]*>\s*<svg", RegexOptions.Compiled);
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            foreach (Match m in iconOnly.Matches(text))
            {
                var tag = m.Value[..m.Value.IndexOf('>')];
                if (tag.Contains("aria-label=") || tag.Contains("title=")) continue;
                // An svg followed by visible text is not icon-only; look past the svg for text before the closing tag.
                var after = text[(m.Index + m.Length)..];
                var close = after.IndexOf(m.Groups[1].Value == "a" ? "</a>" : "</button>", StringComparison.Ordinal);
                if (close < 0) continue;
                var inner = Regex.Replace(after[..close], "<svg.*?</svg>", "", RegexOptions.Singleline);
                inner = Regex.Replace(inner, "<[^>]+>", "").Trim();
                if (inner.Length == 0) offenders.Add(Path.GetRelativePath(root, path) + ": " + tag.Replace('\n', ' ')[..Math.Min(90, tag.Length)]);
            }
        }
        Assert.True(offenders.Count == 0, "Icon-only control without aria-label/title:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Posts_list_has_bulk_selection_wired_to_the_bulk_action()
    {
        var view = Web("Views/Posts/Index.cshtml");
        Assert.Contains("id=\"bulkForm\"", view);
        Assert.Contains("action=\"/admin/posts/bulk\"", view);
        Assert.Contains("form=\"bulkForm\"", view);
        Assert.Contains("id=\"bulkAll\"", view);
        var controller = Web("Controllers/PostsController.cs");
        Assert.Contains("[HttpPost(\"bulk\")]", controller);
        Assert.Contains("[ValidateAntiForgeryToken]", controller);
        Assert.Contains("HasClaim(\"Permission\", \"posts.publish\")", controller);
    }

    [Fact]
    public void Comment_moderation_can_mark_spam_and_reply_and_both_are_audited()
    {
        var view = Web("Views/Comments/Index.cshtml");
        Assert.Contains("/admin/comments/spam/", view);
        Assert.Contains("/admin/comments/reply/", view);
        var controller = Web("Controllers/CommentsController.cs");
        Assert.Contains("[HttpPost(\"spam/{id}\")]", controller);
        Assert.Contains("[HttpPost(\"reply/{id}\")]", controller);
        Assert.Contains("RegisterCommentSpamAsync(", controller);
        Assert.Contains("AuditActions.CommentMarkedSpam", controller);
        Assert.Contains("AuditActions.CommentReplied", controller);
    }

    [Fact]
    public void Dark_scheme_is_declared_for_both_token_namespaces_and_skips_dark_presets()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "Blog.Web", "wwwroot", "css", "inkwell.css"));
        Assert.Contains("prefers-color-scheme: dark", css);
        Assert.Contains("html[data-theme=\"dark\"] body.inkwell-root", css);
        Assert.Contains(":not([data-preset=\"ink\"]):not([data-preset=\"onyx\"])", css);
        var pub = Web("Views/Shared/_PublicLayout.cshtml");
        Assert.Contains("data-theme=\"@colorScheme\"", pub);
        Assert.Contains("prefers-color-scheme: dark", pub);
        var admin = Web("Views/Shared/_AdminLayout.cshtml");
        Assert.Contains("data-theme=", admin);
        Assert.Contains("prefers-color-scheme: dark", admin);
    }

    [Fact]
    public void Every_navbar_variant_carries_the_site_search()
    {
        var dir = Path.Combine(Root(), "Blog.Web", "Views", "Shared", "Components", "Navbar");
        var files = Directory.GetFiles(dir, "*.cshtml");
        Assert.True(files.Length >= 8, "expected the eight navbar variants");
        foreach (var f in files)
            Assert.True(File.ReadAllText(f).Contains("_NavSearch"), Path.GetFileName(f) + " has no search");
        Assert.Contains("ink-search-head", Web("Views/Blog/Index.cshtml"));
    }
}

/// <summary>Runtime checks that need the dev database: the search page, the comments queue, and the posts list with bulk controls.</summary>
public class UxTier2RenderTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public UxTier2RenderTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    [Fact]
    public async Task Search_page_echoes_the_query_with_a_count_and_is_noindex()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/search?q=software");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ink-search-head", html);
        Assert.Contains("software", html);
        Assert.Matches(new Regex(@"(\d+ results?|No results) for"), html);
        Assert.Contains("noindex", html);
        Assert.Contains("<title>Search: software", html);
        Assert.Contains("ink-nav-search", html); // header search present on public pages
    }

    [Fact]
    public async Task Public_html_carries_the_colour_scheme_attribute()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/");
        Assert.Matches(new Regex("<html[^>]*data-theme=\"(system|light|dark)\""), html);
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
