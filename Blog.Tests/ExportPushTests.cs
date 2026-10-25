using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services.Export;
using Blog.Core.Services.Export.Push;
using Blog.Web.Services;
using Blog.Web.Services.Export.Push;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Direct push to WordPress and Ghost (ADR-0029): the pure rules, the request bodies, and the whole push run
/// against an in-memory stand-in for the target: skip, update and duplicate, resume, rate limits, a rejected
/// credential, a restart, retries, and the promise that no credential reaches a table or the audit trail.
/// </summary>
public class ExportPushTests : IDisposable
{
    private const string Secret = "s3cr3t-app-pass word";
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    // ── Address ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://www.example.com", "https://www.example.com")]
    [InlineData("https://www.example.com/", "https://www.example.com")]
    [InlineData("www.example.com", "https://www.example.com")]
    [InlineData("https://example.com/blog/", "https://example.com/blog")]
    [InlineData("https://example.com:8443", "https://example.com:8443")]
    public void Public_https_addresses_are_accepted_and_normalised(string input, string expected)
    {
        var (url, error) = PushRules.NormaliseSiteUrl(input, allowPrivate: false);
        Assert.Null(error);
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData("http://www.example.com")]
    [InlineData("https://localhost")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://10.0.0.5")]
    [InlineData("https://169.254.169.254")]
    [InlineData("https://intranet")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("https://example.com/?x=1")]
    [InlineData("ftp://example.com")]
    [InlineData("")]
    public void Unsafe_addresses_are_refused(string input)
    {
        var (url, error) = PushRules.NormaliseSiteUrl(input, allowPrivate: false);
        Assert.Null(url);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Testing_mode_allows_local_http_but_still_refuses_credentials_in_the_address()
    {
        Assert.Equal("http://127.0.0.1:9400", PushRules.NormaliseSiteUrl("http://127.0.0.1:9400/", allowPrivate: true).Url);
        Assert.Equal("http://localhost:2368", PushRules.NormaliseSiteUrl("http://localhost:2368", allowPrivate: true).Url);
        Assert.NotNull(PushRules.NormaliseSiteUrl("http://a:b@localhost", allowPrivate: true).Error);
    }

    // ── Ghost key and token ─────────────────────────────────────────────────────

    private const string GhostKey = "6489f2a1c0ffee0123456789:" + "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";

    [Fact]
    public void A_Ghost_admin_key_parses_and_a_content_key_does_not()
    {
        var (id, secret, error) = PushRules.ParseGhostKey(GhostKey);
        Assert.Null(error);
        Assert.Equal("6489f2a1c0ffee0123456789", id);
        Assert.Equal(32, secret!.Length);
        Assert.NotNull(PushRules.ParseGhostKey("22444f78447824223cefc48062").Error);      // a Content API key has no colon
        Assert.NotNull(PushRules.ParseGhostKey("abc:not-hex-at-all").Error);
        Assert.NotNull(PushRules.ParseGhostKey(" ").Error);
    }

    [Fact]
    public void The_Ghost_token_is_a_five_minute_HS256_JWT_for_the_admin_audience()
    {
        var (id, secret, _) = PushRules.ParseGhostKey(GhostKey);
        var token = PushRules.GhostToken(id!, secret!, new DateTimeOffset(Now));
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        static byte[] Unb64(string s) => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
        using var header = JsonDocument.Parse(Unb64(parts[0]));
        using var payload = JsonDocument.Parse(Unb64(parts[1]));
        Assert.Equal("HS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal(id, header.RootElement.GetProperty("kid").GetString());
        Assert.Equal("/admin/", payload.RootElement.GetProperty("aud").GetString());
        Assert.Equal(300, payload.RootElement.GetProperty("exp").GetInt64() - payload.RootElement.GetProperty("iat").GetInt64());
        var expected = HMACSHA256.HashData(secret!, Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]));
        Assert.Equal(expected, Unb64(parts[2]));
    }

    // ── Conflicts, retries, order ───────────────────────────────────────────────

    [Theory]
    [InlineData(false, PushConflictPolicy.Skip, PushAction.Create)]
    [InlineData(false, PushConflictPolicy.Update, PushAction.Create)]
    [InlineData(false, PushConflictPolicy.Duplicate, PushAction.Create)]
    [InlineData(true, PushConflictPolicy.Skip, PushAction.Skip)]
    [InlineData(true, PushConflictPolicy.Update, PushAction.Update)]
    [InlineData(true, PushConflictPolicy.Duplicate, PushAction.Create)]
    public void An_existing_slug_is_handled_by_the_policy(bool exists, PushConflictPolicy policy, PushAction expected) =>
        Assert.Equal(expected, PushRules.Decide(exists ? new RemoteItem("7", null) : null, policy));

    [Fact]
    public void Retry_after_reads_seconds_and_dates_with_bounds()
    {
        var now = new DateTimeOffset(Now);
        Assert.Equal(TimeSpan.FromSeconds(30), PushRules.RetryAfter("30", now));
        Assert.Equal(TimeSpan.FromMinutes(2), PushRules.RetryAfter(now.AddMinutes(2).ToString("r"), now));
        Assert.Equal(PushRules.DefaultRetryAfter, PushRules.RetryAfter(null, now));
        Assert.Equal(PushRules.DefaultRetryAfter, PushRules.RetryAfter("soon", now));
        Assert.Equal(PushRules.MaxRetryAfter, PushRules.RetryAfter("86400", now));
        Assert.Equal(TimeSpan.FromSeconds(5), PushRules.RetryAfter("0", now));
        Assert.True(PushRules.IsThrottle(429) && PushRules.IsThrottle(503) && !PushRules.IsThrottle(500));
        Assert.True(PushRules.IsAuthFailure(401) && PushRules.IsAuthFailure(403) && !PushRules.IsAuthFailure(404));
    }

    [Fact]
    public void Pages_go_first_with_parents_before_children_then_posts()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), post = Guid.NewGuid(), orphan = Guid.NewGuid();
        var order = PushRules.Order(new[]
        {
            new ExportItemRef(PushItemKind.Page, c, b, "Grandchild", "c"),
            new ExportItemRef(PushItemKind.Post, post, null, "Post", "p"),
            new ExportItemRef(PushItemKind.Page, b, a, "Child", "b"),
            new ExportItemRef(PushItemKind.Page, a, null, "Root", "a"),
            new ExportItemRef(PushItemKind.Page, orphan, Guid.NewGuid(), "Parent not pushed", "o"),
        });
        var keys = order.Select(o => o.SourceKey).ToList();
        Assert.True(keys.IndexOf(a.ToString()) < keys.IndexOf(b.ToString()));
        Assert.True(keys.IndexOf(b.ToString()) < keys.IndexOf(c.ToString()));
        Assert.Equal(post.ToString(), keys.Last());
        Assert.Equal(5, keys.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 5), order.Select(o => o.Seq));
    }

    [Fact]
    public void A_parent_cycle_still_sends_every_page_once()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var order = PushRules.Order(new[]
        {
            new ExportItemRef(PushItemKind.Page, a, b, "A", "a"),
            new ExportItemRef(PushItemKind.Page, b, a, "B", "b"),
        });
        Assert.Equal(2, order.Select(o => o.SourceKey).Distinct().Count());
    }

    [Fact]
    public void The_credential_never_prints()
    {
        var c = new PushCredential(ExportTarget.WordPress, "editor", Secret);
        Assert.DoesNotContain(Secret, c.ToString());
        Assert.DoesNotContain("editor", c.ToString());
    }

    // ── Bodies ──────────────────────────────────────────────────────────────────

    private static readonly ExportTerm News = new(Guid.NewGuid(), "News", "news");
    private static readonly ExportTerm Guides = new(Guid.NewGuid(), "Guides", "guides");
    private static readonly ExportTerm NewsTag = new(Guid.NewGuid(), "News", "news");

    private static ExportCatalog Catalog() => new(new[] { News }, new[] { Guides, NewsTag }, Array.Empty<ExportAuthor>(),
        new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase) { ["/uploads/2026/10/hero.webp"] = ("A lens", "Photo by us") });

    private static ExportPost Post(PostStatus status = PostStatus.Published, DateTime? scheduled = null, string slug = "hello") => new()
    {
        Id = Guid.NewGuid(), Title = "Hello — “world”", Slug = slug, Html = "<p>Body</p>", Status = status,
        CreatedAtUtc = Now.AddDays(-3), UpdatedAtUtc = Now.AddDays(-1), PublishedAtUtc = Now.AddDays(-2), ScheduledAtUtc = scheduled,
        CategoryIds = new[] { News.Id }, TagIds = new[] { Guides.Id, NewsTag.Id },
        Series = new[] { new ExportSeriesRef("Lens basics", "lens-basics", 2) },
        Seo = new ExportSeo(new string('t', 320), "desc", "https://canonical.example/x", null, null, null, null, null, null),
    };

    [Theory]
    [InlineData(PostStatus.Published, null, "publish", "published")]
    [InlineData(PostStatus.Scheduled, 3, "future", "scheduled")]
    [InlineData(PostStatus.Scheduled, -3, "draft", "draft")]
    [InlineData(PostStatus.Scheduled, null, "draft", "draft")]
    [InlineData(PostStatus.InReview, null, "pending", "draft")]
    [InlineData(PostStatus.Draft, null, "draft", "draft")]
    [InlineData(PostStatus.NeedsChanges, null, "draft", "draft")]
    public void Statuses_map_and_a_schedule_already_past_becomes_a_draft(PostStatus status, int? scheduledDays, string wp, string ghost)
    {
        var p = Post(status, scheduledDays is int d ? Now.AddDays(d) : null) with { PublishedAtUtc = status == PostStatus.Published ? Now.AddDays(-2) : null };
        Assert.Equal(wp, PushPayloads.PostStatusFor(p, ExportTarget.WordPress, Now).Status);
        Assert.Equal(ghost, PushPayloads.PostStatusFor(p, ExportTarget.Ghost, Now).Status);
    }

    [Fact]
    public void The_WordPress_post_body_carries_terms_media_and_a_zone_free_gmt_date()
    {
        var p = Post();
        var body = PushPayloads.WordPressPost(p, "<p>x</p>", "41", new[] { "3" }, new[] { "8", "9", "8" }, Now);
        Assert.Equal("publish", body["status"]!.GetValue<string>());
        Assert.Equal("2026-10-12T09:00:00", body["date_gmt"]!.GetValue<string>());
        Assert.Equal(41, body["featured_media"]!.GetValue<int>());
        Assert.Equal(new[] { 3 }, body["categories"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal(new[] { 8, 9 }, body["tags"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal("open", body["comment_status"]!.GetValue<string>());
    }

    [Fact]
    public void WordPress_terms_include_one_series_tag_and_no_duplicates()
    {
        var terms = PushPayloads.WordPressTerms(Post(), Catalog());
        Assert.Contains(terms, t => t is { Taxonomy: "categories", Slug: "news" });
        Assert.Contains(terms, t => t is { Taxonomy: "tags", Slug: "news" });   // a category and a tag may share a slug in WordPress
        Assert.Contains(terms, t => t is { Taxonomy: "tags", Slug: "series-lens-basics", Name: "Series: Lens basics" });
        Assert.Equal(terms.Count, terms.Select(t => t.Key).Distinct().Count());
    }

    [Fact]
    public void Ghost_tags_merge_categories_and_tags_by_slug_and_mark_series_internal()
    {
        var tags = PushPayloads.GhostTags(Post(), Catalog());
        Assert.Single(tags, t => t.Slug == "news");
        Assert.Contains(tags, t => t is { Name: "#series-lens-basics", Slug: "hash-series-lens-basics" });
    }

    [Fact]
    public void The_Ghost_body_respects_field_limits_and_sends_updated_at_only_when_updating()
    {
        var truncated = 0;
        var post = Post() with { Title = new string('x', 300) };
        var created = PushPayloads.GhostPost(post, "<p>x</p>", "https://g.example/content/images/a.webp", new string('a', 250), "cap",
            PushPayloads.GhostTags(post, Catalog()), null, null, null, Now, ref truncated)["posts"]![0]!;
        Assert.True(created["title"]!.GetValue<string>().Length <= 255);
        Assert.True(created["meta_title"]!.GetValue<string>().Length <= 300);
        Assert.True(created["feature_image_alt"]!.GetValue<string>().Length <= 191);
        Assert.Null(created["updated_at"]);
        Assert.True(truncated >= 3);
        Assert.Equal("https://canonical.example/x", created["canonical_url"]!.GetValue<string>());
        Assert.Equal("Hello — “world”", PushPayloads.GhostPost(Post(), "", null, null, null, Array.Empty<GhostTag>(), null, null, "2026-10-01T00:00:00.000Z", Now, ref truncated)
            ["posts"]![0]!["title"]!.GetValue<string>());   // UTF-8 survives untouched
        var updating = PushPayloads.GhostPost(Post(), "", null, null, null, Array.Empty<GhostTag>(), null, null, "2026-10-01T00:00:00.000Z", Now, ref truncated);
        Assert.Equal("2026-10-01T00:00:00.000Z", updating["posts"]![0]!["updated_at"]!.GetValue<string>());
    }

    [Fact]
    public void Push_fidelity_declares_what_the_APIs_do_not_take()
    {
        var counts = new ExportCounts(10, 2, 1, 2, 1, 5, 0, 1, 0, 0, 0, 1, 4, 0, 0, 2);
        var wp = PushRules_Fidelity(ExportTarget.WordPress, counts);
        Assert.Equal(FidelityOutcome.Lost, wp.Single(r => r.Item.Contains("comment")).Outcome);
        Assert.Equal(FidelityOutcome.Lost, wp.Single(r => r.Item.Contains("search or social")).Outcome);
        Assert.Equal(FidelityOutcome.Lost, wp.Single(r => r.Item.Contains("custom fields")).Outcome);
        Assert.Equal(FidelityOutcome.Changed, wp.Single(r => r.Item.Contains("author")).Outcome);
        var ghost = PushRules_Fidelity(ExportTarget.Ghost, counts);
        Assert.Equal(FidelityOutcome.Kept, ghost.Single(r => r.Item.Contains("search or social")).Outcome);
        Assert.Equal(FidelityOutcome.Changed, ghost.Single(r => r.Item.Contains("categor")).Outcome);
    }

    private static IReadOnlyList<FidelityRow> PushRules_Fidelity(ExportTarget t, ExportCounts c) => ExportFidelity.ForPush(t, c, new ExportOptions());

    // ── The push run, against an in-memory target ───────────────────────────────

    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "inkwell-push-" + Guid.NewGuid().ToString("N"));
    private readonly FakeSource _source = new();
    private readonly FakeRepo _repo = new();
    private readonly FakeTarget _target = new();
    private readonly FakeAudit _audit = new();
    private readonly PushCredentialStore _credentials = new();

    public ExportPushTests()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "uploads", "2026", "10"));
        File.WriteAllBytes(Path.Combine(_webRoot, "uploads", "2026", "10", "hero.webp"), new byte[] { 1, 2, 3 });
        var root = Guid.NewGuid();
        _source.Pages.Add(new ExportPage { Id = root, Title = "About", Slug = "about", Html = "<p>About</p>", IsPublished = true, CreatedAtUtc = Now });
        _source.Pages.Add(new ExportPage { Id = Guid.NewGuid(), ParentId = root, Title = "Team", Slug = "team", Html = "<p>Team</p>", IsPublished = true, CreatedAtUtc = Now });
        _source.Posts.Add(Post(slug: "one") with { Html = "<p><img src=\"/uploads/2026/10/hero.webp\" alt=\"\"></p>", FeatureImage = "/uploads/2026/10/hero.webp" });
        _source.Posts.Add(Post(slug: "two") with { Html = "<p><img src=\"https://source.example/uploads/2026/10/hero.webp\"><img src=\"/uploads/2026/10/missing.webp\"></p>" });
        _source.Posts.Add(Post(slug: "three"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_webRoot, true); } catch { /* temp */ }
    }

    private ExportPushService Service(int batch = 25, int itemSeconds = 50)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Exports:PushBatchSize"] = batch.ToString(), ["Exports:PushItemSeconds"] = itemSeconds.ToString() }).Build();
        var audit = new AuditService(_audit, new HttpContextAccessor(), new FakeTenant());
        return new ExportPushService(_repo, _source, new FakeFactory(_target), _credentials, audit, new FakeEnv(_webRoot), config,
            NullLogger<ExportPushService>.Instance);
    }

    private async Task<ExportPush> StartAsync(ExportTarget target = ExportTarget.WordPress, PushConflictPolicy policy = PushConflictPolicy.Skip)
    {
        var r = await Service().StartAsync(target, "https://dest.example", target == ExportTarget.WordPress ? "editor" : null,
            target == ExportTarget.WordPress ? Secret : GhostKey, policy, new ExportOptions { IncludePages = true }, "https://source.example",
            Guid.NewGuid(), "Admin", CancellationToken.None);
        Assert.Null(r.Error);
        return r.Push!;
    }

    [Fact]
    public async Task A_push_sends_pages_then_posts_uploads_each_image_once_and_finishes()
    {
        var push = await StartAsync();
        Assert.Equal(5, push.TotalCount);
        Assert.NotNull(_credentials.Get(push.Id));

        var report = await Service().RunAsync(CancellationToken.None);
        Assert.Equal(5, report.Sent);
        Assert.Equal(1, report.Finished);

        Assert.Equal(new[] { "page:about", "page:team", "post:one", "post:two", "post:three" }, _target.Saves.Select(s => s.Key));
        Assert.Equal(1, _target.Uploads);                                         // hero.webp once, though three references use it
        var team = _target.Items["page:team"];
        Assert.Equal(_target.Items["page:about"].Id, team.Body["parent"]!.GetValue<int>().ToString());
        var two = _target.Items["post:two"].Body["content"]!.GetValue<string>();
        Assert.Contains("https://dest.example/wp-content/uploads/hero.webp", two);   // absolute self-link rewritten to the upload
        Assert.Contains("https://source.example/uploads/2026/10/missing.webp", two); // missing file keeps pointing at this site
        Assert.Equal(_target.MediaId, _target.Items["post:one"].Body["featured_media"]!.GetValue<int>().ToString());
        Assert.Equal("A lens", _target.LastAlt);

        var done = await _repo.GetAsync(push.Id);
        Assert.Equal(PushStatus.Completed, done!.Status);
        Assert.Null(_credentials.Get(push.Id));                                   // forgotten once finished
        Assert.Contains(_audit.Entries, e => e.Action == AuditActions.ExportPushed && e.NewValues!.Contains("5 created"));
        Assert.Equal(3, _target.Terms.Count(t => t.StartsWith("tags:")));        // guides, news and the series tag, each once for three posts
        Assert.Equal(1, _target.Terms.Count(t => t.StartsWith("categories:")));
    }

    [Fact]
    public async Task Running_again_with_skip_creates_nothing_new_and_uploads_nothing()
    {
        await StartAsync();
        await Service().RunAsync(CancellationToken.None);
        var saves = _target.Saves.Count;
        var uploads = _target.Uploads;

        var second = await StartAsync();
        var report = await Service().RunAsync(CancellationToken.None);
        Assert.Equal(5, report.Skipped);
        Assert.Equal(saves, _target.Saves.Count);
        Assert.Equal(uploads, _target.Uploads);
        Assert.Equal(5, (await _repo.CountAsync(second.Id)).Skipped);
        Assert.All(_repo.Items.Where(i => i.PushId == second.Id && i.Kind is PushItemKind.Post or PushItemKind.Page),
            i => Assert.False(string.IsNullOrEmpty(i.RemoteId)));
    }

    [Fact]
    public async Task Update_overwrites_and_duplicate_adds_a_copy()
    {
        await StartAsync();
        await Service().RunAsync(CancellationToken.None);

        var update = await StartAsync(policy: PushConflictPolicy.Update);
        await Service().RunAsync(CancellationToken.None);
        Assert.Equal(5, (await _repo.CountAsync(update.Id)).Updated);
        Assert.Equal(5, _target.Saves.Count(s => s.Updated));

        var duplicate = await StartAsync(policy: PushConflictPolicy.Duplicate);
        await Service().RunAsync(CancellationToken.None);
        Assert.Equal(5, (await _repo.CountAsync(duplicate.Id)).Created);
        Assert.Equal(10, _target.Saves.Count(s => !s.Updated));
    }

    [Fact]
    public async Task Ghost_gets_tags_inline_and_the_version_stamp_on_update()
    {
        await StartAsync(ExportTarget.Ghost);
        await Service().RunAsync(CancellationToken.None);
        Assert.Empty(_target.Terms);
        var one = _target.Items["post:one"].Body["posts"]![0]!;
        Assert.Contains(one["tags"]!.AsArray(), t => t!["slug"]!.GetValue<string>() == "hash-series-lens-basics");
        Assert.Equal("https://dest.example/wp-content/uploads/hero.webp", one["feature_image"]!.GetValue<string>());

        await StartAsync(ExportTarget.Ghost, PushConflictPolicy.Update);
        await Service().RunAsync(CancellationToken.None);
        Assert.Equal("v1", _target.Saves.Last(s => s.Updated).Body["posts"]![0]!["updated_at"]!.GetValue<string>());
    }

    [Fact]
    public void Svg_is_never_uploaded_to_wordpress()
    {
        Assert.Null(PushRules.ImageContentType("/uploads/logo.svg", ExportTarget.WordPress));
        Assert.Equal("image/svg+xml", PushRules.ImageContentType("/uploads/logo.svg", ExportTarget.Ghost));
        Assert.Equal("image/webp", PushRules.ImageContentType("/uploads/a.webp", ExportTarget.WordPress));
    }

    [Fact]
    public async Task An_image_the_site_refuses_with_a_server_error_is_linked_here_and_the_post_still_goes()
    {
        await StartAsync();
        _target.FailUploads = new PushException("The site answered HTTP 500: rest_upload_unknown_error.", 500);
        await Service().RunAsync(CancellationToken.None);
        var one = _target.Items["post:one"].Body;
        Assert.Contains("/uploads/2026/10/hero.webp", one.ToJsonString());      // still points at this site
        Assert.Equal(0, _target.Uploads);
        Assert.True(_target.Saves.Count(s => !s.Updated) >= 2);                  // the posts were sent anyway
    }

    [Fact]
    public async Task An_item_past_its_deadline_counts_an_attempt_and_frees_the_scheduler()
    {
        var push = await StartAsync();
        _target.StallSaves = true;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Service(itemSeconds: 1).RunAsync(CancellationToken.None);
        Assert.InRange(clock.Elapsed.TotalSeconds, 0.5, 10);                     // returned, not hung
        var items = await _repo.ListContentItemsAsync(push.Id, null);
        Assert.Contains(items, i => i.Attempts == 1 && i.Error!.Contains("took too long"));
    }

    [Fact]
    public async Task A_batch_stops_at_the_limit_and_the_next_run_resumes()
    {
        var push = await StartAsync();
        await Service(batch: 2).RunAsync(CancellationToken.None);
        Assert.Equal(2, _target.Saves.Count);
        Assert.Equal(PushStatus.Running, (await _repo.GetAsync(push.Id))!.Status);
        await Service(batch: 2).RunAsync(CancellationToken.None);
        await Service(batch: 2).RunAsync(CancellationToken.None);
        Assert.Equal(5, _target.Saves.Count);
        Assert.Equal(5, _target.Saves.Select(s => s.Key).Distinct().Count());     // nothing sent twice
        Assert.Equal(PushStatus.Completed, (await _repo.GetAsync(push.Id))!.Status);
    }

    [Fact]
    public async Task A_rate_limit_pauses_the_push_until_retry_after_without_using_an_attempt()
    {
        var push = await StartAsync();
        _target.FailNextSave = new PushException("slow down", 429, TimeSpan.FromSeconds(90));
        await Service().RunAsync(CancellationToken.None);

        var p = await _repo.GetAsync(push.Id);
        Assert.Equal(PushStatus.Running, p!.Status);
        Assert.True(p.ResumeAt > DateTime.UtcNow.AddSeconds(60));
        Assert.All(_repo.Items.Where(i => i.PushId == push.Id), i => Assert.Equal(0, i.Attempts));   // a rate limit is not the item's fault

        await Service().RunAsync(CancellationToken.None);   // still waiting
        Assert.Empty(_target.Saves);
        p.ResumeAt = DateTime.UtcNow.AddSeconds(-1);
        await Service().RunAsync(CancellationToken.None);
        Assert.Equal(5, _target.Saves.Count);
    }

    [Fact]
    public async Task A_rejected_credential_pauses_the_push_forgets_it_and_resume_needs_it_again()
    {
        var push = await StartAsync();
        _target.FailNextSave = new PushException("WordPress did not accept the credential.", 401);
        var report = await Service().RunAsync(CancellationToken.None);
        Assert.Equal(1, report.Paused);
        Assert.Equal(PushStatus.Paused, (await _repo.GetAsync(push.Id))!.Status);
        Assert.Null(_credentials.Get(push.Id));
        Assert.Contains(_audit.Entries, e => e.Action == AuditActions.ExportPushPaused);

        var missing = await Service().ResumeAsync(push.Id, "editor", "", CancellationToken.None);
        Assert.Equal("pushSecret", missing.Field);
        var resumed = await Service().ResumeAsync(push.Id, "editor", Secret, CancellationToken.None);
        Assert.Null(resumed.Error);
        await Service().RunAsync(CancellationToken.None);
        Assert.Equal(PushStatus.Completed, (await _repo.GetAsync(push.Id))!.Status);
        Assert.Equal(5, _target.Saves.Count);
    }

    [Fact]
    public async Task After_a_restart_a_running_push_is_paused_for_the_operator()
    {
        var push = await StartAsync();
        _credentials.Forget(push.Id);   // what a new process looks like
        await Service().RunAsync(CancellationToken.None);
        var p = await _repo.GetAsync(push.Id);
        Assert.Equal(PushStatus.Paused, p!.Status);
        Assert.Contains("restarted", p.Message);
        Assert.Empty(_target.Saves);
    }

    [Fact]
    public async Task A_failing_item_is_retried_then_recorded_as_failed_and_the_rest_still_go()
    {
        var push = await StartAsync();
        _target.AlwaysFailSlug = "two";
        for (var i = 0; i < PushRules.MaxAttempts; i++)
        {
            await Service().RunAsync(CancellationToken.None);
            foreach (var item in _repo.Items.Where(x => x.Status == PushItemStatus.Pending)) item.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
        }
        var counts = await _repo.CountAsync(push.Id);
        Assert.Equal(1, counts.Failed);
        Assert.Equal(4, counts.Created);
        var failed = _repo.Items.Single(x => x.PushId == push.Id && x.Kind == PushItemKind.Post && x.Status == PushItemStatus.Failed);
        Assert.Contains(_repo.Items, x => x.Kind == PushItemKind.Media && x.Status == PushItemStatus.Failed && x.SourceKey.EndsWith("missing.webp"));
        Assert.Equal(PushRules.MaxAttempts, failed.Attempts);
        Assert.Contains("broken", failed.Error);
        Assert.Equal(PushStatus.Completed, (await _repo.GetAsync(push.Id))!.Status);
    }

    [Fact]
    public async Task Cancel_stops_pending_items_and_only_one_push_runs_at_a_time()
    {
        var push = await StartAsync();
        var second = await Service().StartAsync(ExportTarget.WordPress, "https://dest.example", "editor", Secret, PushConflictPolicy.Skip,
            new ExportOptions(), "https://source.example", Guid.NewGuid(), "Admin", CancellationToken.None);
        Assert.Null(second.Push);
        Assert.Contains("not finished", second.Error);

        Assert.True(await Service().CancelAsync(push.Id));
        Assert.Equal(PushStatus.Cancelled, (await _repo.GetAsync(push.Id))!.Status);
        Assert.Equal(5, (await _repo.CountAsync(push.Id)).Cancelled);
        Assert.Null(_credentials.Get(push.Id));
        Assert.False(await Service().CancelAsync(push.Id));
        await Service().RunAsync(CancellationToken.None);
        Assert.Empty(_target.Saves);
    }

    [Fact]
    public async Task A_failed_preflight_records_nothing()
    {
        _target.PreflightError = "WordPress did not accept the user name and application password.";
        var r = await Service().StartAsync(ExportTarget.WordPress, "https://dest.example", "editor", Secret, PushConflictPolicy.Skip,
            new ExportOptions(), "https://source.example", Guid.NewGuid(), "Admin", CancellationToken.None);
        Assert.Null(r.Push);
        Assert.Empty(_repo.Pushes);
        Assert.Empty(_audit.Entries);
        Assert.Equal("pushSite", r.Field);                 // with no field named, the address is the likely culprit

        _target.PreflightField = "pushSecret";
        var bad = await Service().StartAsync(ExportTarget.WordPress, "https://dest.example", "editor", Secret, PushConflictPolicy.Skip,
            new ExportOptions(), "https://source.example", Guid.NewGuid(), "Admin", CancellationToken.None);
        Assert.Equal("pushSecret", bad.Field);             // shown at the password, not in a banner
    }

    [Fact]
    public async Task No_credential_reaches_a_table_or_the_audit_trail()
    {
        _target.FailNextSave = new PushException("WordPress did not accept the credential.", 401);
        var push = await StartAsync();
        await Service().RunAsync(CancellationToken.None);
        await Service().ResumeAsync(push.Id, "editor", Secret, CancellationToken.None);
        await Service().RunAsync(CancellationToken.None);
        await StartAsync(ExportTarget.Ghost);
        await Service().RunAsync(CancellationToken.None);

        var stored = JsonSerializer.Serialize(new { _repo.Pushes, _repo.Items }) + JsonSerializer.Serialize(_audit.Entries);
        Assert.DoesNotContain(Secret, stored);
        Assert.DoesNotContain(GhostKey.Split(':')[1], stored);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("editor:" + Secret)), stored);
    }

    [Fact]
    public void Source_scan_push_is_admin_only_anti_forgery_protected_and_never_in_cloud_mode()
    {
        var src = File.ReadAllText(RepoFile("Blog.Web", "Controllers", "ExportController.cs"));
        Assert.Contains("[Authorize(Policy = \"AdminOnly\")]", src);
        foreach (var action in new[] { "Push(", "ResumePush(", "CancelPush(" })
        {
            var at = src.IndexOf($"public async Task<IActionResult> {action}", StringComparison.Ordinal);
            Assert.True(at > 0, action);
            var head = src[Math.Max(0, at - 120)..at];
            Assert.Contains("[ValidateAntiForgeryToken]", head);
            Assert.Contains("_tenant.IsCloudMode", src[at..(at + 600)]);
        }
        var view = File.ReadAllText(RepoFile("Blog.Web", "Views", "Export", "Index.cshtml"));
        // A one-shot action, not an editor: no unsaved-changes tracking (v1.0.9 UX review).
        Assert.Contains("id=\"push-form\" class=\"flex flex-col gap-4\" autocomplete=\"off\"", view);
        Assert.DoesNotContain("id=\"push-form\" class=\"flex flex-col gap-4\" autocomplete=\"off\" data-track-changes", view);
        Assert.Equal(System.Text.RegularExpressions.Regex.Matches(view, "aria-invalid=\"@Invalid").Count,
            System.Text.RegularExpressions.Regex.Matches(view, "autofocus=\"@Focus").Count);   // the rejected field takes focus
        Assert.DoesNotContain("Exports:Push", view);                                        // no configuration keys in the Desk
        Assert.Contains("HX-Refresh", src);                                                 // a finished push reloads its page
        var job = File.ReadAllText(RepoFile("Blog.Web", "Program.cs"));
        Assert.Contains("ExportPushJob", job);
        Assert.Contains("AddSingleton<Blog.Web.Services.Export.Push.PushCredentialStore>", job);
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    // ── Fakes ───────────────────────────────────────────────────────────────────

    private sealed class FakeTarget
    {
        public readonly Dictionary<string, (string Id, JsonObject Body)> Items = new();
        public readonly List<(string Key, bool Updated, JsonObject Body)> Saves = new();
        public readonly List<string> Terms = new();
        public int Uploads;
        public string MediaId = "501";
        public string? LastAlt;
        public string? PreflightError;
        public string? PreflightField;
        public PushException? FailNextSave;
        public string? AlwaysFailSlug;
        public PushException? FailUploads;
        public bool StallSaves;
        private int _next = 100;
        public string NextId() => (++_next).ToString();
    }

    private sealed class FakeClient : IPushClient
    {
        private readonly FakeTarget _t;
        private readonly bool _ghost;
        public FakeClient(FakeTarget t, bool ghost) { _t = t; _ghost = ghost; }
        private static string Key(PushItemKind k, string slug) => (k == PushItemKind.Page ? "page:" : "post:") + slug;

        public Task<PushPreflight> PreflightAsync(bool needPages, CancellationToken ct) =>
            Task.FromResult(_t.PreflightError is null ? new PushPreflight(true, "Dest", "6.0", null) : new PushPreflight(false, null, null, _t.PreflightError, _t.PreflightField));

        public Task<RemoteItem?> FindAsync(PushItemKind kind, string slug, CancellationToken ct) =>
            Task.FromResult(_t.Items.TryGetValue(Key(kind, slug), out var hit) ? new RemoteItem(hit.Id, "https://dest.example/" + slug, "v1") : null);

        public Task<RemoteItem> SaveAsync(PushItemKind kind, JsonObject body, RemoteItem? existing, CancellationToken ct)
        {
            if (_t.StallSaves) return Stall(ct);
            if (_t.FailNextSave is { } fail) { _t.FailNextSave = null; throw fail; }
            var item = _ghost ? body[kind == PushItemKind.Page ? "pages" : "posts"]![0]! : body;
            var slug = item["slug"]!.GetValue<string>();
            if (slug == _t.AlwaysFailSlug) throw new PushException("The site answered HTTP 500: broken.", 500);
            var key = Key(kind, slug);
            var id = existing?.Id ?? _t.NextId();
            if (existing is null && _t.Items.ContainsKey(key)) key += "-" + id;    // duplicate: the target picks a free slug
            _t.Items[key] = (id, body);
            _t.Saves.Add((Key(kind, slug), existing is not null, body));
            return Task.FromResult(new RemoteItem(id, "https://dest.example/" + slug, "v1"));
        }

        private static async Task<RemoteItem> Stall(CancellationToken ct) { await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); }

        public Task<RemoteItem> UploadImageAsync(string fileName, string contentType, Stream content, string? alt, string? caption, CancellationToken ct)
        {
            if (_t.FailUploads is { } refused) throw refused;
            _t.Uploads++;
            _t.LastAlt = alt;
            var url = "https://dest.example/wp-content/uploads/" + fileName;
            return Task.FromResult(new RemoteItem(_ghost ? url : _t.MediaId, url));
        }

        public Task<string> EnsureTermAsync(PushTerm term, CancellationToken ct)
        {
            _t.Terms.Add(term.Taxonomy + ":" + term.Slug);
            return Task.FromResult(_t.NextId());
        }
    }

    private sealed class FakeFactory : IPushClientFactory
    {
        private readonly FakeTarget _t;
        public FakeFactory(FakeTarget t) => _t = t;
        public bool AllowPrivateTargets => false;
        public IPushClient Create(ExportTarget target, string siteUrl, PushCredential credential) => new FakeClient(_t, target == ExportTarget.Ghost);
    }

    private sealed class FakeSource : IExportSource
    {
        public readonly List<ExportPost> Posts = new();
        public readonly List<ExportPage> Pages = new();
        public Task<ExportCounts> CountAsync(ExportOptions options) => throw new NotSupportedException();
        public Task<ExportCatalog> LoadCatalogAsync(ExportOptions options) => Task.FromResult(Catalog());
        public IAsyncEnumerable<ExportPost> StreamPostsAsync(ExportOptions options, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<ExportPage> StreamPagesAsync(ExportOptions options, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExportItemRef>> ListItemsAsync(ExportOptions options) => Task.FromResult<IReadOnlyList<ExportItemRef>>(
            (options.IncludePages ? Pages.Select(p => new ExportItemRef(PushItemKind.Page, p.Id, p.ParentId, p.Title, p.Slug)) : Enumerable.Empty<ExportItemRef>())
                .Concat(Posts.Select(p => new ExportItemRef(PushItemKind.Post, p.Id, null, p.Title, p.Slug))).ToList());
        public Task<ExportPost?> GetPostAsync(Guid id) => Task.FromResult(Posts.FirstOrDefault(p => p.Id == id));
        public Task<ExportPage?> GetPageAsync(Guid id) => Task.FromResult(Pages.FirstOrDefault(p => p.Id == id));
    }

    private sealed class FakeRepo : IExportPushRepository
    {
        public readonly List<ExportPush> Pushes = new();
        public readonly List<ExportPushItem> Items = new();
        private long _id;

        public Task CreateAsync(ExportPush push, IReadOnlyList<NewPushItem> items)
        {
            push.TotalCount = items.Count;
            Pushes.Add(push);
            foreach (var i in items)
                Items.Add(new ExportPushItem { Id = ++_id, PushId = push.Id, Kind = i.Kind, SourceKey = i.SourceKey, Title = i.Title, Seq = i.Seq, NextAttemptAt = push.StartedAt });
            return Task.CompletedTask;
        }
        public Task<ExportPush?> GetAsync(Guid id) => Task.FromResult(Pushes.FirstOrDefault(p => p.Id == id));
        public Task<IReadOnlyList<ExportPush>> ListRecentAsync(int take) => Task.FromResult<IReadOnlyList<ExportPush>>(Pushes.OrderByDescending(p => p.StartedAt).Take(take).ToList());
        public Task<IReadOnlyList<ExportPush>> GetRunningAsync() => Task.FromResult<IReadOnlyList<ExportPush>>(Pushes.Where(p => p.Status == PushStatus.Running).ToList());
        public Task<ExportPush?> GetActiveAsync() => Task.FromResult(Pushes.LastOrDefault(p => p.IsActive));
        public Task<ExportPushCounts> CountAsync(Guid pushId)
        {
            var c = Items.Where(i => i.PushId == pushId && i.Kind is PushItemKind.Post or PushItemKind.Page).ToList();
            int N(PushItemStatus s) => c.Count(i => i.Status == s);
            return Task.FromResult(new ExportPushCounts(N(PushItemStatus.Pending), N(PushItemStatus.Created), N(PushItemStatus.Updated),
                N(PushItemStatus.Skipped), N(PushItemStatus.Failed), N(PushItemStatus.Cancelled)));
        }
        public Task<IReadOnlyList<ExportPushItem>> GetDueAsync(Guid pushId, DateTime nowUtc, int take) => Task.FromResult<IReadOnlyList<ExportPushItem>>(
            Items.Where(i => i.PushId == pushId && i.Kind is PushItemKind.Post or PushItemKind.Page && i.Status == PushItemStatus.Pending && i.NextAttemptAt <= nowUtc)
                 .OrderBy(i => i.Seq).Take(take).Select(Copy).ToList());
        public Task<ExportPushItem> GetOrAddAsync(Guid pushId, PushItemKind kind, string sourceKey, string? title)
        {
            var hit = Items.FirstOrDefault(i => i.PushId == pushId && i.Kind == kind && i.SourceKey == sourceKey);
            if (hit is null) Items.Add(hit = new ExportPushItem { Id = ++_id, PushId = pushId, Kind = kind, SourceKey = sourceKey, Title = title, NextAttemptAt = DateTime.UtcNow });
            return Task.FromResult(Copy(hit));
        }
        public Task<ExportPushItem?> FindAsync(Guid pushId, PushItemKind kind, string sourceKey) =>
            Task.FromResult(Items.Where(i => i.PushId == pushId && i.Kind == kind && i.SourceKey == sourceKey).Select(Copy).FirstOrDefault());
        public Task MarkItemAsync(long itemId, PushItemStatus status, int attempts, DateTime nextAttemptAtUtc, string? remoteId, string? remoteUrl, string? error, DateTime nowUtc)
        {
            var i = Items.Single(x => x.Id == itemId);
            i.Status = status; i.Attempts = attempts; i.NextAttemptAt = nextAttemptAtUtc;
            i.RemoteId = remoteId ?? i.RemoteId; i.RemoteUrl = remoteUrl ?? i.RemoteUrl; i.Error = error;
            return Task.CompletedTask;
        }
        public Task SetStatusAsync(Guid pushId, PushStatus status, string? message, DateTime? resumeAtUtc, DateTime nowUtc, DateTime? finishedAtUtc)
        {
            var p = Pushes.Single(x => x.Id == pushId);
            p.Status = status; p.Message = message; p.ResumeAt = resumeAtUtc; p.FinishedAt = finishedAtUtc ?? p.FinishedAt;
            return Task.CompletedTask;
        }
        public Task<int> CancelPendingAsync(Guid pushId, DateTime nowUtc)
        {
            var pending = Items.Where(i => i.PushId == pushId && i.Status == PushItemStatus.Pending).ToList();
            foreach (var i in pending) i.Status = PushItemStatus.Cancelled;
            return Task.FromResult(pending.Count(i => i.Kind is PushItemKind.Post or PushItemKind.Page));
        }
        public Task<IReadOnlyList<ExportPushItem>> ListContentItemsAsync(Guid pushId, PushItemStatus? status) => Task.FromResult<IReadOnlyList<ExportPushItem>>(
            Items.Where(i => i.PushId == pushId && i.Kind is PushItemKind.Post or PushItemKind.Page && (status is null || i.Status == status)).ToList());
        private IEnumerable<ExportPushItem> Match(Guid pushId, PushItemStatus? status, string? q) =>
            Items.Where(i => i.PushId == pushId && i.Kind is PushItemKind.Post or PushItemKind.Page && (status is null || i.Status == status)
                && (string.IsNullOrWhiteSpace(q) || new[] { i.Title, i.Error, i.RemoteUrl }.Any(v => v?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)));
        public Task<IReadOnlyList<ExportPushItem>> SearchContentItemsAsync(Guid pushId, PushItemStatus? status, string? query, int page, int pageSize) =>
            Task.FromResult<IReadOnlyList<ExportPushItem>>(Match(pushId, status, query).OrderBy(i => i.Seq).Skip((page - 1) * pageSize).Take(pageSize).ToList());
        public Task<int> CountContentItemsAsync(Guid pushId, PushItemStatus? status, string? query) => Task.FromResult(Match(pushId, status, query).Count());
        public Task<int> PruneFinishedAsync(DateTime olderThanUtc)
        {
            var gone = Pushes.Where(p => p.Status is PushStatus.Completed or PushStatus.Cancelled && p.FinishedAt < olderThanUtc).Select(p => p.Id).ToList();
            Pushes.RemoveAll(p => gone.Contains(p.Id)); Items.RemoveAll(i => gone.Contains(i.PushId));
            return Task.FromResult(gone.Count);
        }

        // The service must not depend on mutating what the repository hands back.
        private static ExportPushItem Copy(ExportPushItem i) => new()
        {
            Id = i.Id, PushId = i.PushId, Kind = i.Kind, SourceKey = i.SourceKey, Title = i.Title, Seq = i.Seq, Status = i.Status,
            Attempts = i.Attempts, NextAttemptAt = i.NextAttemptAt, RemoteId = i.RemoteId, RemoteUrl = i.RemoteUrl, Error = i.Error,
        };
    }

    private sealed class FakeAudit : IAuditRepository
    {
        public readonly List<AuditLog> Entries = new();
        public Task LogAsync(AuditLog entry) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<(IReadOnlyList<AuditLog> Items, int Total)> GetPagedAsync(Guid ownerId, int page, int pageSize, string? entityType = null,
            string? action = null, Guid? userId = null, DateTime? from = null, DateTime? to = null) =>
            Task.FromResult<(IReadOnlyList<AuditLog>, int)>((Entries, Entries.Count));
    }

    private sealed class FakeTenant : ITenantContext
    {
        public Guid UserId => Guid.Empty;
        public string Slug => "";
        public bool IsCloudMode => false;
        public string TenantPathPrefix => "";
        public bool IsResolved => true;
    }

    private sealed class FakeEnv : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public FakeEnv(string webRoot) { WebRootPath = webRoot; ContentRootPath = webRoot; }
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Blog.Web";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = Environments.Development;
    }
}
