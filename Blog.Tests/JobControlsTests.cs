using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>Operator controls on built-in jobs (ADR-0040): the override replaces the declared cadence, never below one minute.</summary>
public class JobControlsTests
{
    [Fact]
    public void No_override_keeps_the_declared_interval()
    {
        Assert.Equal(TimeSpan.FromHours(24), JobSchedule.EffectiveInterval(TimeSpan.FromHours(24), null));
        Assert.Equal(TimeSpan.FromHours(24), JobSchedule.EffectiveInterval(TimeSpan.FromHours(24), 0));
        Assert.Equal(TimeSpan.FromHours(24), JobSchedule.EffectiveInterval(TimeSpan.FromHours(24), -5));
    }

    [Fact]
    public void Override_replaces_the_declared_interval()
    {
        Assert.Equal(TimeSpan.FromMinutes(90), JobSchedule.EffectiveInterval(TimeSpan.FromHours(24), 90));
        Assert.Equal(TimeSpan.FromMinutes(1), JobSchedule.EffectiveInterval(TimeSpan.FromHours(24), 1));
    }

    [Fact]
    public void A_zero_interval_is_due_now_so_run_now_can_reuse_the_claim()
    {
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now, JobSchedule.DueBefore(now, TimeSpan.Zero));
        Assert.True(JobSchedule.IsDue(now.AddSeconds(-1), TimeSpan.Zero, now));
    }
}
