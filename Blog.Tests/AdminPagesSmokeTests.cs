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
    [InlineData("/admin/security", "table-scroll")]
    [InlineData("/admin/audit", "desk-busy")]          // its table renders only once there are entries
    [InlineData("/admin/errors", "table-scroll")]
    [InlineData("/admin/dashboard", "desk-busy")]
    [InlineData("/admin/settings", "data-track-changes")]
    [InlineData("/admin/posts/create", "data-track-changes")]
    [InlineData("/admin/pages/create", "data-track-changes")]
    [InlineData("/admin/comments", "desk-busy")]
    [InlineData("/admin/analytics", "desk-busy")]
    [InlineData("/admin/ai-crawlers", "desk-busy")]
    [InlineData("/admin/content-health", "desk-busy")]
    [InlineData("/admin/link-audit", "desk-busy")]
    [InlineData("/admin/subscribers", "desk-busy")]
    [InlineData("/admin/theme", "desk-busy")]
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
    public async Task List_paging_survives_a_search_and_a_page_number()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var html = await AdminClient().GetStringAsync("/admin/tags?q=e&page=1");
        Assert.Contains("role=\"search\"", html);
        Assert.Contains("value=\"e\"", html);
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
