using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Blog.Core.Domain;
using Blog.Core.Services.Export;
using Blog.Web.Services.Export;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// The content exporter (ADR-0020): WordPress WXR 1.2 and Ghost import JSON. Writers are checked by
/// parsing their output back; the fidelity matrix by its rules; the screen by source scans; and a full
/// export of the dev site end to end. The real-instance import proof (a running WordPress and Ghost)
/// is a manual step recorded in docs/plans/EXPORTER-PLAN.md.
/// </summary>
public class ContentExportTests
{
    private readonly ITestOutputHelper _out;
    public ContentExportTests(ITestOutputHelper output) => _out = output;

    private const string Origin = "https://blog.example.com";
    private static readonly XNamespace Wp = "http://wordpress.org/export/1.2/";
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    private static readonly Guid AuthorId = Guid.NewGuid(), CatId = Guid.NewGuid(), TagId = Guid.NewGuid(), SharedTagId = Guid.NewGuid();
    private static readonly Guid CommentId = Guid.NewGuid();

    private static ExportSite Site => new("Example — Blog", "Reviews “and” guides", Origin, "en", TimeZoneInfo.Utc);

    private static ExportCatalog Catalog(bool emails) => new(
        new[] { new ExportTerm(CatId, "Practice software", "practice-software"), new ExportTerm(Guid.NewGuid(), "Shared", "shared") },
        new[] { new ExportTerm(TagId, "EHR", "ehr"), new ExportTerm(SharedTagId, "Shared tag", "shared") },
        new[] { new ExportAuthor(AuthorId, "jane", "Jane Doe", "jane-doe", emails ? "jane@example.com" : null, "Optometrist", null, "/uploads/avatars/jane.jpg", "Admin") },
        new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase)
        {
            ["/uploads/features/hero.jpg"] = ("A practice front desk", "Photo: the author"),
        });

    private static ExportPost RichPost(PostStatus status = PostStatus.Published, DateTime? scheduled = null) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Best EHR — 2026 “guide”",
        Slug = "best-ehr-2026",
        Html = "<p>Body with an em dash — and a CDATA end ]]> marker.\u0001</p><img src=\"/uploads/2026/09/inline.png\" alt=\"x\"><img src=\"https://elsewhere.com/uploads/a.png\">",
        Status = status,
        CreatedAtUtc = new DateTime(2026, 9, 1, 8, 0, 0),
        UpdatedAtUtc = new DateTime(2026, 9, 2, 8, 0, 0),
        PublishedAtUtc = status == PostStatus.Published ? new DateTime(2026, 9, 1, 9, 30, 0) : null,
        ScheduledAtUtc = scheduled,
        AuthorId = AuthorId,
        FeatureImage = "/uploads/features/hero.jpg",
        Seo = new ExportSeo("Meta title", "Meta description", "/best-ehr-2026", null, null, "/uploads/features/hero.jpg", null, null, null),
        AnswerCapsule = "The best EHR depends on the size of your practice.",
        KeyFactsJson = "[{\"label\":\"Price\",\"value\":\"$299/month\"}]",
        FaqJson = "[{\"question\":\"Is it cloud based?\",\"answer\":\"Yes.\"}]",
        HowToJson = "[{\"name\":\"Pick\",\"text\":\"Choose a plan.\"}]",
        RoundupJson = "{\"entries\":[{\"rank\":1,\"name\":\"Acme EHR\",\"score\":9.1,\"bestFor\":\"Small practices\",\"pros\":[\"Fast\"],\"cons\":[\"Pricey\"],\"website\":\"https://acme.example\"}]}",
        CategoryIds = new[] { CatId },
        TagIds = new[] { TagId, SharedTagId },
        Series = new[] { new ExportSeriesRef("EHR basics", "ehr-basics", 2) },
        Comments = new[]
        {
            new ExportComment(CommentId, null, "Reader", "reader@example.com", null, "<p>Great — thanks</p>", true, new DateTime(2026, 9, 3)),
            new ExportComment(Guid.NewGuid(), CommentId, "Jane", "jane@example.com", null, "<p>Reply</p>", false, new DateTime(2026, 9, 4)),
        },
    };

    private static async IAsyncEnumerable<T> Seq<T>(params T[] items)
    {
        foreach (var i in items) { await Task.Yield(); yield return i; }
    }

    private static ExportPage AboutPage => new()
    {
        Id = Guid.NewGuid(), Title = "About", Slug = "about", Html = "<p>About us</p>", IsPublished = true,
        CreatedAtUtc = new DateTime(2026, 1, 1), UpdatedAtUtc = new DateTime(2026, 1, 1), AuthorId = AuthorId,
        Seo = new ExportSeo(null, null, null, null, null, null, null, null, null),
    };

    private static async Task<(XDocument Doc, ExportResult Result)> Wxr(ExportOptions o, params ExportPost[] posts)
    {
        var ms = new MemoryStream();
        var r = await WxrExportWriter.WriteAsync(ms, Site, Catalog(o.IncludeEmails), Seq(posts), Seq(AboutPage), o, CancellationToken.None);
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.False(text.StartsWith('﻿'), "WXR must be UTF-8 without a byte-order mark.");
        return (XDocument.Parse(text), r);
    }

    private static async Task<(JsonElement Data, JsonElement Meta, ExportResult Result, JsonDocument Doc)> Ghost(ExportOptions o, params ExportPost[] posts)
    {
        var ms = new MemoryStream();
        var r = await GhostExportWriter.WriteAsync(ms, Site, Catalog(o.IncludeEmails), Seq(posts), Seq(AboutPage), o, CancellationToken.None);
        var doc = JsonDocument.Parse(ms.ToArray());
        var db = doc.RootElement.GetProperty("db")[0];
        return (db.GetProperty("data"), db.GetProperty("meta"), r, doc);
    }

    // ── WordPress ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Wxr_is_a_file_the_wordpress_importer_accepts()
    {
        var (doc, result) = await Wxr(new ExportOptions(), RichPost());
        var channel = doc.Root!.Element("channel")!;
        Assert.Equal("1.2", channel.Element(Wp + "wxr_version")!.Value);
        Assert.Matches(@"^\d+\.\d+$", channel.Element(Wp + "wxr_version")!.Value);       // the importer's own check
        Assert.Equal(Origin, channel.Element(Wp + "base_site_url")!.Value);
        Assert.Equal(new[] { "practice-software", "shared" }, channel.Elements(Wp + "category").Select(c => c.Element(Wp + "category_nicename")!.Value));
        Assert.Equal("jane", channel.Element(Wp + "author")!.Element(Wp + "author_login")!.Value);

        var items = channel.Elements("item").ToList();
        var post = items.Single(i => i.Element(Wp + "post_type")!.Value == "post");
        Assert.Equal("Best EHR — 2026 “guide”", post.Element("title")!.Value);
        Assert.Equal("best-ehr-2026", post.Element(Wp + "post_name")!.Value);
        Assert.Equal("publish", post.Element(Wp + "status")!.Value);
        Assert.Equal("2026-09-01 09:30:00", post.Element(Wp + "post_date_gmt")!.Value);
        Assert.Equal("jane", post.Element(Dc + "creator")!.Value);
        Assert.Single(items, i => i.Element(Wp + "post_type")!.Value == "page");
        Assert.Equal(1, result.Posts);
        Assert.Equal(1, result.Pages);
    }

    [Fact]
    public async Task Wxr_body_keeps_text_exactly_and_renders_every_block()
    {
        var (doc, _) = await Wxr(new ExportOptions(), RichPost());
        var post = doc.Root!.Element("channel")!.Elements("item").First();
        var body = post.Element(Content + "encoded")!.Value;
        Assert.Contains("an em dash — and a CDATA end ]]> marker.", body);       // split CDATA rejoins exactly
        Assert.False(body.Contains('\u0001'));                                  // invalid XML characters dropped
        Assert.Contains("<strong>The best EHR depends", body);
        Assert.Contains("<h2>Key facts</h2>", body);
        Assert.Contains("$299/month", body);
        Assert.Contains("1. Acme EHR", body);
        Assert.Contains("rel=\"nofollow noopener\">Visit website</a>", body);
        Assert.Contains("<h2>Step by step</h2>", body);
        Assert.Contains("<h2>Frequently asked questions</h2>", body);
        Assert.Contains("Part 2 of the series “EHR basics”", body);
        Assert.Contains($"src=\"{Origin}/uploads/2026/09/inline.png\"", body);   // absolute, so WordPress can fetch and rewrite it
        Assert.Contains("src=\"https://elsewhere.com/uploads/a.png\"", body);   // another site's uploads untouched

        var meta = post.Elements(Wp + "postmeta").ToDictionary(m => m.Element(Wp + "meta_key")!.Value, m => m.Element(Wp + "meta_value")!.Value);
        Assert.Contains("_inkwell_faq", meta.Keys);
        Assert.Contains("_inkwell_roundup", meta.Keys);
        Assert.Equal("Meta title", meta["_yoast_wpseo_title"]);
        Assert.Equal("Meta title", meta["rank_math_title"]);
        Assert.Equal($"{Origin}/best-ehr-2026", meta["_yoast_wpseo_canonical"]);

        var terms = post.Elements("category").Select(c => (string)c.Attribute("domain")! + ":" + (string)c.Attribute("nicename")!).ToList();
        Assert.Contains("category:practice-software", terms);
        Assert.Contains("post_tag:ehr", terms);
        Assert.Contains("post_tag:series-ehr-basics", terms);
    }

    [Fact]
    public async Task Wxr_images_become_attachments_and_the_feature_image_is_linked()
    {
        var (doc, result) = await Wxr(new ExportOptions(), RichPost());
        var items = doc.Root!.Element("channel")!.Elements("item").ToList();
        var attachments = items.Where(i => i.Element(Wp + "post_type")!.Value == "attachment").ToList();
        Assert.Equal(2, attachments.Count);
        Assert.Equal(2, result.Attachments);
        var hero = attachments.Single(a => a.Element(Wp + "attachment_url")!.Value == Origin + "/uploads/features/hero.jpg");
        var post = items.First(i => i.Element(Wp + "post_type")!.Value == "post");
        var thumb = post.Elements(Wp + "postmeta").Single(m => m.Element(Wp + "meta_key")!.Value == "_thumbnail_id").Element(Wp + "meta_value")!.Value;
        Assert.Equal(hero.Element(Wp + "post_id")!.Value, thumb);
        Assert.Equal(post.Element(Wp + "post_id")!.Value, hero.Element(Wp + "post_parent")!.Value);
        Assert.Equal("A practice front desk", hero.Elements(Wp + "postmeta").Single().Element(Wp + "meta_value")!.Value);
        var ids = items.Select(i => i.Element(Wp + "post_id")!.Value).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Wxr_comments_keep_threading_and_never_carry_ips_or_unrequested_emails()
    {
        var (doc, _) = await Wxr(new ExportOptions(), RichPost());
        var comments = doc.Root!.Element("channel")!.Elements("item").First().Elements(Wp + "comment").ToList();
        Assert.Equal(2, comments.Count);
        Assert.Equal("1", comments[0].Element(Wp + "comment_approved")!.Value);
        Assert.Equal("0", comments[1].Element(Wp + "comment_approved")!.Value);
        Assert.Equal(comments[0].Element(Wp + "comment_id")!.Value, comments[1].Element(Wp + "comment_parent")!.Value);
        Assert.All(comments, c => Assert.Equal("", c.Element(Wp + "comment_author_email")!.Value));
        Assert.All(comments, c => Assert.Equal("", c.Element(Wp + "comment_author_IP")!.Value));
        Assert.Equal("", doc.Root!.Element("channel")!.Element(Wp + "author")!.Element(Wp + "author_email")!.Value);

        var (withEmails, _) = await Wxr(new ExportOptions { IncludeEmails = true }, RichPost());
        Assert.Equal("jane@example.com", withEmails.Root!.Element("channel")!.Element(Wp + "author")!.Element(Wp + "author_email")!.Value);

        var (noComments, r) = await Wxr(new ExportOptions { IncludeComments = false }, RichPost());
        Assert.Empty(noComments.Root!.Element("channel")!.Elements("item").First().Elements(Wp + "comment"));
        Assert.Equal(0, r.Comments);
    }

    [Theory]
    [InlineData(PostStatus.InReview, "pending")]
    [InlineData(PostStatus.NeedsChanges, "draft")]
    [InlineData(PostStatus.Draft, "draft")]
    public async Task Wxr_maps_unpublished_states(PostStatus status, string expected)
    {
        var (doc, _) = await Wxr(new ExportOptions { IncludeDrafts = true }, RichPost(status));
        Assert.Equal(expected, doc.Root!.Element("channel")!.Elements("item").First().Element(Wp + "status")!.Value);
    }

    // ── Ghost ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ghost_json_has_the_documented_shape()
    {
        var (data, meta, result, doc) = await Ghost(new ExportOptions(), RichPost());
        using var _doc = doc;
        Assert.True(meta.GetProperty("exported_on").GetInt64() > 1_700_000_000_000);            // epoch milliseconds
        Assert.Equal(GhostExportWriter.DeclaredGhostVersion, meta.GetProperty("version").GetString());

        var posts = data.GetProperty("posts").EnumerateArray().ToList();
        var post = posts.Single(p => p.GetProperty("type").GetString() == "post");
        Assert.Equal("published", post.GetProperty("status").GetString());
        Assert.Equal("2026-09-01T09:30:00.000Z", post.GetProperty("published_at").GetString());
        Assert.Equal("/content/images/features/hero.jpg", post.GetProperty("feature_image").GetString());
        var html = post.GetProperty("html").GetString()!;
        Assert.Contains("an em dash — and a CDATA end ]]> marker.", html);
        Assert.Contains("src=\"/content/images/2026/09/inline.png\"", html);
        Assert.Contains("<h2>Frequently asked questions</h2>", html);
        Assert.Contains("1. Acme EHR", html);
        Assert.Single(posts, p => p.GetProperty("type").GetString() == "page");

        var pm = data.GetProperty("posts_meta").EnumerateArray().Single(m => m.GetProperty("post_id").GetInt32() == post.GetProperty("id").GetInt32());
        Assert.Equal("Meta title", pm.GetProperty("meta_title").GetString());
        Assert.Equal("A practice front desk", pm.GetProperty("feature_image_alt").GetString());
        Assert.Equal("/content/images/features/hero.jpg", pm.GetProperty("og_image").GetString());
        Assert.Contains("/uploads/features/hero.jpg", result.UploadPaths);
        Assert.Contains("/uploads/2026/09/inline.png", result.UploadPaths);
        Assert.False(data.TryGetProperty("comments", out _));                                // Ghost cannot import comments
    }

    [Fact]
    public async Task Ghost_turns_categories_into_tags_and_series_into_internal_tags()
    {
        var (data, _, _, doc) = await Ghost(new ExportOptions(), RichPost());
        using var _d = doc;
        var tags = data.GetProperty("tags").EnumerateArray().ToList();
        var slugs = tags.Select(t => t.GetProperty("slug").GetString()).ToList();
        Assert.Contains("practice-software", slugs);
        Assert.Single(slugs, s => s == "shared");                                             // category and tag merged
        var series = tags.Single(t => t.GetProperty("slug").GetString() == "hash-series-ehr-basics");
        Assert.Equal("#series-ehr-basics", series.GetProperty("name").GetString());
        Assert.Equal("internal", series.GetProperty("visibility").GetString());
        var links = data.GetProperty("posts_tags").EnumerateArray().Select(x => (x.GetProperty("post_id").GetInt32(), x.GetProperty("tag_id").GetInt32())).ToList();
        Assert.Equal(links.Count, links.Distinct().Count());
        Assert.Equal(4, links.Count);                                                         // category, two tags, series
    }

    [Fact]
    public async Task Ghost_users_only_with_emails()
    {
        var (data, _, _, doc) = await Ghost(new ExportOptions(), RichPost());
        using (doc)
        {
            Assert.False(data.TryGetProperty("users", out _));
            Assert.False(data.TryGetProperty("posts_authors", out _));
        }

        var (withEmails, _, _, doc2) = await Ghost(new ExportOptions { IncludeEmails = true }, RichPost());
        using (doc2)
        {
            var user = withEmails.GetProperty("users")[0];
            Assert.Equal("jane@example.com", user.GetProperty("email").GetString());
            Assert.Equal("Administrator", user.GetProperty("roles")[0].GetString());
            Assert.Equal("/content/images/avatars/jane.jpg", user.GetProperty("profile_image").GetString());
            Assert.Equal(2, withEmails.GetProperty("posts_authors").GetArrayLength());       // the post and the page
        }
    }

    [Fact]
    public async Task Ghost_cuts_values_to_its_limits_and_never_invents_a_schedule()
    {
        var longPost = RichPost() with { Title = string.Join(' ', Enumerable.Repeat("word", 80)) };
        var past = RichPost(PostStatus.Scheduled, DateTime.UtcNow.AddDays(-1)) with { Slug = "past" };
        var future = RichPost(PostStatus.Scheduled, DateTime.UtcNow.AddDays(3)) with { Slug = "future" };
        var (data, _, result, doc) = await Ghost(new ExportOptions(), longPost, past, future);
        using var _d = doc;
        var posts = data.GetProperty("posts").EnumerateArray().ToList();
        Assert.True(posts[0].GetProperty("title").GetString()!.Length <= 255);
        Assert.True(result.Truncated >= 1);
        var p = posts.Single(x => x.GetProperty("slug").GetString() == "past");
        Assert.Equal("draft", p.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, p.GetProperty("published_at").ValueKind);
        Assert.Equal("scheduled", posts.Single(x => x.GetProperty("slug").GetString() == "future").GetProperty("status").GetString());
    }

    // ── Fidelity ────────────────────────────────────────────────────────────────

    [Fact]
    public void Fidelity_declares_losses_for_the_actual_selection()
    {
        var counts = new ExportCounts(Posts: 10, Pages: 2, Categories: 3, Tags: 5, Authors: 1, Comments: 4,
            WithRoundup: 2, WithFaq: 0, WithHowTo: 0, WithKeyFacts: 1, WithAnswerCapsule: 0, InSeries: 0, WithSeo: 6, Drafts: 0, InReview: 0);
        var ghost = ExportFidelity.For(ExportTarget.Ghost, counts, new ExportOptions());
        Assert.Contains(ghost, r => r.Item.Contains("comment") && r.Outcome == FidelityOutcome.Lost);
        Assert.Contains(ghost, r => r.Item.Contains("categor") && r.Outcome == FidelityOutcome.Changed);
        Assert.Contains(ghost, r => r.Item.Contains("author") && r.Outcome == FidelityOutcome.Lost);
        Assert.DoesNotContain(ghost, r => r.Item.Contains("FAQ"));                              // nothing to say about zero
        Assert.Contains(ghost, r => r.Item == "2 posts with a roundup");

        var wp = ExportFidelity.For(ExportTarget.WordPress, counts, new ExportOptions { IncludeEmails = true });
        Assert.Contains(wp, r => r.Item.Contains("comment") && r.Outcome == FidelityOutcome.Kept);
        Assert.Contains(wp, r => r.Item.Contains("author") && r.Outcome == FidelityOutcome.Kept);
        Assert.Contains(wp, r => r.Item.Contains("search or social") && r.Detail.Contains("Yoast"));
    }

    // ── Wiring (source scans) ───────────────────────────────────────────────────

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void Export_is_admin_only_audited_and_absent_in_cloud_mode()
    {
        var src = RepoFile("Blog.Web", "Controllers", "ExportController.cs");
        Assert.Contains("[Authorize(Policy = \"AdminOnly\")]", src);
        Assert.Matches(new Regex(@"\[HttpPost\(""download""\)\]\s*\[ValidateAntiForgeryToken\]"), src);
        foreach (var a in new[] { "ExportStarted", "ExportCompleted", "ExportFailed" }) Assert.Contains("AuditActions." + a, src);
        // Every action (download, and the push actions of ADR-0029) answers 404 in cloud mode.
        Assert.Equal(Regex.Matches(src, @"public async Task<IActionResult> \w+\(").Count,
            Regex.Matches(src, @"if \(_tenant\.IsCloudMode\) return NotFound\(\);").Count);
        var svc = RepoFile("Blog.Web", "Services", "Export", "ExportService.cs");
        Assert.Contains("FileOptions.DeleteOnClose", svc);
        Assert.Contains("App_Data", svc);
        Assert.DoesNotContain("WebRootPath, \"exports", svc);
    }

    [Fact]
    public async Task Custom_fields_become_wordpress_custom_fields_and_ghost_declares_the_loss()
    {
        var post = RichPost() with { CustomFieldsJson = "{\"price-from\":\"299\",\"trial\":\"true\"}" };
        var (doc, _) = await Wxr(new ExportOptions(), post);
        var meta = doc.Descendants("item").First(i => i.Element(Wp + "post_type")!.Value == "post")
            .Elements(Wp + "postmeta").ToDictionary(m => m.Element(Wp + "meta_key")!.Value, m => m.Element(Wp + "meta_value")!.Value);
        Assert.Equal("299", meta["price-from"]);
        Assert.Equal("true", meta["trial"]);

        var counts = new ExportCounts(1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, WithCustomFields: 1);
        var wp = ExportFidelity.For(ExportTarget.WordPress, counts, new ExportOptions()).Single(r => r.Item.Contains("custom fields"));
        var ghost = ExportFidelity.For(ExportTarget.Ghost, counts, new ExportOptions()).Single(r => r.Item.Contains("custom fields"));
        Assert.Equal(FidelityOutcome.Kept, wp.Outcome);
        Assert.Equal(FidelityOutcome.Lost, ghost.Outcome);
        Assert.DoesNotContain(ExportFidelity.For(ExportTarget.Ghost, counts with { WithCustomFields = 0 }, new ExportOptions()), r => r.Item.Contains("custom fields"));
    }

    [Fact]
    public void Exporter_never_reads_secrets()
    {
        var repo = RepoFile("Blog.Infrastructure", "Data", "Repositories", "ExportRepository.cs");
        foreach (var secret in new[] { "PasswordHash", "AuthorIp", "Settings", "Members", "JsonPayload" })
            Assert.DoesNotContain(secret, repo);
    }

    [Fact]
    public void The_wizard_states_what_the_real_imports_showed()
    {
        // Found by importing into WordPress 7.1.2 and Ghost 6.67.0 (EXPORTER-PLAN §11).
        var view = RepoFile("Blog.Web", "Views", "Export", "Index.cshtml");
        Assert.Contains("other than 80, 443 or 8080", view);      // WordPress refuses image downloads from other ports
        Assert.Contains("Running the import again is safe", view); // the WordPress importer skips posts it already has
        Assert.Contains("and only once", view);                    // Ghost copies every post on a second import
    }

    // ── End to end on the dev site ──────────────────────────────────────────────

    [Fact]
    public async Task The_dev_site_exports_to_both_formats()
    {
        using var factory = new WebApplicationFactory<Program>();
        try
        {
            var cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            await probe.OpenAsync();
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return; }

        using var scope = factory.Services.CreateScope();
        var export = scope.ServiceProvider.GetRequiredService<ExportService>();
        var options = new ExportOptions();
        var counts = await export.CountAsync(options);
        Assert.True(counts.Posts > 0);

        var wxr = await export.CreateAsync(ExportTarget.WordPress, options, "http://localhost:5099", CancellationToken.None);
        string xml;
        await using (wxr.Content) { using var reader = new StreamReader(wxr.Content, Encoding.UTF8); xml = await reader.ReadToEndAsync(); }
        var doc = XDocument.Parse(xml);
        var items = doc.Root!.Element("channel")!.Elements("item").ToList();
        Assert.Equal(counts.Posts, items.Count(i => i.Element(Wp + "post_type")!.Value == "post"));
        Assert.Equal(counts.Pages, items.Count(i => i.Element(Wp + "post_type")!.Value == "page"));
        Assert.Equal(0, Regex.Matches(xml, "â€").Count);
        _out.WriteLine($"WXR: {wxr.Result.Posts} posts, {wxr.Result.Pages} pages, {wxr.Result.Attachments} attachments, {xml.Length / 1024} KB");

        var ghost = await export.CreateAsync(ExportTarget.Ghost, options, "http://localhost:5099", CancellationToken.None);
        await using (ghost.Content)
        {
            var copy = new MemoryStream();
            await ghost.Content.CopyToAsync(copy);
            using var zip = new ZipArchive(copy, ZipArchiveMode.Read);
            var json = zip.Entries.Single(e => e.FullName.EndsWith(".json") && !e.FullName.Contains('/'));
            using var jd = await JsonDocument.ParseAsync(json.Open());
            var posts = jd.RootElement.GetProperty("db")[0].GetProperty("data").GetProperty("posts");
            Assert.Equal(counts.Posts + counts.Pages, posts.GetArrayLength());
            var images = zip.Entries.Count(e => e.FullName.StartsWith("content/images/"));
            Assert.Equal(ghost.Result.Attachments, images);
            Assert.True(images > 0);
            _out.WriteLine($"Ghost: {posts.GetArrayLength()} items, {images} images, {ghost.MissingImages} missing, {copy.Length / 1024} KB");
        }

        // For the manual real-instance proof (EXPORTER-PLAN §11): set INKWELL_EXPORT_DROP to a folder and the
        // exact artifacts the code produces are saved there, with author emails included.
        var drop = Environment.GetEnvironmentVariable("INKWELL_EXPORT_DROP");
        if (!string.IsNullOrWhiteSpace(drop))
        {
            Directory.CreateDirectory(drop);
            var withEmails = new ExportOptions { IncludeEmails = true };
            foreach (var target in new[] { ExportTarget.WordPress, ExportTarget.Ghost })
            {
                var f = await export.CreateAsync(target, withEmails, Environment.GetEnvironmentVariable("INKWELL_EXPORT_ORIGIN") ?? "http://localhost:5099", CancellationToken.None);
                await using (f.Content)
                await using (var outFile = File.Create(Path.Combine(drop, f.DownloadName)))
                    await f.Content.CopyToAsync(outFile);
                _out.WriteLine($"Saved {f.DownloadName}");
            }
        }
    }
}
