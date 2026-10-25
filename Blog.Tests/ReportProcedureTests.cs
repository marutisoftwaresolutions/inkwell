using System.Text.RegularExpressions;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Admin → Analytics and Admin → AI Crawlers read through one stored procedure each (ADR-0037). Before that,
/// each page fired one ad-hoc query per section (eight and four), each scanning the whole table, and on a
/// production-sized database single sections ran past the 30-second command timeout, so the page returned
/// a server error. These tests keep the shape that fixed it:
///   - every procedure the code calls is created in all three schema places, so a fresh install or an
///     upgraded one never meets "Could not find stored procedure";
///   - the two controllers read only the report call, so a later section cannot quietly bring back a
///     per-section table scan;
///   - the Analytics procedure counts exactly what the dashboard's inline total counts (same content-page
///     rule) and returns zeros, not a mapping failure, for an empty window.
/// The database cases need the dev database and skip cleanly without it.
/// </summary>
public class ReportProcedureTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public ReportProcedureTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    // ── Source scans ─────────────────────────────────────────────────────────────

    private static readonly Regex ProcCall = new(@"""dbo\.(usp_\w+)""", RegexOptions.Compiled);

    [Fact]
    public void Every_stored_procedure_the_code_calls_is_created_in_all_three_schema_places()
    {
        var root = RepoRoot();
        var called = Directory.GetFiles(Path.Combine(root, "Blog.Infrastructure"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => ProcCall.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();
        Assert.NotEmpty(called);

        var migration = File.ReadAllText(Path.Combine(root, "Blog.Infrastructure", "Data", "MigrationService.cs"));
        var init      = File.ReadAllText(Path.Combine(root, "DBScripts", "init.sql"));
        var dated     = Directory.GetFiles(Path.Combine(root, "DBScripts"), "20??-??-??_*.sql").Select(File.ReadAllText).ToList();

        var missing = new List<string>();
        foreach (var proc in called)
        {
            var create = new Regex($@"CREATE\s+OR\s+ALTER\s+PROCEDURE\s+dbo\.{proc}\b", RegexOptions.IgnoreCase);
            if (!create.IsMatch(migration)) missing.Add($"{proc}: not created by MigrationService.cs");
            if (!create.IsMatch(init))      missing.Add($"{proc}: not created by DBScripts/init.sql");
            if (!dated.Any(create.IsMatch)) missing.Add($"{proc}: no dated DBScripts/YYYY-MM-DD_*.sql creates it");
        }
        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Theory]
    [InlineData("usp_Analytics_Report")]
    [InlineData("usp_CrawlerVisits_Report")]
    [InlineData("usp_CrawlerVisits_RollUp")]
    [InlineData("usp_PageViews_RollUp")]
    public void Procedure_text_is_identical_in_the_dated_script_MigrationService_and_init_sql(string proc)
    {
        var root = RepoRoot();
        var dated = Body(File.ReadAllText(Path.Combine(root, "DBScripts", "2026-10-22_create-report-stored-procedures.sql")), proc);
        Assert.False(string.IsNullOrEmpty(dated), $"{proc} not found in the dated script");

        Assert.Equal(dated, Body(File.ReadAllText(Path.Combine(root, "Blog.Infrastructure", "Data", "MigrationService.cs")), proc));
        // init.sql runs as one batch (a test executes it through Dapper), so it creates the procedure through
        // sp_executesql with its quotes doubled; un-double them to compare.
        Assert.Equal(dated, Body(File.ReadAllText(Path.Combine(root, "DBScripts", "init.sql")).Replace("''", "'"), proc));
    }

    /// <summary>From "CREATE OR ALTER PROCEDURE dbo.{proc}" to the first line that is exactly "END;", whitespace-normalised.</summary>
    private static string Body(string text, string proc)
    {
        var m = Regex.Match(text, $@"CREATE OR ALTER PROCEDURE dbo\.{proc}\b.*?^END;", RegexOptions.Singleline | RegexOptions.Multiline);
        return m.Success ? Regex.Replace(m.Value, @"\s+", " ") : "";
    }

    [Theory]
    [InlineData("Blog.Web/Controllers/AnalyticsController.cs", "_pageViews")]
    [InlineData("Blog.Web/Controllers/AiCrawlersController.cs", "_visits")]
    public void Report_screens_read_only_through_the_report_call(string rel, string field)
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), rel.Replace('/', Path.DirectorySeparatorChar)));
        var calls = Regex.Matches(src, $@"\b{field}\.(\w+)\(").Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.Equal(new[] { "GetReportAsync" }, calls);
    }

    // ── Against the real schema ──────────────────────────────────────────────────

    [Fact]
    public async Task Analytics_report_counts_content_pages_once_and_matches_the_dashboard_total()
    {
        if (!DbAvailable()) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPageViewRepository>();

        // An owner nobody else writes to, so live traffic cannot satisfy or disturb the assertions.
        var owner = Guid.NewGuid();
        var now = DateTime.UtcNow;
        PageView View(string path, string? source = null, string? referrer = null, string? campaign = null,
                      string? country = null, string? region = null) => new()
        {
            OwnerId = owner, Path = path, Source = source, Medium = source is null ? null : "organic",
            Referrer = referrer, Campaign = campaign, Country = country, Region = region, CreatedAt = now
        };

        try
        {
            await repo.RecordManyAsync(new[]
            {
                View("/", "google", "https://www.google.com/", null, "US", "Ohio"),
                View("/", null, null, "launch", "US", "Ohio"),
                View("/a-post", "google", "https://www.google.com/", null, "IN", null),
                View("/category/news"),     // not a content page: never counted
                View("/feed"),              // not a content page: never counted
            });

            var from = now.Date;
            var to = now.Date.AddDays(1);
            var report = await repo.GetReportAsync(owner, from, to, 20);

            Assert.Equal(3, report.Total);
            Assert.Equal(await repo.GetTotalAsync(owner, from, to), report.Total);
            Assert.Equal(3, Assert.Single(report.Daily).Count);

            Assert.Equal(new[] { ("/", 2), ("/a-post", 1) }, report.TopPages.Select(p => (p.Path, p.Views)));
            Assert.Contains(report.Sources, s => s.Source == "google" && s.Medium == "organic" && s.Count == 2);
            Assert.Contains(report.Sources, s => s.Source == "direct" && s.Medium == "none" && s.Count == 1);
            Assert.Equal(("https://www.google.com/", 2), (report.Referrers.Single().Referrer, report.Referrers.Single().Count));
            Assert.Equal(("launch", 1), (report.Campaigns.Single().Campaign, report.Campaigns.Single().Count));
            Assert.Equal(new[] { ("US", 2), ("IN", 1) }, report.Countries.Select(c => (c.Country, c.Count)));
            Assert.Equal(("Ohio", 2), (report.Regions.Single().Region, report.Regions.Single().Count));
        }
        finally
        {
            await using var conn = new SqlConnection(ConnectionString());
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("DELETE FROM PageViews WHERE OwnerId = @o", conn);
            cmd.Parameters.AddWithValue("@o", owner);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Analytics_report_for_an_empty_window_is_zero_not_a_failure()
    {
        if (!DbAvailable()) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPageViewRepository>();

        var report = await repo.GetReportAsync(Guid.NewGuid(), DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        Assert.Equal(0, report.Total);
        Assert.Empty(report.Daily);
        Assert.Empty(report.Sources);
        Assert.Empty(report.TopPages);
        Assert.Empty(report.Referrers);
        Assert.Empty(report.Campaigns);
        Assert.Empty(report.Countries);
        Assert.Empty(report.Regions);
    }

    [Fact]
    public async Task Crawler_report_is_the_same_before_and_after_the_daily_roll_up()
    {
        if (!DbAvailable()) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICrawlerVisitRepository>();

        var name = "RollupBot-" + Guid.NewGuid().ToString("N")[..8];
        var path = "/rollup-" + Guid.NewGuid().ToString("N")[..8];
        CrawlerVisit Visit(DateTime at, bool? verified) => new()
        {
            OwnerId = Guid.Empty, Crawler = name, Operator = "Test", IsAi = true, Path = path,
            StatusCode = 200, UserAgent = "test", VisitedAt = at, Verified = verified
        };

        try
        {
            // The summary is derived data: start it empty so this run summarises every day it covers.
            await ExecAsync("DELETE FROM CrawlerVisitDaily");
            var now = DateTime.UtcNow;
            await repo.RecordManyAsync(new[]
            {
                Visit(now.Date.AddDays(-3).AddHours(5), true),
                Visit(now.Date.AddDays(-3).AddHours(6), false),   // failed check: in the totals, out of pages and trend
                Visit(now.Date.AddDays(-2).AddHours(7), null),
                Visit(now, true),                                  // today: never summarised, always read raw
            });

            var raw = await repo.GetReportAsync(null, 30, take: 500);
            var (days, rows) = await repo.RollUpAsync(Blog.Web.Services.Jobs.CrawlerVisitRollupJob.LastFinishedDay(now), 1000);
            Assert.True(days >= 2 && rows >= 3, $"expected the two past days to be summarised, got {days} day(s), {rows} row(s)");
            var rolled = await repo.GetReportAsync(null, 30, take: 500);

            // Other test classes record visits for "now" in parallel, so compare what this test controls: its own
            // crawler and page, and the trend for days that are already over.
            object Shape(CrawlerReport r) => new
            {
                Activity = r.Activity.Where(a => a.Crawler == name)
                                     .Select(a => (a.Crawler, a.Visits, a.Pages, a.VerifiedVisits, a.FailedVisits, a.IsAi, a.LastSeen)).ToList(),
                Pages    = r.TopPages.Where(p => p.Path == path).Select(p => (p.Path, p.Visits, p.Crawlers)).ToList(),
                Daily    = r.Daily.Where(d => d.Day < now.Date).Select(d => (d.Day, d.Visits)).ToList(),
            };
            Assert.Equivalent(Shape(raw), Shape(rolled), strict: true);

            var mine = Assert.Single(rolled.Activity, a => a.Crawler == name);
            Assert.Equal((4, 1, 2, 1), (mine.Visits, mine.Pages, mine.VerifiedVisits, mine.FailedVisits));
            Assert.Equal(3, Assert.Single(rolled.TopPages, p => p.Path == path).Visits);

            // A second run has nothing new to do and must not double-count.
            await repo.RollUpAsync(Blog.Web.Services.Jobs.CrawlerVisitRollupJob.LastFinishedDay(now), 1000);
            Assert.Equal(4, Assert.Single((await repo.GetReportAsync(null, 30, take: 500)).Activity, a => a.Crawler == name).Visits);
        }
        finally
        {
            await ExecAsync("DELETE FROM CrawlerVisits WHERE Crawler = @n; DELETE FROM CrawlerVisitDaily WHERE Crawler = @n", name);
        }
    }

    [Fact]
    public async Task Analytics_report_is_the_same_before_and_after_the_daily_roll_up_and_after_pruning()
    {
        if (!DbAvailable()) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPageViewRepository>();

        // An owner nobody else writes to, so the whole report can be compared.
        var owner = Guid.NewGuid();
        var now = DateTime.UtcNow;
        PageView View(DateTime at, string path, string? source = null, string? referrer = null, string? campaign = null,
                      string? country = null, string? region = null) => new()
        {
            OwnerId = owner, Path = path, Source = source, Medium = source is null ? null : "organic",
            Referrer = referrer, Campaign = campaign, Country = country, Region = region, CreatedAt = at
        };
        var d3 = now.Date.AddDays(-3);
        var d2 = now.Date.AddDays(-2);

        try
        {
            await repo.RecordManyAsync(new[]
            {
                View(d3.AddHours(1), "/", "google", "https://www.google.com/", null, "US", "Ohio"),
                View(d3.AddHours(2), "/a-post", null, null, "launch", "IN", null),
                View(d3.AddHours(3), "/category/news"),                       // not a content page
                View(d2.AddHours(4), "/a-post", "google", "https://www.google.com/", "launch", "US", "Ohio"),
                View(d2.AddHours(5), "/b-post", "bing", null, null, null, null),
                View(now, "/", "google", null, null, "US", null),            // today: never summarised, always raw
            });

            // The summary is derived data: start it empty so this run summarises every day it covers.
            await ExecAsync("DELETE FROM PageViewDaily");
            var from = now.Date.AddDays(-29);
            var to = now.Date.AddDays(1);

            Assert.Equal(0, await repo.PruneAsync(owner, now, TimeSpan.FromMinutes(1)));   // nothing summarised yet: nothing may go
            var raw = await repo.GetReportAsync(owner, from, to, 20);

            var (days, _) = await repo.RollUpAsync(Blog.Web.Services.Jobs.CrawlerVisitRollupJob.LastFinishedDay(now), 1000);
            Assert.True(days >= 2, $"expected the two past days to be summarised, got {days}");
            var rolled = await repo.GetReportAsync(owner, from, to, 20);

            Assert.Equal(5, raw.Total);
            Assert.Equivalent(raw, rolled, strict: true);

            // Pruning removes the summarised days' raw views and keeps today's; the report does not change.
            Assert.Equal(5, await repo.PruneAsync(owner, now, TimeSpan.FromMinutes(1)));
            Assert.Equivalent(raw, await repo.GetReportAsync(owner, from, to, 20), strict: true);
        }
        finally
        {
            await using var conn = new SqlConnection(ConnectionString());
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("DELETE FROM PageViews WHERE OwnerId = @o; DELETE FROM PageViewDaily WHERE OwnerId = @o", conn);
            cmd.Parameters.AddWithValue("@o", owner);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData(0, 0)]        // the default keeps every view
    [InlineData(-5, 0)]
    [InlineData(7, 31)]       // the dashboard's 30-day figures read raw views
    [InlineData(90, 90)]
    [InlineData(99999, 3650)]
    public void Page_view_retention_is_off_by_default_and_never_below_a_month(int configured, int expected)
    {
        Assert.Equal(expected, Blog.Web.Services.Jobs.PageViewRetentionJob.ClampDays(configured));
        Assert.Equal(0, new UserSettings().PageViewRetentionDays);
    }

    [Theory]
    [InlineData("2026-10-22T00:30:00", "2026-10-20")]   // yesterday ended only 30 minutes ago: wait for late batches
    [InlineData("2026-10-22T01:00:00", "2026-10-21")]
    [InlineData("2026-10-22T23:59:00", "2026-10-21")]
    public void Roll_up_waits_an_hour_after_a_day_ends(string utcNow, string expected)
    {
        Assert.Equal(DateTime.Parse(expected), Blog.Web.Services.Jobs.CrawlerVisitRollupJob.LastFinishedDay(DateTime.Parse(utcNow)));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private async Task ExecAsync(string sql, string? name = null)
    {
        await using var conn = new SqlConnection(ConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        if (name is not null) cmd.Parameters.AddWithValue("@n", name);
        await cmd.ExecuteNonQueryAsync();
    }

    private string? ConnectionString() =>
        _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");

    private bool DbAvailable()
    {
        try
        {
            var cs = ConnectionString();
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
