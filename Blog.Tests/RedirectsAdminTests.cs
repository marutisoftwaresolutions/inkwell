using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
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
/// Renders Admin → Redirects and exercises the repository against the real schema. Also runs the
/// triage over the site's actual 404 log — the link suggester shipped a false-positive rule that
/// only surfaced against real data, and this is the same class of judgement.
///
/// Requires the dev database; skips cleanly when it is unreachable.
/// </summary>
public class RedirectsAdminTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public RedirectsAdminTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    private HttpClient AdminClient() =>
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

    [Theory]
    [InlineData("")]
    [InlineData("?tab=rules")]
    [InlineData("?tab=notfound")]
    [InlineData("?tab=nonsense")]   // unknown tab falls back rather than throwing
    public async Task The_screen_renders(string query)
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var response = await AdminClient().GetAsync("/admin/redirects" + query);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Redirects", html);
        Assert.DoesNotContain("An error occurred", html);
    }

    [Fact]
    public async Task It_is_not_reachable_without_signing_in()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var anon = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Assert.NotEqual(HttpStatusCode.OK, (await anon.GetAsync("/admin/redirects")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK,
            (await anon.PostAsync("/admin/redirects/save", new FormUrlEncodedContent(
                [new("from", "/x"), new("to", "/y"), new("statusCode", "301")]))).StatusCode);
    }

    [Fact]
    public async Task A_rule_can_be_listed_and_removed()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRedirectRepository>();

        var from = "/test-" + Guid.NewGuid().ToString("N")[..8];
        await repo.UpsertAsync(from, "/somewhere", RedirectStatus.MovedPermanently);

        var all = await repo.GetAllAsync();
        var mine = Assert.Single(all, r => r.From == from);
        Assert.Equal("/somewhere", mine.To);
        Assert.Equal(301, mine.StatusCode);
        Assert.NotEqual(default, mine.CreatedAt);   // the list orders by this

        Assert.True(await repo.DeleteAsync(from));
        Assert.False(await repo.DeleteAsync(from));   // removing twice is not an error path
        Assert.DoesNotContain(await repo.GetAllAsync(), r => r.From == from);
    }

    [Fact]
    public async Task A_path_that_already_has_a_rule_leaves_the_404_worklist()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRedirectRepository>();

        var before = await repo.GetUnhandledNotFoundsAsync();
        var target = before.FirstOrDefault();
        if (target is null) { _out.WriteLine("No 404s logged on dev — nothing to assert."); return; }

        await repo.UpsertAsync(target.Path, "", RedirectStatus.Gone);
        try
        {
            Assert.DoesNotContain(await repo.GetUnhandledNotFoundsAsync(), n => n.Path == target.Path);
        }
        finally
        {
            await repo.DeleteAsync(target.Path);
        }
    }

    [Fact]
    public async Task Triage_over_the_real_404_log_does_not_manufacture_suggestions()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRedirectRepository>();

        var groups = await repo.GetUnhandledNotFoundsAsync();
        var slugs  = await repo.GetPublishedSlugsAsync();
        var triaged = NotFoundTriage.Triage(groups, slugs);

        _out.WriteLine($"{groups.Count} unhandled 404 paths, {slugs.Count} published slugs");
        foreach (var c in triaged.Where(c => c.Kind != NotFoundKind.Unmatched))
            _out.WriteLine($"  {c.Kind,-9} {c.Path}  ->  {c.SuggestedSlug ?? c.Referer}  ({c.Similarity:P0}, {c.Hits} hits)");

        // Every suggestion must name a slug that actually exists, and clear the threshold. A
        // suggestion pointing at a post that is not published would redirect readers into a 404.
        foreach (var c in triaged.Where(c => c.SuggestedSlug is not null))
        {
            Assert.Contains(c.SuggestedSlug, slugs);
            Assert.True(c.Similarity >= NotFoundTriage.MinSimilarity,
                $"{c.Path} suggested {c.SuggestedSlug} at only {c.Similarity:P0}");
        }

        // A worklist bigger than the log itself would mean duplicates.
        Assert.Equal(groups.Count, triaged.Count);
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
        public const string SchemeName = "TestAdminRedirects";

        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString()),
                new Claim(ClaimTypes.Name, "test-admin"),
                new Claim(ClaimTypes.Role, "Admin")
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
