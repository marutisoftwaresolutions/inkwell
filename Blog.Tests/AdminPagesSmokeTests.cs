using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Every admin screen touched by the Desk UX work renders as a signed-in admin: the list pages with
/// their search and pager, the editors with the unsaved-changes guard, Settings, and the pages whose
/// tables gained the scroll wrapper. A Razor runtime error on any of them fails here rather than in
/// front of the operator. Runs against the dev database; skips when it is unreachable.
/// </summary>
public class AdminPagesSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public AdminPagesSmokeTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    private HttpClient AdminClient() =>
        _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddAuthentication(TestAdminAuthHandler.SchemeName)
             .AddScheme<AuthenticationSchemeOptions, TestAdminAuthHandler>(TestAdminAuthHandler.SchemeName, _ => { });
            s.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = TestAdminAuthHandler.SchemeName;
                o.DefaultChallengeScheme    = TestAdminAuthHandler.SchemeName;
            });
        })).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/admin/pages", "_ListSearch")]
    [InlineData("/admin/pages?q=zz-no-such-page", "No")]
    [InlineData("/admin/categories?q=a", "Search")]
    [InlineData("/admin/tags", "Search")]
    [InlineData("/admin/series", "Search")]
    [InlineData("/admin/users", "Search")]
    [InlineData("/admin/import", "Search")]
    [InlineData("/admin/redirects?q=x", "Search")]
    [InlineData("/admin/media?q=logo", "Search")]
    [InlineData("/admin/posts", "table-scroll")]
    [InlineData("/admin/calendar", "data-calendar-counts")]
    [InlineData("/admin/calendar?month=2026-02", "table-scroll")]
    [InlineData("/admin/calendar?month=not-a-month", "This month")]
    [InlineData("/admin/security", "desk-page-title")] // an empty rule list shows the empty state, not a table
    [InlineData("/admin/audit", "desk-busy")]          // its table renders only once there are entries
    [InlineData("/admin/errors", "table-scroll")]
    [InlineData("/admin/dashboard", "desk-busy")]
    [InlineData("/admin/settings", "data-track-changes")]
    [InlineData("/admin/posts/create", "data-track-changes")]
    [InlineData("/admin/posts/create?mode=markdown", "markdownSource")]
    [InlineData("/admin/pages/create", "data-track-changes")]
    [InlineData("/admin/pages/create", "data-component=\"gallery\"")]
    [InlineData("/admin/posts/create", "inkwell-gallery-editor.js")]
    [InlineData("/admin/comments", "desk-busy")]
    [InlineData("/admin/analytics", "desk-busy")]
    [InlineData("/admin/ai-crawlers", "desk-busy")]
    [InlineData("/admin/content-health", "desk-busy")]
    [InlineData("/admin/link-audit", "desk-busy")]
    [InlineData("/admin/subscribers", "desk-busy")]
    [InlineData("/admin/theme", "desk-busy")]
    [InlineData("/admin/webhooks", "desk-page-title")]
    [InlineData("/admin/webhooks?q=zz-no-such-hook", "Search")]
    [InlineData("/admin/webhooks/new", "data-track-changes")]
    [InlineData("/admin/menus", "data-track-changes")]
    [InlineData("/admin/backups", "desk-page-title")]
    [InlineData("/admin/newsletter", "desk-page-title")]
    [InlineData("/admin/newsletter/new", "data-track-changes")]
    [InlineData("/account/security", "Two-factor sign-in")]
    [InlineData("/account/two-factor/setup", "qr-box")]
    [InlineData("/admin/export", "table-scroll")]
    [InlineData("/admin/export?target=Ghost&submitted=true&includeEmails=true", "Universal import")]
    [InlineData("/admin/forms", "role=\"search\"")]
    [InlineData("/admin/forms/create", "data-track-changes")]
    public async Task Admin_page_renders_for_an_admin(string url, string expectedFragment)
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var response = await AdminClient().GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} → {(int)response.StatusCode}");
        Assert.DoesNotContain("An error occurred", html);
        Assert.Contains("inkwell-dialog", html);          // layout dialog present on every Desk page
        Assert.Contains("inkwell-desk.js", html);         // busy bar + dirty guard script
        // "_ListSearch" is a partial name, not output; check the rendered form instead.
        if (expectedFragment == "_ListSearch") expectedFragment = "role=\"search\"";
        if (expectedFragment == "Search") expectedFragment = "role=\"search\"";
        Assert.Contains(expectedFragment, html);
    }

    /// <summary>The push status page and its polled progress panel (ADR-0029), for the newest push on the dev database.</summary>
    [Fact]
    public async Task Push_status_page_renders_for_an_admin()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        using var scope = _factory.Services.CreateScope();
        var recent = await scope.ServiceProvider.GetRequiredService<Blog.Core.Interfaces.IExportPushRepository>().ListRecentAsync(1);
        if (recent.Count == 0) { _out.WriteLine("No push on the dev database yet — skipping."); return; }
        var id = recent[0].Id;
        var client = AdminClient();

        foreach (var url in new[] { $"/admin/export/push/{id}", $"/admin/export/push/{id}?show=failed&q=zz-none" })
        {
            var response = await client.GetAsync(url);
            var html = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} → {(int)response.StatusCode}");
            Assert.Contains("role=\"progressbar\"", html);
            Assert.Contains("table-scroll", html);
            Assert.Contains("role=\"search\"", html);
        }
        var panel = await client.GetAsync($"/admin/export/push/{id}/progress");
        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
        Assert.Contains("push-progress", await panel.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/export/push/{Guid.NewGuid()}")).StatusCode);
    }

    /// <summary>
    /// The smoke list above is hand-written, so this test derives every admin index route from the
    /// controllers themselves and fails when a screen exists that the list does not render. Nine
    /// screens were missing on 2026-09-16 because nothing enforced this.
    /// </summary>
    [Fact]
    public void Smoke_list_covers_every_admin_index_route()
    {
        var listed = typeof(AdminPagesSmokeTests)
            .GetMethod(nameof(Admin_page_renders_for_an_admin))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false)
            .Cast<InlineDataAttribute>()
            .SelectMany(a => a.GetData(null!))
            .Select(row => ((string)row[0]!).Split('?')[0].TrimEnd('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var controller in typeof(Program).Assembly.GetTypes()
                     .Where(t => t.IsSubclassOf(typeof(Microsoft.AspNetCore.Mvc.Controller)) && !t.IsAbstract))
        {
            var route = controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RouteAttribute), true)
                .Cast<Microsoft.AspNetCore.Mvc.RouteAttribute>().FirstOrDefault()?.Template;
            if (route is null || !route.StartsWith("admin", StringComparison.OrdinalIgnoreCase)) continue;
            var hasIndex = controller.GetMethods().Any(m =>
                m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute), true)
                 .Cast<Microsoft.AspNetCore.Mvc.HttpGetAttribute>().Any(a => string.IsNullOrEmpty(a.Template)));
            if (!hasIndex) continue; // e.g. Revisions: reached only via an entity id

            var prefix = "/" + route.Replace("[controller]", controller.Name.Replace("Controller", "").ToLowerInvariant()).TrimEnd('/');
            if (!listed.Contains(prefix)) missing.Add(prefix);
        }
        Assert.True(missing.Count == 0, "Admin screens with no smoke coverage: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task Image_details_render_and_save_alt_text_and_caption()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        using var scope = _factory.Services.CreateScope();
        var media = scope.ServiceProvider.GetRequiredService<Blog.Core.Interfaces.IMediaRepository>();
        var item = (await media.GetAllAsync(TestAdminAuthHandler.UserId, 1, 50)).FirstOrDefault(m => m.ContentType.StartsWith("image/"));
        if (item == null) { _out.WriteLine("No image in the admin's library — skipping."); return; }

        var client = AdminClient();
        var page = await client.GetAsync($"/admin/media/details/{item.Id}?folder=images&page=2");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("data-track-changes", html);
        Assert.Contains("for=\"altText\"", html);
        Assert.Contains("for=\"caption\"", html);
        Assert.Contains("name=\"page\" value=\"2\"", html);                 // the way back keeps the library's place
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/media/details/{Guid.NewGuid()}")).StatusCode);

        var api = await client.GetStringAsync("/admin/media/api?page=1");
        Assert.Contains("\"altText\"", api);                                 // the picker can copy it into a gallery

        try
        {
            Assert.True(await media.UpdateDetailsAsync(item.Id, TestAdminAuthHandler.UserId, "A test description", "A test caption"));
            var saved = await media.GetByIdAsync(item.Id);
            Assert.Equal("A test description", saved!.AltText);
            Assert.Equal("A test caption", saved.Caption);
            Assert.False(await media.UpdateDetailsAsync(item.Id, Guid.NewGuid(), "x", null));   // not someone else's file
        }
        finally
        {
            await media.UpdateDetailsAsync(item.Id, TestAdminAuthHandler.UserId, item.AltText, item.Caption);
        }
    }

    [Fact]
    public async Task An_invalid_custom_field_value_comes_back_to_the_field_and_saves_nothing()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        using var scope = _factory.Services.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<Blog.Core.Interfaces.ISettingRepository>();
        var owner = (await scope.ServiceProvider.GetRequiredService<Blog.Core.Interfaces.IUserRepository>().GetFirstAdminAsync())!.Id;
        var settings = await settingsRepo.GetSettingsAsync(owner);
        var original = settings.CustomFields.ToList();
        var posts = scope.ServiceProvider.GetRequiredService<Blog.Core.Interfaces.IPostRepository>();
        var post = (await posts.GetPostsAsync(new Blog.Core.Interfaces.PostFilter { Status = Blog.Core.Domain.PostStatus.Published, Page = 1, PageSize = 1 })).Items.FirstOrDefault();
        if (post == null) { _out.WriteLine("No published post — skipping."); return; }
        var before = (await posts.GetByIdAsync(post.Id, null))!;

        try
        {
            settings.CustomFields = original.Append(new Blog.Core.Domain.CustomFieldDefinition
                { Key = "zz-smoke-price", Label = "Smoke price", Type = "number", AppliesTo = "both" }).ToList();
            await settingsRepo.SaveSettingsAsync(owner, settings);

            var client = AdminClient();

            // A value saved before the field became a number is shown as it is, flagged, never blanked.
            await SetCustomFieldsJsonAsync(post.Id, "{\"zz-smoke-price\":\"from 40\"}");
            var misfit = await client.GetStringAsync($"/admin/posts/edit/{post.Id}");
            Assert.Contains("value=\"from 40\"", misfit);
            Assert.Contains("was saved before this field became a number", misfit);

            var editor = misfit;
            Assert.Contains("name=\"cf[zz-smoke-price]\"", editor);
            Assert.Contains("type=\"number\"", editor);
            Assert.Contains("name=\"cf[zz-smoke-price]\"", await client.GetStringAsync("/admin/pages/create"));
            var settingsPage = await client.GetStringAsync("/admin/settings");
            Assert.Contains("id=\"s-fields\"", settingsPage);
            // For a manual check in a real browser: set INKWELL_UI_DROP to a folder to keep the rendered pages.
            if (Environment.GetEnvironmentVariable("INKWELL_UI_DROP") is { Length: > 0 } drop)
            {
                Directory.CreateDirectory(drop);
                await File.WriteAllTextAsync(Path.Combine(drop, "settings.html"), settingsPage);
                await File.WriteAllTextAsync(Path.Combine(drop, "post-edit.html"), editor);
            }

            var token = System.Text.RegularExpressions.Regex.Match(editor, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["Title"] = before.Title + " CHANGED", ["Slug"] = before.Slug,
                ["Html"] = before.Html ?? "", ["submitAction"] = "draft", ["cf[zz-smoke-price]"] = "about three hundred",
            });
            var res = await client.PostAsync($"/admin/posts/edit/{post.Id}", form);
            var html = await res.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);                                   // the editor again, not a redirect
            Assert.Contains("Enter a number, such as 12 or 4.5.", html);
            Assert.Contains("value=\"about three hundred\"", html);                            // what was typed is kept
            Assert.Contains("aria-invalid=\"true\"", html);

            var after = (await posts.GetByIdAsync(post.Id, null))!;
            Assert.Equal(before.Title, after.Title);
            Assert.Equal("{\"zz-smoke-price\":\"from 40\"}", after.CustomFieldsJson);   // the planted value, untouched
        }
        finally
        {
            settings.CustomFields = original;
            await settingsRepo.SaveSettingsAsync(owner, settings);
            await SetCustomFieldsJsonAsync(post.Id, before.CustomFieldsJson);
        }
    }

    private async Task SetCustomFieldsJsonAsync(Guid postId, string? json)
    {
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE Posts SET CustomFieldsJson = @j WHERE Id = @id", conn);
        cmd.Parameters.AddWithValue("@j", (object?)json ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", postId);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Date_settings_narrow_to_a_country_and_preview_the_choice()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var client = AdminClient();

        var page = await client.GetStringAsync("/admin/settings");
        Assert.Contains("x-data='dateSettings(", page);
        Assert.Contains("name=\"DateFormat\"", page);
        Assert.Contains("<option value=\"IN\"", page);

        using var india = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/admin/settings/date-options?country=IN"));
        var zones = india.RootElement.GetProperty("zones").EnumerateArray().Select(z => z.GetProperty("value").GetString()).ToList();
        Assert.Contains("Asia/Kolkata", zones);
        Assert.DoesNotContain("America/New_York", zones);
        Assert.Equal("Asia/Kolkata", india.RootElement.GetProperty("suggestZone").GetString());
        var locales = india.RootElement.GetProperty("locales").EnumerateArray().Select(z => z.GetProperty("value").GetString()).ToList();
        Assert.Contains("en-IN", locales);
        Assert.Contains("hi-IN", locales);
        Assert.StartsWith(india.RootElement.GetProperty("suggestLocale").GetString()![..2], "en hi");   // the site's language there, else India's most used

        using var preview = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/admin/settings/date-preview?zone=Europe/Berlin&locale=de-DE&format=locale&clock=24h"));
        Assert.True(preview.RootElement.GetProperty("known").GetBoolean());
        Assert.Matches(@"^\d{1,2}\. \p{L}+ \d{4}$", preview.RootElement.GetProperty("long").GetString()!);
        using var unknown = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/admin/settings/date-preview?zone=Not/AZone&locale=en-US"));
        Assert.False(unknown.RootElement.GetProperty("known").GetBoolean());
    }

    [Fact]
    public async Task A_newsletter_draft_is_written_previewed_and_deleted_from_the_desk()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var client = AdminClient();
        var subject = "Smoke newsletter " + Guid.NewGuid().ToString("N")[..8];
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        try
        {
            var editor = await client.GetStringAsync("/admin/newsletter/new");
            var token = System.Text.RegularExpressions.Regex.Match(editor, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

            var empty = await client.PostAsync("/admin/newsletter/save", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["__RequestVerificationToken"] = token, ["subject"] = "" }));
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);                                          // back to the editor
            Assert.Contains("Give the newsletter a subject", await empty.Content.ReadAsStringAsync());

            var saved = await client.PostAsync("/admin/newsletter/save", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["__RequestVerificationToken"] = token, ["subject"] = subject, ["introMarkdown"] = "Hello **readers**" }));
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
            var location = saved.Headers.Location!.ToString();
            var id = Guid.Parse(location.TrimEnd('/').Split('/').Last());

            var page = await client.GetStringAsync(location);
            if (Environment.GetEnvironmentVariable("INKWELL_UI_DROP") is { Length: > 0 } uiDrop) await File.WriteAllTextAsync(Path.Combine(uiDrop, "newsletter-edit.html"), page);
            Assert.Contains("data-track-changes", page);
            Assert.Contains($"/admin/newsletter/{id}/preview", page);
            Assert.Contains("data-confirm-title=\"Send this newsletter?\"", page);

            var preview = await client.GetAsync($"/admin/newsletter/{id}/preview");
            Assert.Contains("sandbox", preview.Headers.GetValues("Content-Security-Policy").Single());
            var html = await preview.Content.ReadAsStringAsync();
            Assert.Contains(System.Net.WebUtility.HtmlEncode(subject), html);
            Assert.Contains("<strong>readers</strong>", html);
            Assert.DoesNotContain(Blog.Core.Services.NewsletterEmail.UnsubscribePlaceholder, html);

            var deleted = await client.PostAsync($"/admin/newsletter/{id}/delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
            Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(location)).StatusCode);
        }
        finally
        {
            await using var conn = new SqlConnection(cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("DELETE FROM NewsletterIssues WHERE Subject = @s", conn);
            cmd.Parameters.AddWithValue("@s", subject);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task List_paging_survives_a_search_and_a_page_number()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var html = await AdminClient().GetStringAsync("/admin/tags?q=e&page=1");
        Assert.Contains("role=\"search\"", html);
        Assert.Contains("value=\"e\"", html);
    }

    [Fact]
    public async Task Webhook_editor_and_delivery_log_render_for_an_existing_webhook()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<Blog.Core.Interfaces.IWebhookRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<Blog.Web.Services.Webhooks.WebhookPublisher>();
        var webhook = new Blog.Core.Domain.Webhook
        {
            Id = Guid.NewGuid(), OwnerId = Guid.Empty, Name = "smoke-" + Guid.NewGuid().ToString("N")[..6],
            Url = "https://hooks.example.com/smoke", SecretProtected = publisher.ProtectSecret("smoke-secret-0123456789"),
            Events = Blog.Core.Domain.WebhookEvents.PostPublished, IsEnabled = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        await repo.CreateAsync(webhook);
        await repo.CreateDeliveryAsync(new Blog.Core.Domain.WebhookDelivery
        {
            WebhookId = webhook.Id, OwnerId = Guid.Empty, EventId = Guid.NewGuid(), EventType = Blog.Core.Domain.WebhookEvents.PostPublished,
            PayloadJson = "{}", Status = Blog.Core.Domain.WebhookDeliveryStatus.Failed, NextAttemptAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
        });

        try
        {
            var client = AdminClient();
            var edit = await client.GetAsync($"/admin/webhooks/{webhook.Id}");
            var editHtml = await edit.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
            Assert.Contains("data-track-changes", editHtml);
            Assert.Contains("Leave blank to keep the current secret", editHtml);
            Assert.DoesNotContain("smoke-secret-0123456789", editHtml);   // the secret is never shown back

            var log = await client.GetAsync($"/admin/webhooks/{webhook.Id}/deliveries");
            var logHtml = await log.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, log.StatusCode);
            Assert.Contains("table-scroll", logHtml);
            Assert.Contains("Redeliver", logHtml);
            Assert.Contains("Failed", logHtml);

            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/webhooks/{Guid.NewGuid()}")).StatusCode);
        }
        finally
        {
            await repo.DeleteAsync(webhook.Id, Guid.Empty);
        }
    }

    private bool ProbeDb()
    {
        try
        {
            var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs)) return false;
            using var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            conn.Open();
            // The smoke user must be a real row: screens that seed per-user rows on first visit
            // (Theme settings) hit a foreign key to Users, which an invented id violates.
            using var cmd = new SqlCommand("SELECT TOP 1 Id FROM Users WHERE Role = N'Admin' ORDER BY CreatedAt", conn);
            if (cmd.ExecuteScalar() is Guid adminId) TestAdminAuthHandler.UserId = adminId;
            return true;
        }
        catch (Exception ex) { _out.WriteLine($"DB probe failed: {ex.Message}"); return false; }
    }

    /// <summary>An Admin with every permission claim the Desk policies check.</summary>
    private sealed class TestAdminAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestAdminSmoke";

        /// <summary>Resolved from the dev database by <see cref="ProbeDb"/>; falls back to an empty id.</summary>
        public static Guid UserId { get; set; } = Guid.Empty;

        public TestAdminAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, UserId.ToString()),
                new(ClaimTypes.Name, "test-admin"),
                new(ClaimTypes.Email, "test-admin@example.com"),
                new(ClaimTypes.Role, "Admin"),
            };
            foreach (var p in new[] { "posts.edit", "posts.publish", "posts.delete", "pages.manage", "comments.manage",
                                      "categories.manage", "tags.manage", "media.manage", "settings.manage", "themes.manage" })
                claims.Add(new Claim("Permission", p));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
