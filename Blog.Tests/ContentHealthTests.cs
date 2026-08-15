using System.Net;
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
/// The maintenance rules behind Admin → Content Health. They decide what an operator is told to fix,
/// so a wrong flag either creates busywork or hides a real problem.
/// </summary>
public class ContentHealthItemTests
{
    private static ContentHealthItem Item(string title = "A post") => new()
    {
        Title = title,
        Slug = "a-post",
        HasKeyFacts = true,
        HasFaq = true,
        MetaLength = 140,
        LastVerifiedAt = DateTime.UtcNow.AddDays(-10),
        NextReviewAt = DateTime.UtcNow.AddMonths(5)
    };

    [Fact]
    public void A_healthy_post_needs_no_attention() =>
        Assert.False(Item().NeedsAttention);

    [Fact]
    public void An_unverified_post_is_flagged()
    {
        var i = Item();
        i.LastVerifiedAt = null;

        Assert.True(i.NeverVerified);
        Assert.True(i.NeedsAttention);
    }

    [Fact]
    public void An_overdue_review_is_flagged_but_a_future_one_is_not()
    {
        var overdue = Item();
        overdue.NextReviewAt = DateTime.UtcNow.AddDays(-1);
        Assert.True(overdue.ReviewOverdue);

        Assert.False(Item().ReviewOverdue);
    }

    [Fact]
    public void A_review_inside_30_days_is_due_soon_and_not_yet_overdue()
    {
        var i = Item();
        i.NextReviewAt = DateTime.UtcNow.AddDays(10);

        Assert.True(i.ReviewDueSoon);
        Assert.False(i.ReviewOverdue);
    }

    [Theory]
    [InlineData(0, true, false)]      // missing
    [InlineData(140, false, false)]   // fine
    [InlineData(155, false, false)]   // exactly at the limit
    [InlineData(156, false, true)]    // truncates
    public void Snippet_length_is_judged_against_the_serp_limit(int length, bool missing, bool tooLong)
    {
        var i = Item();
        i.MetaLength = length;

        Assert.Equal(missing, i.MetaMissing);
        Assert.Equal(tooLong, i.MetaTooLong);
    }

    [Fact]
    public void A_title_promising_a_past_year_is_stale()
    {
        var past = Item($"Best Optical Software {DateTime.UtcNow.Year - 1}");
        Assert.True(past.StaleYearStamp);
    }

    [Fact]
    public void A_current_or_future_year_is_not_stale()
    {
        Assert.False(Item($"Best Optical Software {DateTime.UtcNow.Year}").StaleYearStamp);
        Assert.False(Item($"Best Optical Software {DateTime.UtcNow.Year + 1}").StaleYearStamp);
    }

    [Theory]
    [InlineData("Top 5 Optical POS Systems")]       // a count, not a year
    [InlineData("ICD-10 Coding for Optometry")]     // a code containing digits
    [InlineData("A post with no numbers at all")]
    public void Numbers_that_are_not_years_never_trigger_the_stale_flag(string title) =>
        Assert.False(Item(title).StaleYearStamp);

    [Fact]
    public void Summary_counts_match_the_items()
    {
        var healthy = Item();
        var noKeyFacts = Item(); noKeyFacts.HasKeyFacts = false;
        var overdue = Item(); overdue.NextReviewAt = DateTime.UtcNow.AddDays(-5);

        var s = ContentHealthSummary.From(new[] { healthy, noKeyFacts, overdue });

        Assert.Equal(3, s.Total);
        Assert.Equal(1, s.MissingKeyFacts);
        Assert.Equal(1, s.ReviewOverdue);
        Assert.Equal(1, s.Healthy);
    }
}

/// <summary>Requires the dev database; skips cleanly when it is unreachable.</summary>
public class ContentHealthRepositoryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public ContentHealthRepositoryTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    [Fact]
    public async Task It_returns_published_posts_with_their_health_fields()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IContentHealthRepository>();

        var items = await repo.GetPublishedAsync();

        _out.WriteLine($"published posts: {items.Count}");
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Slug)));

        var summary = ContentHealthSummary.From(items);
        Assert.Equal(items.Count, summary.Total);
    }

    [Fact]
    public async Task The_dashboard_requires_a_signed_in_editor()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/admin/content-health");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.OriginalString ?? "");
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
}
