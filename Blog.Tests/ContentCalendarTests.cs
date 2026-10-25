using Blog.Core.Domain;
using Blog.Core.Services;
using Xunit;
using K = Blog.Core.Services.ContentCalendar.Kind;

namespace Blog.Tests;

/// <summary>The Desk's content calendar (v1.0.9, MARKET-RESEARCH G14): placement by status, time zone and week shape.</summary>
public class ContentCalendarTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    private static ContentCalendar.Source P(string title, PostStatus status, DateTime? published = null, DateTime? scheduled = null) =>
        new(Guid.NewGuid(), title, title.ToLowerInvariant(), status, published, scheduled);

    private static IEnumerable<ContentCalendar.Entry> On(ContentCalendar.Month m, int day) =>
        m.Weeks.SelectMany(w => w).Single(d => d.InMonth && d.Date.Day == day).Entries;

    [Fact]
    public void Weeks_start_on_monday_and_cover_whole_weeks()
    {
        // October 2026 starts on a Thursday and ends on a Saturday.
        var (first, last) = ContentCalendar.GridDays(2026, 10);
        Assert.Equal(new DateOnly(2026, 9, 28), first);
        Assert.Equal(DayOfWeek.Monday, first.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 11, 1), last);
        var month = ContentCalendar.Build(2026, 10, [], "UTC", Now);
        Assert.Equal(5, month.Weeks.Count);
        Assert.All(month.Weeks, w => Assert.Equal(7, w.Count));
        Assert.Equal(31, month.Weeks.SelectMany(w => w).Count(d => d.InMonth));
        Assert.True(month.Weeks.SelectMany(w => w).Single(d => d.Date == new DateOnly(2026, 10, 15)).IsToday);
    }

    [Fact]
    public void Each_status_lands_on_its_own_date_and_undated_drafts_are_left_out()
    {
        var month = ContentCalendar.Build(2026, 10, new[]
        {
            P("Live", PostStatus.Published, published: new DateTime(2026, 10, 2, 8, 0, 0), scheduled: new DateTime(2026, 9, 1)),
            P("Queued", PostStatus.Scheduled, scheduled: new DateTime(2026, 10, 20, 7, 0, 0)),
            P("Idea", PostStatus.Draft, scheduled: new DateTime(2026, 10, 20, 6, 0, 0)),
            P("Waiting", PostStatus.InReview, scheduled: new DateTime(2026, 10, 21)),
            P("Someday", PostStatus.Draft),
        }, "UTC", Now);

        Assert.Equal(K.Published, Assert.Single(On(month, 2)).Kind);
        Assert.Equal(new[] { "Idea", "Queued" }, On(month, 20).Select(e => e.Title));      // in time order
        Assert.Equal(K.Planned, On(month, 20).First().Kind);
        Assert.Equal(K.Planned, Assert.Single(On(month, 21)).Kind);
        Assert.Equal((1, 1, 2), (month.Count(K.Published), month.Count(K.Scheduled), month.Count(K.Planned)));
        Assert.DoesNotContain(month.Weeks.SelectMany(w => w).SelectMany(d => d.Entries), e => e.Title == "Someday");
    }

    [Fact]
    public void A_post_is_placed_on_the_day_it_is_where_the_site_is()
    {
        // 23:30 UTC on 31 October is already 1 November in India.
        var late = P("Late", PostStatus.Published, published: new DateTime(2026, 10, 31, 23, 30, 0, DateTimeKind.Utc));
        Assert.Single(On(ContentCalendar.Build(2026, 10, [late], "UTC", Now), 31));
        var india = ContentCalendar.Build(2026, 11, [late], "Asia/Kolkata", Now);
        var entry = Assert.Single(On(india, 1));
        Assert.Equal(new DateTime(2026, 11, 1, 5, 0, 0), entry.Local);
        // The query range reaches a day past the grid on either side, so no offset drops it.
        var (from, to) = ContentCalendar.QueryRangeUtc(2026, 11);
        Assert.InRange(late.PublishedAt!.Value, from, to);
    }

    [Theory]
    [InlineData("2026-02", 2026, 2)]
    [InlineData(" 2027-12 ", 2027, 12)]
    [InlineData("2026-13", 2026, 10)]
    [InlineData("1999-01", 2026, 10)]
    [InlineData("<script>", 2026, 10)]
    [InlineData(null, 2026, 10)]
    public void An_unusable_month_falls_back_to_this_month(string? value, int year, int month) =>
        Assert.Equal((year, month), ContentCalendar.ParseMonth(value, Now, "UTC"));

    [Fact]
    public void Previous_and_next_cross_the_year()
    {
        var jan = ContentCalendar.Build(2027, 1, [], "UTC", Now);
        Assert.Equal("2026-12", jan.PreviousKey);
        Assert.Equal("2027-02", jan.NextKey);
    }
}
