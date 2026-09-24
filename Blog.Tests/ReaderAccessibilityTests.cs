using System.Text.RegularExpressions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Source-level guards for the reader-surface accessibility pass of 2026-09-19: a skip link and
/// visible keyboard focus, no external hosts in the shared chrome (the product promises no CDN),
/// one search control per navbar, labelled comment forms that keep the reader's draft and land
/// them on the message, and a 404 that neither steals focus nor relies on a javascript: URL.
/// </summary>
public class ReaderAccessibilityTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string WebPath(string rel) => Path.Combine(Root(), "Blog.Web", rel.Replace('/', Path.DirectorySeparatorChar));
    private static string Web(string rel) => File.ReadAllText(WebPath(rel));

    [Fact]
    public void Public_layout_has_a_skip_link_that_targets_the_main_landmark()
    {
        var layout = Web("Views/Shared/_PublicLayout.cshtml");
        Assert.Contains("class=\"skip-link\"", layout);
        Assert.Contains("href=\"#main\"", layout);
        Assert.Contains("<main id=\"main\"", layout);

        // The skip link must be the first focusable thing in <body>, ahead of the navbar.
        var body = layout.IndexOf("<body", StringComparison.Ordinal);
        var skip = layout.IndexOf("class=\"skip-link\"", StringComparison.Ordinal);
        var navbar = layout.IndexOf("Component.InvokeAsync(\"Navbar\"", StringComparison.Ordinal);
        Assert.True(body >= 0 && skip > body && navbar > skip, "skip link must sit between <body> and the navbar");
    }

    [Fact]
    public void Inkwell_css_shows_keyboard_focus_and_hides_the_skip_link_until_focused()
    {
        var css = Web("wwwroot/css/inkwell.css");
        Assert.Matches(new Regex(@"(^|\n):focus-visible\s*\{[^}]*outline:\s*2px solid", RegexOptions.Multiline), css);
        Assert.Contains(".skip-link", css);
        Assert.Contains(".skip-link:focus", css);
        // Inputs that switch the native ring off must get it back under :focus-visible.
        Assert.Contains(".ink-input:focus-visible", css);
        Assert.Contains(".ink-subscribe-input:focus-visible", css);
    }

    [Fact]
    public void Shared_chrome_loads_no_external_image_script_or_font_host()
    {
        // Only tenant-opt-in analytics/captcha may reach an outside host, and only from the layout.
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "schema.org", "www.w3.org", "www.googletagmanager.com", "www.google.com",
        };
        var files = new List<string> { WebPath("Views/Shared/_PublicLayout.cshtml") };
        files.AddRange(Directory.EnumerateFiles(WebPath("Views/Shared/Components/Navbar"), "*.cshtml"));
        files.AddRange(Directory.EnumerateFiles(WebPath("Views/Shared/Components/Footer"), "*.cshtml"));
        Assert.True(files.Count >= 15, "expected the layout plus the navbar and footer variants");

        var external = new Regex(@"<(img|script|link|iframe|source|video|audio)\b[^>]*\b(src|href)=""https?://([^/""]+)", RegexOptions.IgnoreCase);
        var offenders = new List<string>();
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            Assert.DoesNotContain("placehold.co", text);
            foreach (Match m in external.Matches(text))
            {
                var host = m.Groups[3].Value;
                if (allowed.Contains(host)) continue;
                offenders.Add($"{Path.GetFileName(f)}: {host}");
            }
        }
        Assert.True(offenders.Count == 0, "External host in shared chrome:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Every_navbar_has_one_search_control_the_shared_one()
    {
        var files = Directory.EnumerateFiles(WebPath("Views/Shared/Components/Navbar"), "*.cshtml").ToArray();
        Assert.True(files.Length >= 8, "expected the eight navbar variants");
        foreach (var f in files)
        {
            var html = File.ReadAllText(f);
            Assert.DoesNotContain("searchOpen", html);          // the Alpine overlay is gone
            Assert.DoesNotContain("name=\"search\"", html);     // and its ?search= form with it
            Assert.Contains("_NavSearch", html);                // the shared /search?q= control remains
        }
    }

    [Fact]
    public void Icon_only_navbar_buttons_are_named()
    {
        foreach (var name in new[] { "Magazine", "Classic", "Grid", "Modern", "Default", "Feed" })
        {
            var html = Web($"Views/Shared/Components/Navbar/{name}.cshtml");
            Assert.Contains("aria-label=\"Open menu\"", html);
        }
    }

    [Fact]
    public void Comment_form_labels_point_at_their_controls_and_ids_are_unique_per_reply()
    {
        var post = Web("Views/Blog/Post.cshtml");
        var thread = Web("Views/Blog/_CommentThread.cshtml");

        foreach (var (file, text) in new[] { ("Post.cshtml", post), ("_CommentThread.cshtml", thread) })
        {
            var labels = Regex.Matches(text, @"<label\b[^>]*>");
            Assert.True(labels.Count > 0, file + " has no labels");
            foreach (Match l in labels)
                Assert.True(l.Value.Contains(" for=\""), file + ": label without for: " + l.Value);

            foreach (Match f in Regex.Matches(text, @"\bfor=""([^""]+)"""))
                Assert.Contains($"id=\"{f.Groups[1].Value}\"", text);
        }

        // Reply forms are repeated per comment, so every id carries the comment id.
        foreach (Match id in Regex.Matches(thread, @"\bid=""([^""]+)"""))
            Assert.Contains("@rid", id.Groups[1].Value);

        // The redirect target and the placed error.
        Assert.Contains("id=\"comments\"", post);
        Assert.Contains("role=\"alert\"", post);
        Assert.Contains("role=\"alert\"", thread);
        Assert.Contains("CommentDraftContent", post);
    }

    [Fact]
    public void AddComment_returns_to_the_comments_block_and_keeps_the_draft_on_error()
    {
        var src = Web("Controllers/BlogController.cs");
        var start = src.IndexOf("public async Task<IActionResult> AddComment(", StringComparison.Ordinal);
        var end = src.IndexOf("[HttpGet(\"feed\")]", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "AddComment action not found");
        var action = src[start..end];

        Assert.Contains("#comments", action);
        Assert.DoesNotContain("RedirectToAction(\"Post\"", action);   // every exit goes through BackToComments()
        Assert.Contains("TempData[\"CommentDraftName\"]", action);
        Assert.Contains("TempData[\"CommentDraftEmail\"]", action);
        Assert.Contains("TempData[\"CommentDraftContent\"]", action);
        Assert.Contains("TempData[\"CommentDraftParentId\"]", action);
    }

    [Fact]
    public void Neutral_post_body_is_capped_to_the_prose_measure_but_the_header_is_not()
    {
        var view = Web("Views/Blog/_Post_Neutral.cshtml");
        Assert.Contains("class=\"post-prose ink-measure-prose", view);
        Assert.Contains("<article class=\"max-w-4xl", view);
        Assert.Contains(".ink-measure-prose", Web("wwwroot/css/inkwell.css"));
    }

    [Fact]
    public void NotFound_does_not_steal_focus_and_has_no_javascript_url()
    {
        var view = Web("Views/Error/NotFound.cshtml");
        Assert.DoesNotContain("autofocus", view);
        Assert.DoesNotContain("javascript:", view);
        Assert.Contains("<button type=\"button\" class=\"ink-404__back\"", view);
        Assert.Contains("history.length > 1", view);
    }

    [Fact]
    public void Preview_banner_uses_tokens_not_hex_colours()
    {
        var post = Web("Views/Blog/Post.cshtml");
        var banner = Regex.Match(post, @"@if \(ViewBag\.IsPreview == true\)\s*\{(.*?)\}", RegexOptions.Singleline).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(banner), "preview banner not found");
        Assert.DoesNotMatch(new Regex(@"#[0-9a-fA-F]{3,8}\b"), banner);
    }
}
