using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
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
/// Exercises the crawler-visit repository against the real schema and renders Admin → AI Crawlers.
/// The identifier is unit-tested elsewhere; what is untested without this is the part that only
/// fails at runtime — SQL that does not match the table, and a ViewBag the view casts wrongly.
///
/// Requires the dev database; skips cleanly when it is unreachable.
/// </summary>
public class AiCrawlerReportTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public AiCrawlerReportTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    private HttpClient EditorClient() =>
        _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddAuthentication(TestAuthHandler.SchemeName)
             .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            s.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme    = TestAuthHandler.SchemeName;
            });
        })).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ── Repository against the real table ─────────────────────────────────────

    [Fact]
    public async Task A_recorded_visit_comes_back_in_the_activity_totals()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICrawlerVisitRepository>();

        // A name no real crawler uses, so the assertion cannot be satisfied by live traffic.
        var name = "TestBot-" + Guid.NewGuid().ToString("N")[..8];
        var path = "/test-" + Guid.NewGuid().ToString("N")[..8];

        await repo.RecordAsync(new CrawlerVisit
        {
            OwnerId = Guid.Empty, Crawler = name, Operator = "Test", IsAi = true,
            Path = path, StatusCode = 200, UserAgent = "test", VisitedAt = DateTime.UtcNow
        });

        var activity = await repo.GetActivityAsync(null, 1);
        var mine = Assert.Single(activity, a => a.Crawler == name);

        Assert.Equal(1, mine.Visits);
        Assert.Equal(1, mine.Pages);
        Assert.True(mine.IsAi);
        Assert.NotNull(mine.LastSeen);

        var pages = await repo.GetTopPagesAsync(null, 1, aiOnly: true, take: 500);
        Assert.Contains(pages, p => p.Path == path);

        // The daily trend must map without a ValueTuple — this is the shape Dapper cannot fill by name.
        var daily = await repo.GetDailyAiVisitsAsync(null, 1);
        Assert.True(daily.Sum(d => d.Visits) >= 1);
    }

    [Fact]
    public async Task Empty_windows_return_zero_rather_than_failing_to_map()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICrawlerVisitRepository>();

        // A window that cannot contain rows: aggregates over nothing must not blow up on NULL.
        var activity = await repo.GetActivityAsync(Guid.NewGuid(), 30);

        Assert.Empty(activity);
    }

    // ── The admin screen ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("?days=7")]
    [InlineData("?days=90")]
    [InlineData("?days=0")]        // clamped, not divided by zero
    [InlineData("?days=99999")]    // clamped, not a full-table scan
    public async Task The_report_renders_for_any_window(string query)
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var response = await EditorClient().GetAsync("/admin/ai-crawlers" + query);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("AI Crawlers", html);
        Assert.DoesNotContain("An error occurred", html);
    }

    [Fact]
    public async Task Engines_that_never_visited_are_listed_as_zero_not_omitted()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        // "No AI engine is reading you" is the single most useful finding this page can report,
        // and it is only visible if absent crawlers are still on the page.
        var html = await EditorClient().GetStringAsync("/admin/ai-crawlers?days=7");

        Assert.Contains("GPTBot", html);
        Assert.Contains("ClaudeBot", html);
        Assert.Contains("PerplexityBot", html);
        Assert.Contains("Googlebot", html);
    }

    [Fact]
    public async Task It_is_not_reachable_without_signing_in()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var anon = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await anon.GetAsync("/admin/ai-crawlers");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
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

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestEditorAiCrawlers";

        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString()),
                new Claim(ClaimTypes.Name, "test-editor"),
                new Claim(ClaimTypes.Role, "Editor")
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
