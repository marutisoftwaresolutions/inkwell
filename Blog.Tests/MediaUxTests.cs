using System.Text.RegularExpressions;
using Blog.Web.Controllers;
using Blog.Web.Services;
using Blog.Web.Services.Jobs;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Source-level guards for the Media Library screen (2026-09-19 UX audit): actions reachable without a
/// mouse and on phones, named icon buttons, the upload flow protected by anti-forgery and showing the
/// Desk busy state, folder tabs that live in the query string, and the delete dialog that knows who
/// still embeds a file. Plus the pure age rule behind the nightly OG-card prune.
/// </summary>
public class MediaUxTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
    private static string Web(string rel) => File.ReadAllText(Path.Combine(Root(), "Blog.Web", rel.Replace('/', Path.DirectorySeparatorChar)));

    private static string View() => Web("Views/Media/Index.cshtml");
    private static string ControllerSource() => Web("Controllers/MediaController.cs");

    // ── H2: actions are not hover-only ──────────────────────────────────────────────

    [Fact]
    public void Tile_actions_are_revealed_on_keyboard_focus_and_always_shown_on_phones()
    {
        var html = View();
        var overlay = Regex.Match(html, "<div[^>]*group-hover:opacity-100[^>]*>");
        Assert.True(overlay.Success, "the action overlay should still use the hover reveal on wide screens");
        Assert.Contains("group-focus-within:opacity-100", overlay.Value); // Tab reaches the buttons and shows them
        Assert.Contains("md:opacity-0", overlay.Value);                   // hidden only from md up…
        Assert.Contains("opacity-100", overlay.Value);                    // …always visible under 768 px
        Assert.DoesNotContain(" opacity-0 ", overlay.Value);              // never unconditionally hidden
    }

    [Fact]
    public void Delete_and_copy_buttons_carry_an_accessible_name_with_the_file_name()
    {
        var html = View();
        Assert.Matches("<button[^>]*type=\"submit\"[^>]*aria-label=\"Delete @display\"", html);
        Assert.Matches("<button[^>]*data-copy-url=[^>]*aria-label=\"Copy URL of @display\"", html);
    }

    // ── H3: the upload flow ─────────────────────────────────────────────────────────

    [Fact]
    public void Upload_is_protected_by_anti_forgery_on_both_sides()
    {
        var controller = ControllerSource();
        var upload = controller[controller.IndexOf("[HttpPost(\"upload\")]", StringComparison.Ordinal)..];
        upload = upload[..upload.IndexOf("public async Task<IActionResult> Upload", StringComparison.Ordinal)];
        Assert.Contains("[ValidateAntiForgeryToken]", upload);
        Assert.DoesNotContain("//[ValidateAntiForgeryToken]", upload);

        var html = View();
        // The token is rendered on the page and travels with every fetch() as the standard header.
        Assert.Contains("@Html.AntiForgeryToken()", html);
        Assert.DoesNotContain("@* <!-- Upload Area -->@Html.AntiForgeryToken() *@", html);
        Assert.Contains("'RequestVerificationToken': token", html);
    }

    [Fact]
    public void Upload_script_drives_the_desk_busy_bar_and_disables_the_input_while_uploading()
    {
        var html = View();
        Assert.Contains("InkwellDesk.busy(", html);
        Assert.Contains("fileInput.disabled = on", html);
        Assert.Contains("if (uploading || !files.length) return;", html); // no double upload from a second drop
        // The server's reason reaches the operator; nobody is sent to the F12 console.
        Assert.Contains("await response.text()", html);
        Assert.Contains("InkwellDialog.toast('error'", html);
        Assert.DoesNotContain("Check F12 Console", html);
        Assert.DoesNotContain("Upload Complete!", html);
    }

    [Fact]
    public void Clipboard_failure_is_reported_not_swallowed()
    {
        var html = View();
        var copy = html[html.IndexOf("navigator.clipboard.writeText", StringComparison.Ordinal)..];
        Assert.Contains(".catch(", copy[..Math.Min(copy.Length, 400)]);
        Assert.Contains("if (!navigator.clipboard)", html); // http origins have no clipboard API at all
    }

    // ── H4: delete knows its references ─────────────────────────────────────────────

    [Fact]
    public void Delete_dialog_states_references_and_typed_guards_a_referenced_file()
    {
        var html = View();
        Assert.Contains("Not referenced by any post or page.", html);
        Assert.Contains("return \"Used in \" + string.Join(\"; \", parts) + \".\";", html);
        Assert.Contains("data-confirm-typed=\"@typed\"", html);
        Assert.Contains("var typed = used > 0 ? \"DELETE\" : null;", html);
        Assert.Contains("var tone = used > 0 ? \"danger\" : \"warning\";", html);

        var controller = ControllerSource();
        Assert.Contains("FindReferencesAsync(items)", controller);
        Assert.Contains("ViewData[\"MediaReferences\"]", controller);
    }

    [Fact]
    public void Reference_lookup_is_one_bounded_query_with_escaped_like_patterns()
    {
        var repo = File.ReadAllText(Path.Combine(Root(), "Blog.Infrastructure", "Data", "Repositories", "MediaRepository.cs"));
        var method = repo[repo.IndexOf("FindReferencesAsync", StringComparison.Ordinal)..];
        Assert.Contains("ESCAPE '\\'", method);
        Assert.Contains("JOIN Posts p", method);
        Assert.Contains("JOIN Pages g", method);
        Assert.Equal(1, Regex.Matches(method, @"conn\.QueryAsync<").Count); // one round trip for the whole page
    }

    // ── M5: folder tabs live in the query string ────────────────────────────────────

    [Fact]
    public void Folder_tabs_are_links_with_aria_current_and_the_controller_reads_folder_from_the_query()
    {
        var html = View();
        Assert.Contains("<nav aria-label=\"Folders\"", html);
        Assert.Contains("aria-current=\"@(active ? \"page\" : null)\"", html);
        Assert.DoesNotContain("x-data=\"{ tab:", html); // no client-side filter over one SQL page

        var controller = ControllerSource();
        Assert.Contains("Index(string? q, string? folder, int page = 1)", controller);
        Assert.Contains("ViewData[\"ListSearchKeep\"]", controller); // search and pager keep the tab
        Assert.Contains("_media.SearchAsync(userId, query, page, PageSize, folderKey)", controller);
        Assert.Contains("_media.CountSearchAsync(userId, query, folderKey)", controller);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("OG", "og")]
    [InlineData(" twitter ", "twitter")]
    [InlineData("images", "images")]
    [InlineData("../etc", "")]
    public void Folder_from_the_query_string_is_whitelisted(string? input, string expected)
    {
        Assert.Equal(expected, MediaController.NormalizeFolder(input));
    }

    [Fact]
    public void Upload_folder_falls_back_to_images_for_anything_unknown()
    {
        Assert.Equal("images", MediaController.NormalizeFolder("nope", "images"));
        Assert.Equal("og", MediaController.NormalizeFolder("og", "images"));
    }

    // ── M6: the file input is labelled and the copy matches what the server accepts ─

    [Fact]
    public void File_input_is_labelled_and_the_accept_list_and_size_copy_come_from_the_controller()
    {
        var html = View();
        Assert.Contains("<label for=\"fileInput\" class=\"sr-only\">", html);
        Assert.Contains("accept=\"@MediaController.AcceptAttribute\"", html);
        Assert.Contains("up to @MediaController.MaxUploadMegabytes MB each", html);
        Assert.DoesNotContain("SVG, PNG, JPG or GIF (max. 10MB)", html);

        foreach (var mime in new[] { "image/jpeg", "image/svg+xml", "video/mp4", "video/webm", "application/pdf",
                                     "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" })
            Assert.Contains(mime, MediaController.AcceptAttribute.Split(','));
        Assert.DoesNotContain("image/*", MediaController.AcceptAttribute); // the server never accepted every image type
        Assert.Equal(10L * 1024 * 1024, MediaController.MaxUploadBytes);
    }

    // ── Nits ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Headings_descend_in_order_below_the_layout_h1()
    {
        var html = View();
        var levels = Regex.Matches(html, "<h([1-6])\\b").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.NotEmpty(levels);
        Assert.DoesNotContain(1, levels);        // the layout owns the h1
        Assert.All(levels, l => Assert.Equal(2, l)); // both sections are h2; nothing skips to h3 first
    }

    // ── OG card cache is bounded ────────────────────────────────────────────────────

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(400, true)]
    [InlineData(0, false)]
    public void Og_cards_expire_at_thirty_days(int ageDays, bool expired)
    {
        var now = new DateTime(2026, 9, 19, 2, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expired, OgImageCache.IsExpired(now.AddDays(-ageDays), now, OgImageCache.MaxAge));
        Assert.Equal(TimeSpan.FromDays(30), OgImageCache.MaxAge);
    }

    [Fact]
    public void A_non_positive_max_age_never_expires_anything()
    {
        var now = DateTime.UtcNow;
        Assert.False(OgImageCache.IsExpired(now.AddYears(-5), now, TimeSpan.Zero));
    }

    [Fact]
    public void Invalidate_refuses_anything_that_is_not_a_single_slug_segment()
    {
        var root = Path.Combine(Path.GetTempPath(), "inkwell-og-" + Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, OgImageCache.FolderName);
        Directory.CreateDirectory(cache);
        try
        {
            File.WriteAllBytes(Path.Combine(cache, "hello.png"), new byte[] { 1 });
            Assert.False(OgImageCache.Invalidate(root, "../hello"));
            Assert.False(OgImageCache.Invalidate(root, ""));
            Assert.False(OgImageCache.Invalidate(root, "missing"));
            Assert.True(File.Exists(Path.Combine(cache, "hello.png")));
            Assert.True(OgImageCache.Invalidate(root, "hello"));
            Assert.False(File.Exists(Path.Combine(cache, "hello.png")));
            Assert.False(OgImageCache.Invalidate((string?)null, "hello")); // no web root: nothing to do, no throw
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Prune_removes_only_pngs_older_than_the_limit_and_survives_a_missing_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "inkwell-og-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(0, OgImageCache.Prune(root, DateTime.UtcNow, OgImageCache.MaxAge)); // folder absent

        var cache = Path.Combine(root, OgImageCache.FolderName);
        Directory.CreateDirectory(cache);
        try
        {
            var now = DateTime.UtcNow;
            var old = Path.Combine(cache, "old.png");
            var fresh = Path.Combine(cache, "fresh.png");
            var keep = Path.Combine(cache, ".gitkeep");
            File.WriteAllBytes(old, new byte[] { 1 });
            File.WriteAllBytes(fresh, new byte[] { 1 });
            File.WriteAllText(keep, "");
            File.SetLastWriteTimeUtc(old, now.AddDays(-31));
            File.SetLastWriteTimeUtc(fresh, now.AddDays(-1));
            File.SetLastWriteTimeUtc(keep, now.AddDays(-400));

            Assert.Equal(1, OgImageCache.Prune(root, now, OgImageCache.MaxAge));
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(fresh));
            Assert.True(File.Exists(keep)); // only *.png is ever touched
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Og_cache_prune_job_is_nightly_and_registered()
    {
        var job = new OgCachePruneJob(new FakeEnv(null));
        Assert.Equal("og-cache-prune", job.Name);
        Assert.Equal(TimeSpan.FromHours(24), job.Interval);
        Assert.Contains("OgCachePruneJob>", Web("Program.cs"));
    }

    private sealed class FakeEnv : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public FakeEnv(string? webRoot) { WebRootPath = webRoot!; }
        public string WebRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ApplicationName { get; set; } = "Blog.Web";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
    }
}
