using System.Net;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// A date range of one's own on the Goals screen, and the CSV download: what a typed range becomes,
/// what the file holds, and that a goal's name cannot become a spreadsheet formula.
/// </summary>
public class GoalRangeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public GoalRangeTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static readonly DateTime Today = new(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void A_typed_range_is_read_swapped_when_reversed_and_kept_out_of_the_future()
    {
        Assert.Equal((D(2026, 10, 1), D(2026, 10, 31)), Goals.Range("2026-10-01", "2026-10-31", Today));
        Assert.Equal((D(2026, 10, 1), D(2026, 10, 31)), Goals.Range("2026-10-31", "2026-10-01", Today));
        Assert.Equal((D(2026, 11, 1), D(2026, 11, 13)), Goals.Range("2026-11-01", "2027-01-01", Today));
        Assert.Equal((D(2026, 11, 13), D(2026, 11, 13)), Goals.Range("2027-02-01", "2027-03-01", Today));
    }

    [Fact]
    public void A_range_longer_than_a_year_keeps_its_end()
    {
        var range = Goals.Range("2020-01-01", "2026-11-13", Today)!.Value;
        Assert.Equal(D(2026, 11, 13), range.To);
        Assert.Equal(Goals.MaxRangeDays, (range.To - range.From).Days + 1);
    }

    [Theory]
    [InlineData(null, "2026-10-31")]
    [InlineData("2026-10-01", "")]
    [InlineData("01/10/2026", "2026-10-31")]
    [InlineData("yesterday", "today")]
    public void A_half_typed_or_unreadable_range_is_no_range(string? from, string? to) =>
        Assert.Null(Goals.Range(from, to, Today));

    [Fact]
    public void The_download_has_one_line_per_day_and_goal_and_a_name_cannot_become_a_formula()
    {
        var names = new Dictionary<string, string> { ["pricing"] = "Reached pricing, twice", ["evil"] = "=HYPERLINK(\"http://x\")" };
        var csv = Goals.Csv(
        [
            new GoalHit(Guid.Empty, D(2026, 11, 2), "pricing", 3),
            new GoalHit(Guid.Empty, D(2026, 11, 1), "pricing", 5),
            new GoalHit(Guid.Empty, D(2026, 11, 1), "evil", 1),
            new GoalHit(Guid.Empty, D(2026, 11, 1), "removed-goal", 2),
        ], names);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Date,Goal,Count", lines[0]);
        Assert.Equal(5, lines.Length);
        Assert.Equal("2026-11-01,\"'=HYPERLINK(\"\"http://x\"\")\",1", lines[1]);          // neutralised, then quoted
        Assert.Equal("2026-11-01,\"Reached pricing, twice\",5", lines[2]);
        Assert.Equal("2026-11-01,removed-goal,2", lines[3]);                              // a goal since removed keeps its key
        Assert.StartsWith("2026-11-02,", lines[4]);                                       // oldest day first
    }

    [Fact]
    public async Task The_screen_adds_a_column_for_the_range_and_the_download_covers_it()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var factory = _factory.WithWebHostBuilder(b => Microsoft.AspNetCore.TestHost.WebHostBuilderExtensions.ConfigureTestServices(b, s =>
        {
            Microsoft.Extensions.DependencyInjection.AuthenticationServiceCollectionExtensions.AddAuthentication(s, AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName)
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
        var cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);

        // A count dated two years ago under the site's key: outside the 7 and 30 day columns, inside a chosen range, harmless to today's numbers.
        var day = new DateTime(2024, 3, 5);
        const string key = Goals.FormSubmitted;
        try
        {
            await conn.ExecuteAsync("DELETE FROM GoalCounts WHERE OwnerId = @o AND Day = @day AND GoalKey = @key; INSERT INTO GoalCounts (OwnerId, Day, GoalKey, Hits) VALUES (@o, @day, @key, 4321)", new { o = Guid.Empty, day, key });
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var plain = await client.GetStringAsync("/admin/analytics/goals");
            Assert.DoesNotContain("4,321", plain);
            Assert.Contains("href=\"/admin/analytics/goals/export\"", plain);

            var ranged = await client.GetStringAsync("/admin/analytics/goals?from=2024-03-10&to=2024-03-01");      // reversed on purpose
            Assert.Contains("2024-03-01 to 2024-03-10", ranged);
            Assert.Contains("4,321", ranged);
            Assert.Contains("/admin/analytics/goals/export?from=2024-03-01&amp;to=2024-03-10", ranged);

            var response = await client.GetAsync("/admin/analytics/goals/export?from=2024-03-01&to=2024-03-10");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("goals-2024-03-01-to-2024-03-10.csv", response.Content.Headers.ContentDisposition?.ToString());
            var csv = await response.Content.ReadAsStringAsync();
            Assert.Contains("2024-03-05,Form submissions,4321", csv);

            Assert.DoesNotContain("4321", await client.GetStringAsync("/admin/analytics/goals/export"));          // the default is the last 30 days
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM GoalCounts WHERE OwnerId = @o AND Day = @day AND GoalKey = @key", new { o = Guid.Empty, day, key });
        }
    }
}
