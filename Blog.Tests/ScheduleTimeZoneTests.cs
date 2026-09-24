using System.Text.RegularExpressions;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// A schedule time typed in the editor is read in the operator's display zone and stored in UTC.
/// Before 2026-09-17 the editor bound the typed value straight to UTC, so an operator in
/// Asia/Kolkata scheduling 15:00 got 20:30.
/// </summary>
public class ScheduleTimeZoneTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return dir!.FullName;
    }

    [Theory]
    [InlineData("India Standard Time", "Asia/Kolkata", 5, 30)]
    [InlineData("UTC", "UTC", 0, 0)]
    public void Typed_time_round_trips_through_the_display_zone(string windowsId, string ianaId, int offsetHours, int offsetMinutes)
    {
        var zoneId = TimeZoneHelper.IsKnown(windowsId) ? windowsId : ianaId;
        if (!TimeZoneHelper.IsKnown(zoneId)) return; // zone data absent on this host — nothing to assert

        var typed = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Unspecified);
        var stored = TimeZoneHelper.FromDisplay(typed, zoneId);
        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        Assert.Equal(typed.AddHours(-offsetHours).AddMinutes(-offsetMinutes), stored);

        var shown = TimeZoneHelper.ToDisplay(stored, zoneId);
        Assert.Equal(typed, shown);
    }

    [Fact]
    public void Posts_controller_converts_schedule_times_on_both_writes_and_the_edit_read()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "Blog.Web", "Controllers", "PostsController.cs"));
        Assert.Equal(2, Regex.Matches(src, @"await NormaliseScheduleAsync\(post\);").Count); // Create + Edit POST
        Assert.Contains("TimeZoneHelper.FromDisplay(post.ScheduledAt.Value", src);
        Assert.Contains("TimeZoneHelper.ToDisplay(post.ScheduledAt.Value", src);               // Edit GET
    }
}
