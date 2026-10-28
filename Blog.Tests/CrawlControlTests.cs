using System.Net;
using System.Text.RegularExpressions;
using Blog.Core.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Crawl control (ADR-0038). A live site took 7.77M AI-crawler requests in 30 days, almost all to "/" with
/// <c>?category=…&amp;tags=…</c> strings: every listing linked to ~62 filter combinations, each of which linked
/// to 62 more, and robots.txt gave each AI crawler its own <c>Allow: /</c> group, so none of them obeyed the
/// Disallow rules. These tests read robots.txt the way a crawler does (RFC 9309: the most specific group, the
/// longest matching rule, Allow on a tie) so they prove behaviour, not just text — and above all that no real
/// page becomes blocked for any search or AI crawler.
/// </summary>
public class CrawlControlTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string Robots = SeoDocuments.RobotsTxt("https://example.com");
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public CrawlControlTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    public static IEnumerable<object[]> AllowedBots() => SeoDocuments.AllowedCrawlers.Select(b => new object[] { b });

    private static readonly string[] RealPages =
    [
        "/", "/best-optical-software-2026", "/category/optical-retail-technology", "/tag/optical-pos",
        "/author/hitarth", "/series/getting-started", "/series", "/about", "/glossary", "/feed", "/sitemap.xml",
        "/llms.txt", "/llms-full.txt", "/?page=2", "/category/optical-retail-technology?page=2",
        "/uploads/features/best-optical-software-2026.jpg", "/css/inkwell.css", "/46e4db0682d84196be0d7e14045d2f12.txt",
        "/?utm_source=newsletter&utm_medium=email",
    ];

    private static readonly string[] NotForCrawlers =
    [
        "/?category=billing-revenue-cycle&tags=optosoft", "/?tags=optosoft,ai-diagnostics", "/?page=2&tags=optosoft",
        "/?category=a,b", "/?search=ehr", "/?q=ehr", "/search", "/search?q=ehr",
        "/admin/posts", "/account/login", "/setup/", "/api/v1/posts", "/preview/abc123",
    ];

    [Theory]
    [MemberData(nameof(AllowedBots))]
    public void Every_allowed_crawler_can_read_every_kind_of_real_page(string bot)
    {
        var blocked = RealPages.Where(p => !Robots_IsAllowed(Robots, bot, p)).ToList();
        Assert.True(blocked.Count == 0, $"{bot} is blocked from: {string.Join(", ", blocked)}");
    }

    [Theory]
    [MemberData(nameof(AllowedBots))]
    public void Every_allowed_crawler_obeys_the_private_search_and_filter_rules(string bot)
    {
        var allowed = NotForCrawlers.Where(p => Robots_IsAllowed(Robots, bot, p)).ToList();
        Assert.True(allowed.Count == 0, $"{bot} may still fetch: {string.Join(", ", allowed)}");
    }

    [Theory]
    [InlineData("CCBot")]
    [InlineData("Bytespider")]
    [InlineData("omgili")]
    public void Scrapers_are_refused_everything(string bot) =>
        Assert.False(Robots_IsAllowed(Robots, bot, "/best-optical-software-2026"));

    [Fact]
    public void An_unknown_crawler_gets_the_same_rules_as_the_named_ones()
    {
        Assert.True(Robots_IsAllowed(Robots, "SomeNewBot", "/best-optical-software-2026"));
        Assert.False(Robots_IsAllowed(Robots, "SomeNewBot", "/?tags=a,b"));
    }

    [Fact]
    public async Task Every_url_in_the_sitemap_stays_allowed_for_every_search_and_ai_crawler()
    {
        if (!DbAvailable()) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var client = _factory.CreateClient();
        var sitemap = await client.GetStringAsync("/sitemap.xml");
        var paths = Regex.Matches(sitemap, "<loc>([^<]+)</loc>").Select(m => new Uri(m.Groups[1].Value).PathAndQuery).ToList();
        Assert.NotEmpty(paths);
        _out.WriteLine($"{paths.Count} sitemap URLs checked against {SeoDocuments.AllowedCrawlers.Count} crawlers");

        var robots = await client.GetStringAsync("/robots.txt");
        var blocked = (from bot in SeoDocuments.AllowedCrawlers from p in paths where !Robots_IsAllowed(robots, bot, p) select $"{bot} {p}").ToList();
        Assert.True(blocked.Count == 0, string.Join(Environment.NewLine, blocked.Take(20)));
    }

    // ── Listing links and redirects ─────────────────────────────────────────────

    [Theory]
    [InlineData("category", "billing-revenue-cycle", new string[0], new[] { "optosoft" }, "/category/billing-revenue-cycle")]
    [InlineData("tags", "ai-diagnostics", new[] { "billing-revenue-cycle" }, new[] { "optosoft" }, "/tag/ai-diagnostics")]
    [InlineData("tags", "optosoft", new string[0], new[] { "optosoft" }, "/")]                     // active chip clears
    [InlineData("category", "Billing", new[] { "billing" }, new string[0], "/")]                  // case-insensitive
    [InlineData("tags", "bad slug!", new string[0], new string[0], "/")]                          // never echoes junk
    public void A_chip_links_to_its_own_archive_never_to_a_combination(string type, string slug, string[] cats, string[] tags, string expected) =>
        Assert.Equal(expected, ListingQuery.FacetUrl(type, slug, cats, tags));

    [Theory]
    [InlineData("billing-revenue-cycle", "optosoft", "/tag/optosoft")]
    [InlineData(null, "optosoft,ai-diagnostics", "/tag/ai-diagnostics")]
    [InlineData("a,b", null, "/category/b")]
    [InlineData("x", "bad slug!,", "/")]
    [InlineData(null, "optosoft", null)]        // single value: the existing single-value redirect handles it
    [InlineData("billing", null, null)]
    [InlineData(null, null, null)]
    public void A_filter_combination_collapses_to_one_archive(string? category, string? tags, string? expected) =>
        Assert.Equal(expected, ListingQuery.CollapseFacets(category, tags));

    [Fact]
    public async Task The_home_feed_answers_a_filter_combination_with_a_permanent_redirect()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/?category=billing-revenue-cycle&tags=optosoft,ai-diagnostics");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/tag/ai-diagnostics", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public void No_view_links_to_a_home_feed_filter_url()
    {
        var views = Path.Combine(RepoRoot(), "Blog.Web", "Views");
        var offenders = Directory.GetFiles(views, "*.cshtml", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
            .Where(x => Regex.IsMatch(x.line, @"href=""/\?(category|tags)="))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();
        Assert.True(offenders.Count == 0, "Link the archive route instead: " + string.Join(", ", offenders));
    }

    // ── A minimal RFC 9309 robots.txt evaluator ────────────────────────────────

    /// <summary>
    /// The group whose user-agent token equals the crawler's (case-insensitive), else <c>*</c>; within it the
    /// longest matching path pattern wins and Allow wins a tie. <c>*</c> matches any run of characters and a
    /// trailing <c>$</c> anchors the end, as RFC 9309 § 2.2 specifies.
    /// </summary>
    private static bool Robots_IsAllowed(string robots, string userAgent, string pathAndQuery)
    {
        var groups = new List<(List<string> Agents, List<(bool Allow, string Pattern)> Rules)>();
        List<string>? agents = null;
        List<(bool, string)>? rules = null;
        var lastWasAgent = false;
        foreach (var raw in robots.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            if (key == "user-agent")
            {
                if (!lastWasAgent) { agents = new(); rules = new(); groups.Add((agents, rules!)); }
                agents!.Add(value);
                lastWasAgent = true;
            }
            else if ((key == "allow" || key == "disallow") && rules is not null)
            {
                if (value.Length > 0) rules.Add((key == "allow", value));
                lastWasAgent = false;
            }
            else lastWasAgent = false;
        }

        var group = groups.FirstOrDefault(g => g.Agents.Any(a => a.Equals(userAgent, StringComparison.OrdinalIgnoreCase)));
        if (group.Agents is null) group = groups.FirstOrDefault(g => g.Agents.Contains("*"));
        if (group.Agents is null) return true;

        (bool Allow, int Length)? best = null;
        foreach (var (allow, pattern) in group.Rules)
        {
            var rx = "^" + Regex.Escape(pattern).Replace(@"\*", ".*");
            rx = rx.EndsWith(@"\$") ? rx[..^2] + "$" : rx;
            if (!Regex.IsMatch(pathAndQuery, rx)) continue;
            if (best is null || pattern.Length > best.Value.Length || (pattern.Length == best.Value.Length && allow))
                best = (allow, pattern.Length);
        }
        return best?.Allow ?? true;
    }

    private bool DbAvailable()
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

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
