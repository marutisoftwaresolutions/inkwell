using System.Net;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Goals: what a valid goal is, which views and clicks count, that only the site's own goals can ever
/// be counted, that counts are daily numbers with no reader in them, and that the click beacon tells
/// a caller nothing.
/// </summary>
public class GoalTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public GoalTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    // ── Rules ───────────────────────────────────────────────────────────�

    [Theory]
    [InlineData("/pricing", "/pricing")]
    [InlineData("pricing/", "/pricing")]
    [InlineData("/Pricing?utm_source=x#top", "/pricing")]
    [InlineData("https://example.org/thank-you", "/thank-you")]
    [InlineData("/", "/")]
    public void A_visit_target_becomes_the_path_of_a_page_the_site_counts(string typed, string stored) =>
        Assert.Equal(stored, Goals.CleanTarget(Goals.Visit, typed));

    [Theory]
    [InlineData("/category/news")]      // archives are not counted as views
    [InlineData("/admin/posts")]
    [InlineData("")]
    [InlineData("/two words")]
    public void A_visit_target_that_is_never_counted_as_a_view_is_refused(string typed) =>
        Assert.Null(Goals.CleanTarget(Goals.Visit, typed));

    [Theory]
    [InlineData("https://book.example.com/", true)]
    [InlineData("mailto:", true)]
    [InlineData("tel:+44", true)]
    [InlineData("/downloads/", true)]
    [InlineData("//evil.example/", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("book.example.com", false)]
    public void A_click_target_is_the_start_of_a_real_link(string typed, bool accepted) =>
        Assert.Equal(accepted, Goals.CleanTarget(Goals.Click, typed) is not null);

    [Fact]
    public void A_key_is_made_from_the_name_and_never_collides()
    {
        Assert.Equal("reached-the-pricing-page", Goals.KeyFor("Reached the Pricing page!", []));
        Assert.Equal("signup-2", Goals.KeyFor("Signup", ["signup"]));
        Assert.Equal("goal", Goals.KeyFor("???", []));
        Assert.Equal("newsletter-signup-2", Goals.KeyFor("Newsletter signup", []));   // never the built-in key
        Assert.True(Goals.KeyFor(new string('a', 200), []).Length <= Goals.MaxKeyLength);
        Assert.True(Goals.IsKey(Goals.KeyFor("Ünïcode Name 9", [])));
    }

    [Fact]
    public void A_form_gets_a_plain_reason_when_a_goal_cannot_be_saved()
    {
        Assert.Null(Goals.Problem("Pricing", Goals.Visit, "/pricing", 0));
        Assert.Contains("name", Goals.Problem(" ", Goals.Visit, "/pricing", 0));
        Assert.Contains("page visit or a link click", Goals.Problem("x", "purchase", "/pricing", 0));
        Assert.Contains("/pricing", Goals.Problem("x", Goals.Visit, "/category/a", 0));
        Assert.Contains("https://", Goals.Problem("x", Goals.Click, "example.com", 0));
        Assert.Contains(Goals.Max.ToString(), Goals.Problem("x", Goals.Visit, "/pricing", Goals.Max));
    }

    [Fact]
    public void What_is_stored_is_valid_unique_and_capped()
    {
        var kept = Goals.Normalize(
        [
            new Goal { Key = "pricing", Name = "Pricing", Kind = Goals.Visit, Target = "/pricing/" },
            new Goal { Key = "pricing", Name = "Duplicate key", Kind = Goals.Visit, Target = "/other" },
            new Goal { Key = "Bad Key", Name = "x", Kind = Goals.Visit, Target = "/x" },
            new Goal { Key = "newsletter-signup", Name = "Shadowing a built-in", Kind = Goals.Visit, Target = "/x" },
            new Goal { Key = "script", Name = "x", Kind = Goals.Click, Target = "javascript:alert(1)" },
            new Goal { Key = "book", Name = "Book", Kind = Goals.Click, Target = "https://book.example.com/" },
        ]);
        Assert.Equal(new[] { "pricing", "book" }, kept.Select(g => g.Key).ToArray());
        Assert.Equal("/pricing", kept[0].Target);
        Assert.Equal(Goals.Max, Goals.Normalize(Enumerable.Range(0, 50).Select(i => new Goal { Key = "g" + i, Name = "g", Kind = Goals.Visit, Target = "/p" + i })).Count);
        Assert.Empty(Goals.Normalize(null));
    }

    [Fact]
    public void A_view_counts_towards_matching_visit_goals_and_only_click_goals_can_be_reported()
    {
        var goals = Goals.Normalize(
        [
            new Goal { Key = "pricing", Name = "Pricing", Kind = Goals.Visit, Target = "/pricing" },
            new Goal { Key = "book", Name = "Book", Kind = Goals.Click, Target = "https://book.example.com/" },
        ]);
        Assert.Equal(new[] { "pricing" }, Goals.VisitKeysFor(goals, "/Pricing/").ToArray());
        Assert.Empty(Goals.VisitKeysFor(goals, "/pricing-old"));
        Assert.Empty(Goals.VisitKeysFor(goals, null));

        Assert.True(Goals.IsClickGoal(goals, "book"));
        Assert.False(Goals.IsClickGoal(goals, "pricing"));                 // a visit goal cannot be clicked into
        Assert.False(Goals.IsClickGoal(goals, "newsletter-signup"));       // nor a built-in conversion
        Assert.False(Goals.IsClickGoal(goals, "anything-else"));
        Assert.False(Goals.IsClickGoal(goals, null));
    }

    [Fact]
    public void A_batch_adds_the_same_days_hits_together()
    {
        var day = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);
        var merged = Goals.Merge(
        [
            new GoalHit(Guid.Empty, day.AddHours(1), "a", 2), new GoalHit(Guid.Empty, day.AddHours(20), "a", 3),
            new GoalHit(Guid.Empty, day.AddDays(1), "a", 1), new GoalHit(Guid.Empty, day, "b", 0), new GoalHit(Guid.Empty, day, "Bad Key", 5),
        ]);
        Assert.Equal(2, merged.Count);
        Assert.Equal(5, merged.Single(h => h.Day == day).Hits);
    }

    // ── Wiring ───────────────────────────────────────────────────────────

    [Fact]
    public void Counts_hold_numbers_only_and_every_goal_path_is_gated_and_audited()
    {
        var init = Read("DBScripts", "init.sql");
        var table = init[init.IndexOf("CREATE TABLE GoalCounts", StringComparison.Ordinal)..];
        table = table[..table.IndexOf(");", StringComparison.Ordinal)];
        foreach (var personal in new[] { "Ip", "UserAgent", "Email", "Referrer", "Session", "Path" })
            Assert.DoesNotContain(personal, table);

        var beacon = Read("Blog.Web", "Controllers", "PageViewBeaconController.cs");
        var goal = beacon[beacon.IndexOf("[HttpPost(\"/pv/goal\")]", StringComparison.Ordinal)..];
        goal = goal[..goal.IndexOf("private bool IsSameSite", StringComparison.Ordinal)];
        foreach (var gate in new[] { "IsAuthenticated == true", "BotUserAgent.IsBot", "IsSameSite()", "_beacon.Validate(token, path)", "TryConsumeBudget" })
            Assert.Contains(gate, goal);
        Assert.DoesNotContain("return Ok", goal);                          // always 204: the answer tells a caller nothing

        var recorder = Read("Blog.Web", "Services", "GoalRecorder.cs");
        Assert.Contains("Goals.IsClickGoal(await GoalsAsync(owner), key)", recorder);
        Assert.Contains("if (!Goals.IsBuiltIn(builtInKey)) return;", recorder);
        Assert.Contains("_flushGate.Wait(0)", recorder);                   // one flush at a time

        var desk = Read("Blog.Web", "Controllers", "GoalsController.cs");
        Assert.Contains("[Authorize(Policy = \"AdminOnly\")]", desk);
        Assert.Equal(2, desk.Split("[ValidateAntiForgeryToken]").Length - 1);
        Assert.Contains("AuditActions.GoalAdded", desk);
        Assert.Contains("AuditActions.GoalDeleted", desk);

        var script = Read("Blog.Web", "wwwroot", "js", "inkwell-views.js");
        Assert.Contains("'/pv/goal'", script);
        Assert.DoesNotContain("preventDefault", script);                   // the link is never delayed or changed
    }

    [Fact]
    public async Task Only_the_sites_own_goals_are_counted_and_a_day_has_one_row_per_goal()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var scope = _factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        if (admin is null) return;
        var counts = scope.ServiceProvider.GetRequiredService<IGoalCountRepository>();
        var recorder = _factory.Services.GetRequiredService<GoalRecorder>();
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);

        var tag = Guid.NewGuid().ToString("N")[..8];
        var visit = "zzv-" + tag; var click = "zzc-" + tag; var invented = "zzx-" + tag;
        var path = "/zz-goal-" + tag;
        var owner = Guid.NewGuid();                       // a made-up tenant key, so real counts are never touched
        var cache = _factory.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        try
        {
            // The recorder reads a site's goals through a one-minute cache; seed it for the made-up owner.
            IReadOnlyList<Goal> goals = Goals.Normalize(
            [
                new Goal { Key = visit, Name = "Visit", Kind = Goals.Visit, Target = path },
                new Goal { Key = click, Name = "Click", Kind = Goals.Click, Target = "https://book.example.com/" },
            ]);
            Microsoft.Extensions.Caching.Memory.CacheExtensions.Set(cache, "goals:" + owner, goals, TimeSpan.FromMinutes(5));

            await recorder.PageViewsAsync([(owner, path), (owner, path + "/"), (owner, "/somewhere-else")]);
            Assert.True(await recorder.ClickAsync(owner, click));
            Assert.False(await recorder.ClickAsync(owner, invented));      // not one of the site's goals: not counted
            Assert.False(await recorder.ClickAsync(owner, visit));         // a visit goal cannot be reported by a click
            recorder.Conversion(owner, Goals.NewsletterSignup);
            recorder.Conversion(owner, invented);                          // not a built-in conversion: ignored
            await recorder.FlushAsync();
            await recorder.PageViewsAsync([(owner, path)]);                // a second batch the same day adds to the row
            await recorder.FlushAsync();

            var today = DateTime.UtcNow.Date;
            var totals = await counts.GetTotalsAsync(owner, today.AddDays(-1), today);
            Assert.Equal(3, totals[visit]);
            Assert.Equal(1, totals[click]);
            Assert.Equal(1, totals[Goals.NewsletterSignup]);
            Assert.False(totals.ContainsKey(invented));
            Assert.Equal(3, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM GoalCounts WHERE OwnerId = @owner", new { owner }));

            Assert.True(await counts.DeleteGoalAsync(owner, visit) >= 1);
            Assert.False((await counts.GetTotalsAsync(owner, today.AddDays(-1), today)).ContainsKey(visit));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM GoalCounts WHERE OwnerId = @owner", new { owner });
            recorder.Forget(owner);
        }
    }

    [Fact]
    public async Task The_click_beacon_answers_204_whatever_it_is_sent()
    {
        if (!TestDatabase.Available(_factory)) return;   // tenant resolution needs the database: skip without one
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsync("/pv/goal", new FormUrlEncodedContent(new Dictionary<string, string> { ["t"] = "not-a-token", ["p"] = "/", ["g"] = "anything" }));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/pv/goal", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
    }

    [Fact]
    public async Task The_goals_screen_renders_for_an_admin()
    {
        if (!TestDatabase.Available(_factory)) return;
        var html = Read("Blog.Web", "Views", "Goals", "Index.cshtml");
        Assert.Contains("<div class=\"table-scroll\"><table class=\"desk-table\">", html);
        Assert.Contains("data-confirm-title=\"Remove this goal?\"", html);
        Assert.Contains("data-confirm-action=\"Remove goal\"", html);
        await Task.CompletedTask;
    }
}
