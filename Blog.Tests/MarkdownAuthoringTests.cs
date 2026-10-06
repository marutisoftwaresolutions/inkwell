using System.Text.Json;
using System.Text.RegularExpressions;
using Blog.Core.Domain;
using Blog.Core.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Markdown authoring (ADR-0022): the renderer, the lossless switch from the visual editor, revisions,
/// the Ghost importer's Markdown detection, and the wiring that keeps HTML authoritative on the server.
/// </summary>
public class MarkdownAuthoringTests
{
    private readonly ITestOutputHelper _out;
    public MarkdownAuthoringTests(ITestOutputHelper output) => _out = output;

    private static string Squash(string html) => Regex.Replace(Regex.Replace(html, @">\s+<", "><"), @"\s+", " ").Trim();

    [Fact]
    public void Renders_commonmark_and_the_extensions_writers_expect()
    {
        var html = MarkdownRenderer.ToHtml(
            "## Choosing an EHR\n\nSome **bold**, *italic* and ~~struck~~ text — “quoted”.\n\n" +
            "| Plan | Price |\n|---|---|\n| Basic | $99 |\n\n- [x] done\n- [ ] todo\n\nA note[^1].\n\n[^1]: The footnote.\n\nSee https://example.com\n");
        Assert.Contains("<h2>Choosing an EHR</h2>", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<del>struck</del>", html);
        Assert.Contains("— “quoted”", html);
        Assert.Contains("<table>", html);
        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("footnote", html);
        Assert.Contains("<a href=\"https://example.com\">", html);
        Assert.DoesNotContain(" id=\"", html.Replace("id=\"fn", "").Replace("id=\"fnref", ""));     // headings get no ids here
        Assert.Equal("", MarkdownRenderer.ToHtml(null));
        Assert.Equal("", MarkdownRenderer.ToHtml("   "));
    }

    [Fact]
    public void Plaintext_is_the_readable_text()
    {
        Assert.Equal("Title A & B — done", MarkdownRenderer.ToPlaintext("<h2>Title</h2>\n<p>A &amp; B — <em>done</em></p>"));
        Assert.Equal("", MarkdownRenderer.ToPlaintext(null));
    }

    [Theory]
    [InlineData("<h2>Heading</h2>\n<p>Text with <strong>bold</strong> and a <a href=\"/x\">link</a>.</p>")]
    [InlineData("<div class=\"comp-hero\"><h2>Welcome</h2><p>A brief description.</p><a href=\"#\">Get Started</a></div>")]
    [InlineData("<p>Line one</p>\n<ul>\n<li>a</li>\n<li>b</li>\n</ul>\n<blockquote>Quote</blockquote>")]
    public void Visual_editor_html_survives_the_switch_to_markdown(string html) =>
        Assert.Equal(Squash(html), Squash(MarkdownRenderer.ToHtml(html)));

    [Fact]
    public void A_switch_that_would_change_the_post_is_detected()
    {
        Assert.True(MarkdownRenderer.RendersUnchanged("<h2>Fine</h2>\n<p>ok</p>"));
        // A blank line inside a block ends the HTML block; the indented line after it would become code.
        Assert.False(MarkdownRenderer.RendersUnchanged("<div>\n<p>a</p>\n\n    <p>b</p>\n</div>"));
    }

    [Fact]
    public void Editor_mode_is_rich_unless_markdown_is_asked_for()
    {
        Assert.Equal(EditorModes.Rich, new Post().EditorMode);
        Assert.Equal(EditorModes.Markdown, EditorModes.Normalize("Markdown"));
        Assert.Equal(EditorModes.Rich, EditorModes.Normalize("anything-else"));
        Assert.Equal(EditorModes.Rich, EditorModes.Normalize(null));
    }

    [Fact]
    public void Revisions_restore_the_markdown_source_and_old_revisions_restore_as_rich()
    {
        var post = new Post { Id = Guid.NewGuid(), Title = "T", Html = "<p>x</p>", EditorMode = EditorModes.Markdown, Markdown = "x" };
        var rev = RevisionSnapshot.FromPost(post, "Saved", Guid.NewGuid(), "me");
        var target = new Post { Id = post.Id, Html = "<p>changed</p>", EditorMode = EditorModes.Rich };
        RevisionSnapshot.ApplyToPost(rev, target);
        Assert.Equal(EditorModes.Markdown, target.EditorMode);
        Assert.Equal("x", target.Markdown);

        // A rich post's snapshot carries no Markdown keys, so it compares equal to revisions written before this release.
        var rich = RevisionSnapshot.FromPost(new Post { Id = post.Id, Title = "T", Html = "<p>x</p>" }, "Saved", Guid.NewGuid(), "me");
        Assert.DoesNotContain("Markdown", rich.StructuredJson);
        Assert.DoesNotContain("EditorMode", rich.StructuredJson);
        var restored = new Post { Id = post.Id, EditorMode = EditorModes.Markdown, Markdown = "old" };
        RevisionSnapshot.ApplyToPost(rich, restored);
        Assert.Equal(EditorModes.Rich, restored.EditorMode);
        Assert.Null(restored.Markdown);
    }

    // ── Ghost Markdown detection ────────────────────────────────────────────────

    private static string? GhostMarkdown(string postJson)
    {
        using var doc = JsonDocument.Parse(postJson);
        var type = typeof(Blog.Web.Services.Import.GhostImporter).Assembly.GetType("Blog.Web.Services.Import.GhostContent")!;
        return (string?)type.GetMethod("MarkdownSource")!.Invoke(null, new object[] { doc.RootElement });
    }

    private static string Quote(string json) => JsonSerializer.Serialize(json);

    [Fact]
    public void Ghost_markdown_posts_are_recognised_and_mixed_ones_are_not()
    {
        Assert.Equal("# Old", GhostMarkdown("{\"markdown\":\"# Old\"}"));

        var mobiledoc = "{\"version\":\"0.3.1\",\"markups\":[],\"atoms\":[],\"cards\":[[\"markdown\",{\"markdown\":\"**Hi**\"}],[\"markdown\",{\"markdown\":\"Two\"}]],\"sections\":[[10,0],[10,1]]}";
        Assert.Equal("**Hi**\n\nTwo", GhostMarkdown("{\"mobiledoc\":" + Quote(mobiledoc) + "}"));

        var mixed = "{\"version\":\"0.3.1\",\"markups\":[],\"atoms\":[],\"cards\":[[\"markdown\",{\"markdown\":\"x\"}]],\"sections\":[[10,0],[1,\"p\",[[0,[],0,\"text\"]]]]}";
        Assert.Null(GhostMarkdown("{\"mobiledoc\":" + Quote(mixed) + "}"));

        var lexical = "{\"root\":{\"children\":[{\"type\":\"markdown\",\"markdown\":\"- a\\n- b\"}],\"type\":\"root\"}}";
        Assert.Equal("- a\n- b", GhostMarkdown("{\"lexical\":" + Quote(lexical) + "}"));
        var lexicalMixed = "{\"root\":{\"children\":[{\"type\":\"markdown\",\"markdown\":\"x\"},{\"type\":\"paragraph\",\"children\":[]}],\"type\":\"root\"}}";
        Assert.Null(GhostMarkdown("{\"lexical\":" + Quote(lexicalMixed) + "}"));

        Assert.Null(GhostMarkdown("{\"html\":\"<p>x</p>\"}"));
        Assert.Null(GhostMarkdown("{\"mobiledoc\":\"not json\"}"));
    }

    // ── Wiring (source scans) ───────────────────────────────────────────────────

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void The_server_renders_markdown_posts_and_the_editor_fetches_nothing_from_a_cdn()
    {
        var ctrl = RepoFile("Blog.Web", "Controllers", "PostsController.cs");
        Assert.Contains("ApplyEditorMode(post, post.EditorMode);", ctrl);                 // create
        Assert.Contains("ApplyEditorMode(post, existingPost.EditorMode);", ctrl);         // edit: the stored mode, not the posted one
        Assert.DoesNotContain("AutoSave(", ctrl);                                         // the unprotected autosave endpoint is gone
        var posts = Regex.Matches(ctrl, @"\[HttpPost[^\]]*\]\s*(\[[^\]]+\]\s*)*public").Cast<Match>().ToList();
        Assert.All(posts, m => Assert.Contains("[ValidateAntiForgeryToken]", m.Value));

        var scripts = RepoFile("Blog.Web", "Views", "Posts", "_MarkdownEditorScripts.cshtml");
        Assert.Contains("autoDownloadFontAwesome: false", scripts);
        Assert.Contains("spellChecker: false", scripts);
        Assert.Contains("~/lib/easymde/easymde.min.js", scripts);
        Assert.DoesNotContain("cdn.", scripts);
        Assert.Contains("/admin/posts/markdown-preview", scripts);
        Assert.Contains("screenReaderLabel", scripts);                                   // the editing area has a name
        Assert.Contains("Tab: false", scripts);                                          // no keyboard trap
        Assert.Contains("ticket === previewSeq", scripts);                               // stale previews are dropped

        // easymde.min.css loads after admin.css, so the token overrides must out-rank it, and the hidden
        // visual-editor parts must stay hidden despite Tailwind's .flex.
        var css = RepoFile("Blog.Web", "wwwroot", "css", "admin.css");
        Assert.DoesNotMatch(new Regex(@"(?m)^\.EasyMDEContainer"), css);
        Assert.Contains("html .editor-toolbar.fullscreen", css);
        Assert.Contains("#componentPalette[hidden], #canvasScroll[hidden], #editor-container[hidden] { display: none !important; }", css);

        foreach (var view in new[] { "Edit.cshtml", "Create.cshtml" })
            Assert.Contains("<partial name=\"_EditorSwitchGuard\" />", RepoFile("Blog.Web", "Views", "Posts", view));

        var api = RepoFile("Blog.Web", "Controllers", "Api", "PostsApiController.cs");
        Assert.Contains("ApplyBody(existing, req.Content, req.Markdown);", api);
    }

    // ── Real content (dev database) ─────────────────────────────────────────────

    [Fact]
    public async Task Every_dev_post_switches_to_markdown_without_changing_its_html()
    {
        using var factory = new WebApplicationFactory<Program>();
        string? cs;
        try
        {
            cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            await probe.OpenAsync();
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return; }

        await using var conn = new SqlConnection(cs);
        var posts = (await conn.QueryAsync<(string Slug, string Html)>("SELECT Slug, Html FROM Posts WHERE Html IS NOT NULL AND EditorMode = 'rich'")).ToList();
        var changed = posts.Where(p => !MarkdownRenderer.RendersUnchanged(p.Html)).Select(p => p.Slug).ToList();
        _out.WriteLine($"{posts.Count} posts checked; {changed.Count} would change: {string.Join(", ", changed.Take(10))}");
        Assert.Empty(changed);
    }
}
