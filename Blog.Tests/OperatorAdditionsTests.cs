using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Middleware;
using Blog.Web.Services;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Duplicate a post or page, maintenance mode, the email to moderators about a new comment, the
/// floating contact button, and the site's custom code and CSS: what each produces, what each
/// refuses, and the rules around custom code (Admin only, audited, host switch, held for consent).
/// </summary>
public class OperatorAdditionsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public OperatorAdditionsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value)).Build();

    /// <summary>A stand-in for a repository interface: the named methods answer, anything else fails the test loudly.</summary>
    public class Stub<T> : DispatchProxy where T : class
    {
        private Dictionary<string, Func<object?[]?, object?>> _answers = new();
        public static T Of(Dictionary<string, Func<object?[]?, object?>> answers)
        {
            var proxy = Create<T, Stub<T>>();
            ((Stub<T>)(object)proxy)._answers = answers;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            _answers.TryGetValue(method!.Name, out var answer) ? answer(args) : throw new NotImplementedException(method.Name);
    }

    // ── Duplicate ────────────────────────────────────────────────────────

    [Fact]
    public void A_copied_post_is_a_draft_with_the_content_and_none_of_the_originals_history()
    {
        var source = new Post
        {
            Id = Guid.NewGuid(), Title = "Annual report", Slug = "annual-report", Html = "<p>Body</p>", Plaintext = "Body", Markdown = "Body",
            EditorMode = EditorModes.Markdown, FeatureImage = "/uploads/a.jpg", MetaTitle = "Meta", MetaDescription = "Desc",
            CanonicalUrl = "https://example.org/annual-report", Status = PostStatus.Published, PublishedAt = DateTime.UtcNow.AddDays(-9),
            ScheduledAt = DateTime.UtcNow.AddDays(-9), LastVerifiedAt = DateTime.UtcNow, NextReviewAt = DateTime.UtcNow.AddDays(90),
            ReviewNote = "fix the intro", ReviewRequestedAt = DateTime.UtcNow, ViewCount = 4200, AllowComments = false,
            FaqJson = "[{\"Question\":\"Q\",\"Answer\":\"A\"}]", AnswerCapsule = "Short answer.", KeyFactsJson = "[]", HowToJson = "[]",
            RoundupJson = "{}", CustomFieldsJson = "{\"price\":\"9\"}", Visibility = "unlisted", AuthorId = Guid.NewGuid(),
        };
        var me = Guid.NewGuid();
        var copy = ContentCopy.Of(source, me);

        Assert.Equal("Annual report (copy)", copy.Title);
        Assert.Equal(PostStatus.Draft, copy.Status);
        Assert.Equal(me, copy.AuthorId);
        Assert.Equal(Guid.Empty, copy.Id);
        Assert.NotEqual(source.Uuid, copy.Uuid);
        Assert.Equal("", copy.Slug);                       // the service makes a free address
        Assert.Null(copy.CanonicalUrl);                    // or search engines would be pointed at the original
        Assert.Null(copy.PublishedAt); Assert.Null(copy.ScheduledAt); Assert.Null(copy.LastVerifiedAt); Assert.Null(copy.NextReviewAt);
        Assert.Null(copy.ReviewNote); Assert.Null(copy.ReviewRequestedAt);
        Assert.Equal(0, copy.ViewCount);
        Assert.Equal(source.Html, copy.Html); Assert.Equal(source.Markdown, copy.Markdown); Assert.Equal(source.EditorMode, copy.EditorMode);
        Assert.Equal(source.FeatureImage, copy.FeatureImage); Assert.Equal(source.MetaDescription, copy.MetaDescription);
        Assert.Equal(source.FaqJson, copy.FaqJson); Assert.Equal(source.AnswerCapsule, copy.AnswerCapsule);
        Assert.Equal(source.CustomFieldsJson, copy.CustomFieldsJson); Assert.Equal(source.RoundupJson, copy.RoundupJson);
        Assert.Equal("unlisted", copy.Visibility); Assert.False(copy.AllowComments);
    }

    [Fact]
    public void A_copied_page_is_unpublished_out_of_the_navigation_and_at_its_own_address()
    {
        var source = new Page { Id = Guid.NewGuid(), Title = "About", Slug = "about", Content = "<p>Us</p>", IsPublished = true, IsInNav = true,
            PublishedAt = DateTime.UtcNow, CanonicalUrl = "https://example.org/about", MetaDescription = "About us", SortOrder = 3 };
        var copy = ContentCopy.Of(source, Guid.NewGuid(), ContentCopy.CopySlug(source.Slug, 1));
        Assert.Equal("About (copy)", copy.Title);
        Assert.Equal("about-copy", copy.Slug);
        Assert.False(copy.IsPublished); Assert.False(copy.IsInNav);
        Assert.Null(copy.PublishedAt); Assert.Null(copy.CanonicalUrl);
        Assert.Equal(source.Content, copy.Content); Assert.Equal(source.MetaDescription, copy.MetaDescription);
        Assert.Equal("about-copy-2", ContentCopy.CopySlug("about", 2));
        Assert.Equal("page-copy", ContentCopy.CopySlug(" ", 1));
    }

    [Fact]
    public void A_copy_title_always_fits_the_column()
    {
        var title = ContentCopy.CopyTitle(new string('x', 400));
        Assert.Equal(ContentCopy.MaxTitleLength, title.Length);
        Assert.EndsWith(" (copy)", title);
        Assert.Equal("Untitled (copy)", ContentCopy.CopyTitle("  "));
        Assert.True(ContentCopy.CopySlug(new string('s', 300), 12).Length <= ContentCopy.MaxTitleLength);
    }

    [Fact]
    public void Duplicate_is_a_guarded_audited_post_action_on_both_lists()
    {
        foreach (var (file, action) in new[] { ("PostsController.cs", "AuditActions.PostDuplicated"), ("PagesController.cs", "AuditActions.PageDuplicated") })
        {
            var source = Read("Blog.Web", "Controllers", file);
            var at = source.IndexOf("public async Task<IActionResult> Duplicate(Guid id)", StringComparison.Ordinal);
            Assert.True(at > 0, file);
            var head = source[Math.Max(0, at - 160)..at];
            Assert.Contains("[HttpPost(\"{id:guid}/duplicate\")]", head);
            Assert.Contains("[ValidateAntiForgeryToken]", head);
            Assert.Contains(action, source[at..(at + 2600)]);
        }
        // A copy of someone else's post is for editors: an author copies only their own.
        var posts = Read("Blog.Web", "Controllers", "PostsController.cs");
        var body = posts[posts.IndexOf("public async Task<IActionResult> Duplicate(Guid id)", StringComparison.Ordinal)..];
        Assert.Contains("return Forbid();", body[..900]);

        Assert.Contains("action=\"/admin/posts/@post.Id/duplicate\"", Read("Blog.Web", "Views", "Posts", "Index.cshtml"));
        Assert.Contains("action=\"/admin/pages/@item.Id/duplicate\"", Read("Blog.Web", "Views", "Pages", "Index.cshtml"));
    }

    [Fact]
    public async Task Duplicating_a_post_in_the_desk_makes_a_draft_copy_with_its_categories_and_tags()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddAuthentication(AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName)
             .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AdminPagesSmokeTests.TestAdminAuthHandler>(AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName, _ => { });
            s.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName;
                o.DefaultChallengeScheme = AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName;
            });
        }));
        using var scope = factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        if (admin is null) return;
        if (AdminPagesSmokeTests.TestAdminAuthHandler.UserId == Guid.Empty) AdminPagesSmokeTests.TestAdminAuthHandler.UserId = admin.Id;
        var me = AdminPagesSmokeTests.TestAdminAuthHandler.UserId;

        var cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var sourceId = Guid.NewGuid();
        var slug = "zz-dup-" + tag;
        var tagId = Guid.NewGuid();
        try
        {
            await conn.ExecuteAsync(@"INSERT INTO Posts (Id, Title, Slug, Html, Plaintext, Status, Visibility, Type, AuthorId, PublishedAt, CreatedAt, UpdatedAt, CanonicalUrl, AnswerCapsule, ViewCount)
                                      VALUES (@id, @title, @slug, N'<p>Copied body.</p>', N'Copied body.', N'Published', N'public', N'post', @author, SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME(), N'https://example.org/original', N'The short answer.', 77)",
                new { id = sourceId, title = "Dup source " + tag, slug, author = me });
            await conn.ExecuteAsync("INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (@id, @name, @slug, @author)", new { id = tagId, name = "zz-dup-tag-" + tag, slug = "zz-dup-tag-" + tag, author = me });
            await conn.ExecuteAsync("INSERT INTO PostTags (PostId, TagId) VALUES (@p, @t)", new { p = sourceId, t = tagId });

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var list = await client.GetStringAsync("/admin/posts?q=" + Uri.EscapeDataString("Dup source " + tag));
            Assert.Contains($"/admin/posts/{sourceId}/duplicate", list);
            var token = Regex.Match(list, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

            // Without the anti-forgery token nothing is copied.
            var bare = await client.PostAsync($"/admin/posts/{sourceId}/duplicate", new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert.Equal(HttpStatusCode.BadRequest, bare.StatusCode);

            var response = await client.PostAsync($"/admin/posts/{sourceId}/duplicate",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var copyId = Guid.Parse(Regex.Match(response.Headers.Location!.ToString(), "/edit/([0-9a-fA-F-]{36})").Groups[1].Value);
            Assert.NotEqual(sourceId, copyId);

            var copy = await conn.QuerySingleAsync("SELECT Title, Slug, Status, Html, CanonicalUrl, AnswerCapsule, ViewCount, PublishedAt, AuthorId FROM Posts WHERE Id = @copyId", new { copyId });
            Assert.Equal($"Dup source {tag} (copy)", (string)copy.Title);
            Assert.Equal("Draft", (string)copy.Status);
            Assert.NotEqual(slug, (string)copy.Slug);
            Assert.Equal("<p>Copied body.</p>", (string)copy.Html);
            Assert.Equal("The short answer.", (string)copy.AnswerCapsule);
            Assert.Null((string?)copy.CanonicalUrl);
            Assert.Equal(0, (int)copy.ViewCount);
            Assert.Null((DateTime?)copy.PublishedAt);
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM PostTags WHERE PostId = @copyId AND TagId = @tagId", new { copyId, tagId }));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Post.Duplicated' AND EntityId = @id", new { id = copyId.ToString() }));
            // The original is untouched.
            Assert.Equal("Published", await conn.ExecuteScalarAsync<string>("SELECT Status FROM Posts WHERE Id = @sourceId", new { sourceId }));
        }
        finally
        {
            var ids = (await conn.QueryAsync<Guid>("SELECT Id FROM Posts WHERE Title LIKE @t", new { t = "Dup source " + tag + "%" })).ToList();
            foreach (var id in ids)
            {
                await conn.ExecuteAsync("DELETE FROM PostTags WHERE PostId = @id; DELETE FROM PostCategories WHERE PostId = @id;", new { id });
                try { await conn.ExecuteAsync("DELETE FROM Revisions WHERE EntityId = @id", new { id }); } catch { /* no revision rows */ }
                await conn.ExecuteAsync("DELETE FROM Posts WHERE Id = @id", new { id });
            }
            await conn.ExecuteAsync("DELETE FROM Tags WHERE Id = @tagId", new { tagId });
        }
    }

    // ── Audit Trail filter ───────────────────────────────────────────────

    [Fact]
    public async Task The_audit_trail_lists_entries_until_a_filter_is_typed()
    {
        // "action" is a route value as well as a filter box: read by default binding it was always "Index".
        var controller = Read("Blog.Web", "Controllers", "AuditController.cs");
        const string bound = "[FromQuery(Name = \"action\")] string? action,";
        Assert.Equal(2, controller.Split(bound).Length - 1);
        Assert.Equal(2, controller.Split("string? action,").Length - 1);          // no parameter of that name left to default binding

        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddAuthentication(AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName)
             .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AdminPagesSmokeTests.TestAdminAuthHandler>(AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName, _ => { });
            s.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName;
                o.DefaultChallengeScheme = AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName;
            });
        }));
        using var scope = factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        if (admin is null) return;
        if (AdminPagesSmokeTests.TestAdminAuthHandler.UserId == Guid.Empty) AdminPagesSmokeTests.TestAdminAuthHandler.UserId = admin.Id;

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var open = await client.GetStringAsync("/admin/audit");
        Assert.Contains("name=\"action\"", open);
        Assert.DoesNotContain("name=\"action\" value=", open);                        // nothing filtered on arrival (an empty value is left out)
        var filtered = await client.GetStringAsync("/admin/audit?action=No.SuchAction");
        Assert.Contains("name=\"action\" value=\"No.SuchAction\"", filtered);        // the box's value is what filters
    }

    // ── Maintenance mode ─────────────────────────────────────────────────

    [Theory]
    [InlineData("/", true)]
    [InlineData("/a-post", true)]
    [InlineData("/feed", true)]
    [InlineData("/sitemap.xml", true)]
    [InlineData("/administrator", true)]              // not the Desk: only "/admin" and what is under it stay open
    [InlineData("/newsletter/subscribe", true)]
    [InlineData("/admin", false)]
    [InlineData("/admin/settings", false)]
    [InlineData("/Account/Login", false)]
    [InlineData("/api/v1/posts", false)]
    [InlineData("/mcp", false)]
    [InlineData("/health/live", false)]
    [InlineData("/robots.txt", false)]
    [InlineData("/.well-known/security.txt", false)]
    [InlineData("/newsletter/unsubscribe/abc", false)]
    [InlineData("/newsletter/confirm/abc", false)]
    public void The_holding_page_covers_reader_addresses_and_leaves_the_desk_and_sign_in_open(string path, bool covered) =>
        Assert.Equal(covered, MaintenanceMode.Covers(path));

    [Fact]
    public void The_holding_page_is_self_contained_and_encodes_what_the_operator_typed()
    {
        var html = MaintenanceMode.Page("Tea <b>& Co", "Back at <script>alert(1)</script> noon", "ar", "rtl");
        Assert.Contains("Tea &lt;b&gt;&amp; Co", html);
        Assert.DoesNotContain("<script", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\">", html);
        Assert.Contains("prefers-color-scheme:dark", html);
        Assert.DoesNotContain("href=", html);              // no link into a site that is closed
        Assert.DoesNotContain("src=", html);
        Assert.Contains(MaintenanceMode.DefaultMessage, MaintenanceMode.Page("Site", "  ", null, null));
        Assert.Equal(MaintenanceMode.MaxMessageLength, MaintenanceMode.CleanMessage(new string('m', 900)).Length);
        Assert.Equal("one two", MaintenanceMode.CleanMessage("one\ntwo\u0007"));
    }

    private static (MaintenanceModeMiddleware Middleware, MaintenanceGate Gate, Func<bool> Reached) Maintenance(IConfiguration config)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var gate = new MaintenanceGate(new MemoryCache(new MemoryCacheOptions()), services.GetRequiredService<IServiceScopeFactory>(), config);
        var reached = false;
        return (new MaintenanceModeMiddleware(_ => { reached = true; return Task.CompletedTask; }, gate), gate, () => reached);
    }

    private static DefaultHttpContext Request(string path, bool signedIn = false, string method = "GET")
    {
        var ctx = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        ctx.Response.Body = new MemoryStream();
        if (signedIn) ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "editor") }, "test"));
        return ctx;
    }

    [Fact]
    public async Task While_maintenance_mode_is_on_readers_get_503_with_retry_after_and_staff_get_the_site()
    {
        var (middleware, gate, reached) = Maintenance(Config());
        gate.Remember(Guid.Empty, new MaintenanceGate.State(true, "Field Notes", "Moving house until Monday.", "en"));

        var reader = Request("/a-post");
        await middleware.InvokeAsync(reader);
        Assert.False(reached());
        Assert.Equal(503, reader.Response.StatusCode);
        Assert.Equal("3600", reader.Response.Headers.RetryAfter.ToString());
        Assert.Equal("no-store", reader.Response.Headers.CacheControl.ToString());
        Assert.Equal("noindex", reader.Response.Headers["X-Robots-Tag"].ToString());
        reader.Response.Body.Position = 0;
        var body = await new StreamReader(reader.Response.Body).ReadToEndAsync();
        Assert.Contains("Moving house until Monday.", body);
        Assert.Contains("Field Notes", body);

        var head = Request("/", method: "HEAD");
        await middleware.InvokeAsync(head);
        Assert.Equal(503, head.Response.StatusCode);
        Assert.Equal(0, head.Response.Body.Length);

        foreach (var open in new[] { Request("/a-post", signedIn: true), Request("/admin/settings"), Request("/account/login"), Request("/robots.txt") })
        {
            var (m, g, r) = Maintenance(Config());
            g.Remember(Guid.Empty, new MaintenanceGate.State(true, "Field Notes", "", "en"));
            await m.InvokeAsync(open);
            Assert.True(r(), open.Request.Path);
            Assert.Equal(200, open.Response.StatusCode);
        }
    }

    [Fact]
    public async Task Maintenance_mode_fails_open_and_a_host_can_switch_it_off()
    {
        // Off: the site is served.
        var (off, offGate, offReached) = Maintenance(Config());
        offGate.Remember(Guid.Empty, MaintenanceGate.Off);
        await off.InvokeAsync(Request("/"));
        Assert.True(offReached());

        // Settings cannot be read (no repositories registered here): the site is served, not a 503.
        var (broken, _, brokenReached) = Maintenance(Config());
        var ctx = Request("/");
        await broken.InvokeAsync(ctx);
        Assert.True(brokenReached());
        Assert.Equal(200, ctx.Response.StatusCode);

        // The host said no: a stored "on" is ignored.
        var (locked, lockedGate, lockedReached) = Maintenance(Config(("Site:AllowMaintenanceMode", "false")));
        lockedGate.Remember(Guid.Empty, new MaintenanceGate.State(true, "S", "", "en"));
        await locked.InvokeAsync(Request("/"));
        Assert.True(lockedReached());

        // Saving Settings drops what was remembered.
        var (m, gate, _) = Maintenance(Config());
        gate.Remember(Guid.Empty, new MaintenanceGate.State(true, "S", "", "en"));
        gate.Forget();
        Assert.False((await gate.GetAsync(Guid.Empty)).On);
        Assert.NotNull(m);
    }

    [Fact]
    public void Maintenance_mode_sits_after_sign_in_and_the_tenant_and_is_audited()
    {
        var program = Read("Blog.Web", "Program.cs");
        var at = program.IndexOf("app.UseMiddleware<Blog.Web.Middleware.MaintenanceModeMiddleware>();", StringComparison.Ordinal);
        Assert.True(at > program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal));
        Assert.True(at > program.IndexOf("app.UseTenantResolution();", StringComparison.Ordinal));
        Assert.True(at > program.IndexOf("app.UseIpFirewall();", StringComparison.Ordinal));
        Assert.True(at < program.IndexOf("app.MapControllers();", StringComparison.Ordinal));

        var settings = Read("Blog.Web", "Controllers", "SettingsController.cs");
        Assert.Contains("AuditActions.SettingsMaintenanceOn", settings);
        Assert.Contains("AuditActions.SettingsMaintenanceOff", settings);
        Assert.Contains("GetRequiredService<MaintenanceGate>().Forget()", settings);
        Assert.False(new UserSettings().MaintenanceModeEnabled);
        Assert.Contains("shell.MaintenanceOn", Read("Blog.Web", "Views", "Shared", "_AdminLayout.cshtml"));
    }

    // ── Comment emails ───────────────────────────────────────────────────

    [Fact]
    public void A_comment_email_goes_to_the_listed_people_or_else_the_posts_author()
    {
        Assert.Equal(new[] { "a@example.org", "b@example.org" },
            CommentNotice.Recipients("a@example.org; b@example.org,\nA@example.org, not-an-address", "author@example.org"));
        Assert.Equal(new[] { "author@example.org" }, CommentNotice.Recipients("  ", "author@example.org"));
        Assert.Empty(CommentNotice.Recipients("", null));
        Assert.Equal(CommentNotice.MaxRecipients,
            CommentNotice.Recipients(string.Join(",", Enumerable.Range(0, 30).Select(i => $"p{i}@example.org")), null).Count);
    }

    [Theory]
    [InlineData("reader@example.org", true)]
    [InlineData("a.b+c@mail.example.co.uk", true)]
    [InlineData("no-at.example.org", false)]
    [InlineData("two@@example.org", false)]
    [InlineData("a@b", false)]
    [InlineData("a@example.org\r\nBcc: x@evil.test", false)]
    [InlineData("\"quoted\"@example.org", false)]
    [InlineData("a@example.org,b@example.org", false)]
    [InlineData("", false)]
    public void Only_a_plain_address_is_accepted_as_a_recipient(string value, bool ok) => Assert.Equal(ok, CommentNotice.LooksLikeAddress(value));

    [Fact]
    public void The_comment_email_encodes_the_readers_text_and_leaves_their_address_out()
    {
        var held = CommentNotice.Build("Field <Notes>", "Tea & \"coffee\"", "https://example.org/tea", "https://example.org/admin/comments?status=pending",
            "Mallory <img src=x onerror=alert(1)>", "Nice post.\n<script>steal()</script>", held: true, isReply: false);
        Assert.StartsWith("[Field <Notes>] A comment is waiting for approval: Tea & \"coffee\"", held.Subject);
        Assert.DoesNotContain("<script", held.Html);
        Assert.DoesNotContain("<img", held.Html);
        Assert.Contains("&lt;script&gt;steal()&lt;/script&gt;", held.Html);
        Assert.Contains("href=\"https://example.org/admin/comments?status=pending\"", held.Html);
        Assert.Contains("not on the site yet", held.Text);
        Assert.Contains("https://example.org/tea", held.Text);

        var live = CommentNotice.Build("Site", "Post", "https://e.org/p", "https://e.org/admin/comments", "Ann", "Hello", held: false, isReply: true);
        Assert.StartsWith("[Site] New reply on: Post", live.Subject);
        Assert.Contains("already on the site", live.Html);

        // A subject is one line whatever the reader or the post title contains.
        var odd = CommentNotice.Build("Site", "Line one\r\nBcc: x@evil.test", "u", "m", "Ann", new string('c', 5000), true, false);
        Assert.DoesNotContain("\n", odd.Subject);
        Assert.DoesNotContain("\r", odd.Subject);
        Assert.True(odd.Text.Length < 2600);
    }

    [Fact]
    public void Comment_emails_stop_at_the_hourly_limit_and_start_again_after_it()
    {
        var sent = new List<DateTime>();
        var now = new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < CommentNotice.MaxPerHour; i++) Assert.True(CommentNotice.TryTake(sent, now.AddSeconds(i)));
        Assert.False(CommentNotice.TryTake(sent, now.AddMinutes(30)));
        Assert.True(CommentNotice.TryTake(sent, now.AddMinutes(61)));

        var throttle = new CommentNoticeThrottle();
        var site = Guid.NewGuid();
        for (var i = 0; i < CommentNotice.MaxPerHour; i++) Assert.True(throttle.TryTake(site, now));
        Assert.False(throttle.TryTake(site, now));
        Assert.True(throttle.TryTake(Guid.NewGuid(), now));      // another site has its own allowance
    }

    private sealed class RecordingEmail(bool configured, bool fail = false) : IEmailService
    {
        public List<EmailMessage> Sent { get; } = new();
        public bool IsConfigured => configured;
        public Task SendAsync(string toEmail, string toName, string subject, string htmlBody) => Task.CompletedTask;
        public Task<EmailSendResult> SendMessageAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (fail) throw new InvalidOperationException("mail server down");
            Sent.Add(message);
            return Task.FromResult(EmailSendResult.Ok);
        }
    }

    private static CommentNotifier Notifier(UserSettings settings, RecordingEmail email, IConfiguration? config = null) => new(
        Stub<ISettingRepository>.Of(new() { ["GetSettingsAsync"] = _ => Task.FromResult(settings) }),
        Stub<IUserRepository>.Of(new() { ["GetByIdAsync"] = _ => Task.FromResult<User?>(new User { Email = "author@example.org" }) }),
        email, new CommentNoticeThrottle(), config ?? Config(), NullLogger<CommentNotifier>.Instance);

    private static DefaultHttpContext CommentRequest(bool signedIn = false)
    {
        var ctx = Request("/a-post/comment", signedIn, "POST");
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("attacker.example");
        return ctx;
    }

    [Fact]
    public async Task A_readers_comment_is_emailed_once_per_recipient_and_only_when_switched_on()
    {
        var post = new Post { Id = Guid.NewGuid(), Title = "Tea", Slug = "tea", AuthorId = Guid.NewGuid() };
        var comment = new Comment { AuthorName = "Ann", AuthorEmail = "ann@reader.example", Content = "Lovely.", Status = CommentStatus.Pending };

        // Off by default: nothing is sent.
        Assert.False(new UserSettings().CommentNotifyEnabled);
        var quiet = new RecordingEmail(true);
        await Notifier(new UserSettings(), quiet).NotifyAsync(CommentRequest(), Guid.NewGuid(), comment, post);
        Assert.Empty(quiet.Sent);

        // On with no list: the post's author. Links use the configured site address, not the request's Host header.
        var toAuthor = new RecordingEmail(true);
        await Notifier(new UserSettings { CommentNotifyEnabled = true, SiteName = "Field Notes" }, toAuthor, Config(("CanonicalHost", "www.example.org")))
            .NotifyAsync(CommentRequest(), Guid.NewGuid(), comment, post);
        var only = Assert.Single(toAuthor.Sent);
        Assert.Equal("author@example.org", only.ToEmail);
        Assert.Contains("https://www.example.org/tea", only.Html);
        Assert.DoesNotContain("attacker.example", only.Html);
        Assert.DoesNotContain("ann@reader.example", only.Html + only.Text);
        Assert.Contains("waiting for approval", only.Subject);

        // On with a list: each listed person, not the author.
        var toList = new RecordingEmail(true);
        await Notifier(new UserSettings { CommentNotifyEnabled = true, CommentNotifyEmails = "a@example.org, b@example.org" }, toList)
            .NotifyAsync(CommentRequest(), Guid.NewGuid(), comment, post);
        Assert.Equal(new[] { "a@example.org", "b@example.org" }, toList.Sent.Select(m => m.ToEmail).ToArray());

        // A reply written by signed-in staff is not announced; nor is anything when no mail server is set up.
        var staff = new RecordingEmail(true);
        await Notifier(new UserSettings { CommentNotifyEnabled = true }, staff).NotifyAsync(CommentRequest(signedIn: true), Guid.NewGuid(), comment, post);
        Assert.Empty(staff.Sent);
        var noServer = new RecordingEmail(false);
        await Notifier(new UserSettings { CommentNotifyEnabled = true }, noServer).NotifyAsync(CommentRequest(), Guid.NewGuid(), comment, post);
        Assert.Empty(noServer.Sent);

        // A mail server that throws never reaches the reader.
        await Notifier(new UserSettings { CommentNotifyEnabled = true }, new RecordingEmail(true, fail: true)).NotifyAsync(CommentRequest(), Guid.NewGuid(), comment, post);
    }

    [Fact]
    public void The_comment_email_is_sent_after_the_comment_is_stored_and_never_for_rejected_spam()
    {
        var blog = Read("Blog.Web", "Controllers", "BlogController.cs");
        var stored = blog.IndexOf("await _comments.CreateAsync(comment);", StringComparison.Ordinal);
        var notified = blog.IndexOf("GetRequiredService<Blog.Web.Services.CommentNotifier>().NotifyAsync(", StringComparison.Ordinal);
        var rejected = blog.IndexOf("case Blog.Core.Services.CommentSpamAction.RejectSilently:", StringComparison.Ordinal);
        Assert.True(rejected > 0 && stored > rejected && notified > stored);
        Assert.Equal(1, Regex.Matches(blog, @"CommentNotifier>\(\)\.NotifyAsync\(").Count);
    }

    // ── Contact button ───────────────────────────────────────────────────

    [Theory]
    [InlineData("whatsapp", "+44 20 7946 0958", "https://wa.me/442079460958", true)]
    [InlineData("WhatsApp", "(91) 98765-43210", "https://wa.me/919876543210", true)]
    [InlineData("phone", "+1 415 555 0132", "tel:+14155550132", false)]
    [InlineData("email", "hello@example.org", "mailto:hello@example.org", false)]
    public void The_contact_button_links_to_whatsapp_a_call_or_an_email(string kind, string value, string href, bool newTab)
    {
        var link = ContactButton.Build(kind, value, null);
        Assert.NotNull(link);
        Assert.Equal(href, link!.Href);
        Assert.Equal(newTab, link.OpensNewTab);
        Assert.Equal(ContactButton.DefaultLabel(link.Kind), link.Label);
    }

    [Theory]
    [InlineData("", "+44 20 7946 0958")]
    [InlineData("telegram", "+44 20 7946 0958")]
    [InlineData("whatsapp", "12345")]                              // too short to be a number
    [InlineData("whatsapp", "1234567890123456")]                   // longer than any number
    [InlineData("whatsapp", "44 20 +7946 0958")]
    [InlineData("phone", "call me")]
    [InlineData("phone", "+44 20 7946 0958?text=<script>")]
    [InlineData("email", "javascript:alert(1)")]
    [InlineData("email", "a@example.org?bcc=x@evil.test")]
    [InlineData("email", "a@example.org, b@example.org")]
    [InlineData("email", "")]
    public void No_button_is_made_for_anything_that_would_not_be_a_working_link(string kind, string value) =>
        Assert.Null(ContactButton.Build(kind, value, "Talk to us"));

    [Fact]
    public void The_contact_button_is_off_by_default_validated_on_save_and_a_plain_link()
    {
        Assert.Equal("", new UserSettings().ContactButtonKind);
        Assert.Null(ContactButton.Validate("", "anything"));
        Assert.NotNull(ContactButton.Validate("whatsapp", "nope"));
        Assert.Equal(ContactButton.MaxLabelLength, ContactButton.CleanLabel(new string('l', 90)).Length);
        Assert.Equal("Ask us", ContactButton.Build("email", "a@example.org", " Ask\nus ".Replace("\n", " "))!.Label);

        var layout = Read("Blog.Web", "Views", "Shared", "_PublicLayout.cshtml");
        Assert.Contains("Blog.Core.Services.ContactButton.Build(globalSettings.ContactButtonKind, globalSettings.ContactButtonValue, globalSettings.ContactButtonLabel)", layout);
        Assert.Contains("class=\"ink-contact\"", layout);
        Assert.Contains("ContactButton.Validate(contactKind, model.ContactButtonValue)", Read("Blog.Web", "Controllers", "SettingsController.cs"));

        var css = Read("Blog.Web", "wwwroot", "css", "inkwell.css");
        var rule = css[css.IndexOf(".ink-contact {", StringComparison.Ordinal)..];
        Assert.Contains("inset-inline-start", rule[..400]);          // mirrors in right-to-left languages
        Assert.Contains("var(--fg)", rule[..400]);                   // tokens, so every preset and both schemes apply
        Assert.DoesNotMatch(@"#[0-9a-fA-F]{6}", rule[..rule.IndexOf("@media print", StringComparison.Ordinal)]);
    }

    // ── Custom code and CSS ──────────────────────────────────────────────

    [Fact]
    public void Custom_code_is_empty_by_default_and_waits_for_consent_unless_the_admin_says_otherwise()
    {
        var fresh = new UserSettings();
        Assert.Equal("", fresh.CodeInjectionHead + fresh.CodeInjectionFoot + fresh.CustomCss);
        Assert.True(fresh.CodeInjectionNeedsConsent);
        Assert.False(CodeInjection.WaitsForConsent(fresh));                                        // nothing to wait for

        var withCode = new UserSettings { CodeInjectionFoot = "<script src=\"https://tags.example/t.js\"></script>" };
        Assert.True(CodeInjection.WaitsForConsent(withCode));                                      // banner on by default
        withCode.CodeInjectionNeedsConsent = false;
        Assert.False(CodeInjection.WaitsForConsent(withCode));
        withCode.CodeInjectionNeedsConsent = true; withCode.CookieConsentEnabled = false;
        Assert.False(CodeInjection.WaitsForConsent(withCode));                                     // no banner to wait for

        // Code that waits makes the banner ask, even with no Analytics id.
        Assert.Equal(CookieConsentMode.AskForAnalytics, CookieConsent.Decide(true, "", optionalCode: true));
        Assert.Equal(CookieConsentMode.EssentialOnlyNotice, CookieConsent.Decide(true, "", optionalCode: false));
        Assert.Equal(CookieConsentMode.Off, CookieConsent.Decide(false, "", optionalCode: true));
    }

    [Fact]
    public void Held_code_cannot_run_before_consent_whatever_it_contains()
    {
        var json = CodeInjection.HeldJson("</script><script>alert('early')</script>", "<!-- --><img src=x onerror=alert(2)>");
        Assert.DoesNotContain("<", json);
        Assert.DoesNotContain(">", json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("</script><script>alert('early')</script>", doc.RootElement.GetProperty("head").GetString());
        Assert.Equal("<!-- --><img src=x onerror=alert(2)>", doc.RootElement.GetProperty("foot").GetString());
    }

    [Fact]
    public void Custom_css_cannot_leave_its_style_element_and_text_is_capped()
    {
        var css = CodeInjection.SafeCss("a{color:red}</style><script>alert(1)</script>");
        Assert.DoesNotContain("<", css);
        Assert.Contains("a{color:red}", css);
        Assert.Equal(CodeInjection.MaxLength, CodeInjection.Clean(new string('x', CodeInjection.MaxLength + 500)).Length);
        Assert.True(CodeInjection.TooLong(new string('x', CodeInjection.MaxLength + 1)));
        Assert.False(CodeInjection.TooLong(new string('x', CodeInjection.MaxLength)));
        Assert.Equal("ab", CodeInjection.Clean(" a\0b "));
    }

    [Fact]
    public void A_host_decides_whether_custom_code_exists_and_cloud_mode_has_none_unless_asked()
    {
        var single = Stub<ITenantContext>.Of(new() { ["get_IsCloudMode"] = _ => false });
        var cloud = Stub<ITenantContext>.Of(new() { ["get_IsCloudMode"] = _ => true });
        Assert.True(HostSwitches.CodeInjectionAllowed(Config(), single));
        Assert.False(HostSwitches.CodeInjectionAllowed(Config(), cloud));
        Assert.False(HostSwitches.CodeInjectionAllowed(Config(("Site:AllowCodeInjection", "false")), single));
        Assert.True(HostSwitches.CodeInjectionAllowed(Config(("Site:AllowCodeInjection", "true")), cloud));
        Assert.True(HostSwitches.MaintenanceModeAllowed(Config()));
        Assert.False(HostSwitches.MaintenanceModeAllowed(Config(("Site:AllowMaintenanceMode", "false"))));
    }

    [Fact]
    public void Only_an_admin_changes_custom_code_and_every_change_is_audited_with_before_and_after()
    {
        var settings = Read("Blog.Web", "Controllers", "SettingsController.cs");
        Assert.Contains("var codeEditable = model.CodePosted && User.IsInRole(\"Admin\") && HostSwitches.CodeInjectionAllowed(config, _tenantContext);", settings);
        // Every write of the three fields sits inside the codeEditable block.
        var writes = Regex.Matches(settings, @"globalSettings\.(CodeInjectionHead|CodeInjectionFoot|CustomCss|CodeInjectionNeedsConsent) = ");
        Assert.Equal(4, writes.Count);
        var block = Regex.Match(settings, @"if \(codeEditable\)\s*\{\s*codeBefore = [^}]+\}", RegexOptions.Singleline).Value;
        Assert.Equal(4, Regex.Matches(block, @"globalSettings\.(CodeInjectionHead|CodeInjectionFoot|CustomCss|CodeInjectionNeedsConsent) = ").Count);
        Assert.Contains("AuditActions.SettingsSiteCodeChanged, \"Settings\", targetId.ToString(), \"Custom code and CSS\", oldValues: codeBefore, newValues: codeAfter", settings);

        var snapshot = CodeInjection.AuditSnapshot(new UserSettings { CodeInjectionHead = "<meta name=\"x\">", CustomCss = "a{}" });
        Assert.Contains("\"waitsForConsent\":true", snapshot);
        Assert.Contains("a{}", snapshot);

        // The tab is drawn only for an Admin on a host that allows it.
        var view = Read("Blog.Web", "Views", "Settings", "Index.cshtml");
        Assert.Contains("var codeTab = User.IsInRole(\"Admin\") && Blog.Web.Services.HostSwitches.CodeInjectionAllowed(HostConfig, HostTenant);", view);
        var tab = view[view.IndexOf("id=\"s-code\"", StringComparison.Ordinal)..];
        Assert.True(view.LastIndexOf("@if (codeTab)", view.IndexOf("id=\"s-code\"", StringComparison.Ordinal), StringComparison.Ordinal) > 0);
        Assert.Contains("name=\"CodePosted\"", tab);
        Assert.Contains("runs in every visitor's browser", tab);
    }

    [Fact]
    public void The_public_layout_emits_custom_code_only_where_allowed_and_holds_it_for_consent()
    {
        var layout = Read("Blog.Web", "Views", "Shared", "_PublicLayout.cshtml");
        Assert.Contains("var codeAllowed = Blog.Web.Services.HostSwitches.CodeInjectionAllowed(_rcfg, TenantContext);", layout);
        Assert.Contains("var codeLive = codeAllowed && !codeWaits && Blog.Core.Services.CodeInjection.HasHtml(globalSettings);", layout);
        // The raw HTML is written in exactly two places, each inside a codeLive block.
        Assert.Equal(2, Regex.Matches(layout, @"Html\.Raw\(globalSettings\.CodeInjection(Head|Foot)\)").Count);
        Assert.Equal(2, Regex.Matches(layout, @"@if \(codeLive && [^)]*\)\)\s*\{\s*@Html\.Raw\(globalSettings\.CodeInjection(Head|Foot)\)", RegexOptions.Singleline).Count);
        // Held code is data, not markup.
        Assert.Contains("<script type=\"application/json\" id=\"ink-held-code\">@Html.Raw(Blog.Core.Services.CodeInjection.HeldJson(", layout);
        Assert.Contains("<style id=\"site-css\">@Html.Raw(customCss)</style>", layout);
        Assert.Contains("CodeInjection.SafeCss(globalSettings.CustomCss)", layout);
        // Nothing of it in the Desk.
        Assert.DoesNotContain("CodeInjection", Read("Blog.Web", "Views", "Shared", "_AdminLayout.cshtml"));

        var js = Read("Blog.Web", "wwwroot", "js", "inkwell-consent.js");
        Assert.Contains("createContextualFragment", js);
        Assert.Contains("if (choice === 'granted') { loadAnalytics(); loadHeldCode(); }", js);
        Assert.Contains("if (codeLoaded) { location.reload(); return; }", js);
        Assert.Contains("var asking = !!gaId || !!held;", js);

        // A cached post page changes when the button or the code does.
        var blog = Read("Blog.Web", "Controllers", "BlogController.cs");
        Assert.Contains("userSettings.CodeInjectionHead, userSettings.CodeInjectionFoot, userSettings.CustomCss, userSettings.CodeInjectionNeedsConsent,", blog);
        Assert.Contains("userSettings.ContactButtonKind, userSettings.ContactButtonValue, userSettings.ContactButtonLabel,", blog);
    }
}
