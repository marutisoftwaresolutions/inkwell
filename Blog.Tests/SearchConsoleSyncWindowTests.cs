using Blog.Web.Services.SearchConsole;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The date window the Search Console sync asks for is pure arithmetic on three inputs
/// (newest stored day, the clock, retention). These pin it: the first run backfills the whole
/// retention window, later runs re-pull the last three days, and a store that is already ahead
/// of the newest publishable day asks for nothing.
/// </summary>
public class SearchConsoleSyncWindowTests
{
    // 2026-09-19 03:15 UTC → Search Console publishes two days late, so the newest day asked for is 09-17.
    private static readonly DateTime Now = new(2026, 9, 19, 3, 15, 0, DateTimeKind.Utc);
    private static readonly DateTime Newest = new(2026, 9, 17);

    [Fact]
    public void Newest_day_is_the_clock_minus_the_publish_lag_with_the_time_of_day_dropped()
    {
        var w = SearchConsoleSyncJob.Window(null, Now, 16);

        Assert.Equal(2, SearchConsoleSyncJob.PublishLagDays);
        Assert.Equal(Newest, w.To);
        Assert.Equal(TimeSpan.Zero, w.To.TimeOfDay);
        Assert.Equal(w.To, SearchConsoleSyncJob.Window(null, Now.Date.AddHours(23).AddMinutes(59), 16).To);
    }

    [Fact]
    public void First_run_pulls_from_the_retention_floor_to_the_newest_day()
    {
        var w = SearchConsoleSyncJob.Window(latest: null, Now, retentionMonths: 16);

        // 16 months back from 09-17 is 2025-05-17; the floor is the day after, so exactly 16 months are kept.
        Assert.Equal(new DateTime(2025, 5, 18), w.Floor);
        Assert.Equal(w.Floor, w.From);
        Assert.Equal(Newest, w.To);
        Assert.False(w.IsEmpty);
    }

    [Fact]
    public void Incremental_run_refetches_the_last_three_days_before_the_newest_stored_day()
    {
        var latest = new DateTime(2026, 9, 10, 14, 30, 0, DateTimeKind.Utc); // time of day must not matter

        var w = SearchConsoleSyncJob.Window(latest, Now, 16);

        Assert.Equal(3, SearchConsoleSyncJob.RefetchDays);
        Assert.Equal(new DateTime(2026, 9, 7), w.From);
        Assert.Equal(Newest, w.To);
        Assert.False(w.IsEmpty);
    }

    [Fact]
    public void Incremental_run_never_starts_before_the_retention_floor()
    {
        // One month of retention: floor is 2026-08-18. The stored day minus three would be 08-16.
        var w = SearchConsoleSyncJob.Window(new DateTime(2026, 8, 19), Now, retentionMonths: 1);

        Assert.Equal(new DateTime(2026, 8, 18), w.Floor);
        Assert.Equal(w.Floor, w.From);
        Assert.Equal(Newest, w.To);
    }

    [Fact]
    public void A_store_that_is_ahead_of_the_newest_publishable_day_asks_for_nothing()
    {
        // Only possible if the clock went backwards or a manual import ran ahead; either way, no call.
        var w = SearchConsoleSyncJob.Window(Newest.AddDays(10), Now, 16);

        Assert.True(w.From > w.To);
        Assert.True(w.IsEmpty);
    }

    [Fact]
    public void A_store_that_is_current_still_re_pulls_the_revision_window()
    {
        var w = SearchConsoleSyncJob.Window(Newest, Now, 16);

        Assert.Equal(Newest.AddDays(-3), w.From);
        Assert.Equal(Newest, w.To);
        Assert.False(w.IsEmpty);
    }

    [Theory]
    [InlineData(0, 16)]    // unset → default
    [InlineData(-5, 16)]   // nonsense → default
    [InlineData(40, 16)]   // above what Search Console keeps → capped
    [InlineData(1, 1)]
    [InlineData(6, 6)]
    public void Retention_is_clamped_to_what_search_console_can_serve(int requested, int effectiveMonths)
    {
        var w = SearchConsoleSyncJob.Window(null, Now, requested);

        Assert.Equal(Newest.AddMonths(-effectiveMonths).AddDays(1), w.Floor);
    }
}
