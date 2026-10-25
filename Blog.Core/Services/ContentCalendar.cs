using System.Globalization;
using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// The Desk's month view of the publishing schedule (v1.0.9; MARKET-RESEARCH G14). Read-only: it places each post on
/// the day it went or goes live, in the site's display time zone, so "the 14th" means the 14th where the operator is.
/// Published posts use their publish date, scheduled posts their scheduled date, and a draft, a post in review or one
/// returned for changes appears as "planned" on the date it carries. Weeks start on Monday (ISO 8601). Pure.
/// </summary>
public static class ContentCalendar
{
    public enum Kind { Published, Scheduled, Planned }

    /// <summary>What the calendar needs from a post; the repository reads only these columns.</summary>
    public sealed record Source(Guid Id, string Title, string Slug, PostStatus Status, DateTime? PublishedAt, DateTime? ScheduledAt);

    public sealed record Entry(Guid Id, string Title, string Slug, Kind Kind, PostStatus Status, DateTime Local);

    public sealed record Day(DateOnly Date, bool InMonth, bool IsToday, IReadOnlyList<Entry> Entries);

    public sealed record Month(int Year, int MonthNumber, IReadOnlyList<IReadOnlyList<Day>> Weeks)
    {
        public DateOnly First => new(Year, MonthNumber, 1);
        public string Key => First.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        public string PreviousKey => First.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        public string NextKey => First.AddMonths(1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        public int Count(Kind kind) => Weeks.SelectMany(w => w).Where(d => d.InMonth).SelectMany(d => d.Entries).Count(e => e.Kind == kind);
        public IEnumerable<Day> DaysWithEntries => Weeks.SelectMany(w => w).Where(d => d.InMonth && d.Entries.Count > 0);
    }

    /// <summary>The month asked for as yyyy-MM, or the current month in the site's zone when it is missing or not a month.</summary>
    public static (int Year, int Month) ParseMonth(string? value, DateTime nowUtc, string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateOnly.TryParseExact(value.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            && d.Year is >= 2000 and <= 2100)
            return (d.Year, d.Month);
        var local = TimeZoneHelper.ToDisplay(nowUtc, timeZoneId);
        return (local.Year, local.Month);
    }

    /// <summary>The days the grid shows: Monday on or before the 1st to Sunday on or after the last day.</summary>
    public static (DateOnly First, DateOnly Last) GridDays(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        var end = last.AddDays(6 - (((int)last.DayOfWeek + 6) % 7));
        return (start, end);
    }

    /// <summary>The UTC range to query: the whole grid, with a day either side so no zone offset loses a post.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) QueryRangeUtc(int year, int month)
    {
        var (start, end) = GridDays(year, month);
        return (start.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), end.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }

    public static Month Build(int year, int month, IEnumerable<Source> posts, string? timeZoneId, DateTime nowUtc)
    {
        var entries = new List<Entry>();
        foreach (var p in posts)
        {
            var (kind, when) = p.Status switch
            {
                PostStatus.Published => (Kind.Published, p.PublishedAt),
                PostStatus.Scheduled => (Kind.Scheduled, p.ScheduledAt),
                PostStatus.Draft or PostStatus.InReview or PostStatus.NeedsChanges => (Kind.Planned, p.ScheduledAt),
                _ => (Kind.Planned, (DateTime?)null),
            };
            if (when is null) continue;
            entries.Add(new Entry(p.Id, string.IsNullOrWhiteSpace(p.Title) ? "(untitled)" : p.Title, p.Slug, kind, p.Status,
                TimeZoneHelper.ToDisplay(when.Value, timeZoneId)));
        }
        var byDay = entries.GroupBy(e => DateOnly.FromDateTime(e.Local)).ToDictionary(g => g.Key, g => (IReadOnlyList<Entry>)g.OrderBy(e => e.Local).ToList());

        var today = DateOnly.FromDateTime(TimeZoneHelper.ToDisplay(nowUtc, timeZoneId));
        var (startDay, endDay) = GridDays(year, month);
        var weeks = new List<IReadOnlyList<Day>>();
        for (var weekStart = startDay; weekStart <= endDay; weekStart = weekStart.AddDays(7))
            weeks.Add(Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i))
                .Select(d => new Day(d, d.Month == month && d.Year == year, d == today, byDay.GetValueOrDefault(d) ?? []))
                .ToList());
        return new Month(year, month, weeks);
    }
}
