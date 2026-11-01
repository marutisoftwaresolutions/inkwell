using Blog.Core.Domain;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>Schedule arithmetic for Admin → Scheduled scripts (ADR-0039). UTC throughout.</summary>
public class ScriptScheduleTests
{
    private static readonly DateTime Tue1030 = new(2026, 11, 3, 10, 30, 0, DateTimeKind.Utc); // Tuesday

    [Fact]
    public void Hourly_runs_at_the_minute_next_hour_when_minute_has_passed()
    {
        var s = new ScheduledScript { Frequency = ScriptFrequencies.Hourly, TimeOfDayUtc = new TimeSpan(0, 5, 0) };
        Assert.Equal(new DateTime(2026, 11, 3, 11, 5, 0, DateTimeKind.Utc), ScriptSchedule.NextRun(s, Tue1030));
    }

    [Fact]
    public void Hourly_runs_later_this_hour_when_minute_is_still_ahead()
    {
        var s = new ScheduledScript { Frequency = ScriptFrequencies.Hourly, TimeOfDayUtc = new TimeSpan(0, 45, 0) };
        Assert.Equal(new DateTime(2026, 11, 3, 10, 45, 0, DateTimeKind.Utc), ScriptSchedule.NextRun(s, Tue1030));
    }

    [Fact]
    public void Daily_runs_today_if_time_is_ahead_else_tomorrow()
    {
        var later = new ScheduledScript { Frequency = ScriptFrequencies.Daily, TimeOfDayUtc = new TimeSpan(23, 0, 0) };
        var earlier = new ScheduledScript { Frequency = ScriptFrequencies.Daily, TimeOfDayUtc = new TimeSpan(3, 0, 0) };
        Assert.Equal(new DateTime(2026, 11, 3, 23, 0, 0, DateTimeKind.Utc), ScriptSchedule.NextRun(later, Tue1030));
        Assert.Equal(new DateTime(2026, 11, 4, 3, 0, 0, DateTimeKind.Utc), ScriptSchedule.NextRun(earlier, Tue1030));
    }

    [Fact]
    public void Weekly_sunday_0300_from_a_tuesday_is_the_coming_sunday()
    {
        var s = new ScheduledScript { Frequency = ScriptFrequencies.Weekly, DayOfWeek = 0, TimeOfDayUtc = new TimeSpan(3, 0, 0) };
        var next = ScriptSchedule.NextRun(s, Tue1030);
        Assert.Equal(DayOfWeek.Sunday, next.DayOfWeek);
        Assert.Equal(new DateTime(2026, 11, 8, 3, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Weekly_same_day_after_the_time_waits_a_full_week()
    {
        var s = new ScheduledScript { Frequency = ScriptFrequencies.Weekly, DayOfWeek = 2, TimeOfDayUtc = new TimeSpan(9, 0, 0) }; // Tuesday 09:00, now is Tuesday 10:30
        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc), ScriptSchedule.NextRun(s, Tue1030));
    }

    [Fact]
    public void Next_run_is_always_strictly_after_now()
    {
        foreach (var f in ScriptFrequencies.All)
        {
            var s = new ScheduledScript { Frequency = f, DayOfWeek = 2, TimeOfDayUtc = new TimeSpan(10, 30, 0) };
            Assert.True(ScriptSchedule.NextRun(s, Tue1030) > Tue1030, f);
        }
    }

    [Theory]
    [InlineData("", "a.sql", "daily", null, "name")]
    [InlineData("x", "..\\evil.sql", "daily", null, "file")]
    [InlineData("x", "sub/a.sql", "daily", null, "file")]
    [InlineData("x", "a.txt", "daily", null, "file")]
    [InlineData("x", "a.sql", "monthly", null, "Frequency")]
    [InlineData("x", "a.sql", "weekly", null, "weekly")]
    public void Validate_rejects_bad_input(string name, string file, string freq, int? day, string expectedWord)
    {
        var error = ScriptSchedule.Validate(name, file, freq, day, TimeSpan.FromHours(3));
        Assert.NotNull(error);
        Assert.Contains(expectedWord, error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_accepts_a_good_weekly_schedule_and_describes_it()
    {
        Assert.Null(ScriptSchedule.Validate("Weekly reset", "reset-demo.SSMS.sql", "weekly", 0, new TimeSpan(3, 0, 0)));
        var s = new ScheduledScript { Frequency = ScriptFrequencies.Weekly, DayOfWeek = 0, TimeOfDayUtc = new TimeSpan(3, 0, 0) };
        Assert.Equal("Weekly, Sunday 03:00 UTC", ScriptSchedule.Describe(s));
    }
}
