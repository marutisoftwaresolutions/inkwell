using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The two abuse limits on self-service password reset: at most three requests per address per
/// hour (the fourth is silently ignored so the page never reveals whether the address exists), and a
/// link that works exactly once. Both are pure decisions in <see cref="PasswordResetTokens"/>;
/// AccountController only supplies the counts and the clock.
/// </summary>
public class PasswordResetCapTests
{
    [Fact]
    public void The_cap_is_three_requests_per_hour()
    {
        Assert.Equal(3, PasswordResetTokens.MaxRequestsPerWindow);
        Assert.Equal(TimeSpan.FromHours(1), PasswordResetTokens.RequestWindow);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]   // the third request is still allowed
    [InlineData(3, false)]  // the fourth is not
    [InlineData(4, false)]
    [InlineData(int.MaxValue, false)]
    public void A_request_is_allowed_only_while_fewer_than_the_cap_were_made_in_the_window(int recentCount, bool allowed)
    {
        Assert.Equal(allowed, PasswordResetTokens.CanRequest(recentCount));
    }

    [Fact]
    public void A_negative_count_from_a_broken_query_is_treated_as_no_recent_requests()
    {
        Assert.True(PasswordResetTokens.CanRequest(-1));
    }

    [Fact]
    public void A_link_works_once_and_never_again()
    {
        var issued = new DateTime(2026, 9, 19, 9, 0, 0, DateTimeKind.Utc);
        var expires = issued + PasswordResetTokens.Lifetime;

        Assert.True(PasswordResetTokens.IsUsable(usedAtUtc: null, expires, issued.AddMinutes(5)));

        var usedAt = issued.AddMinutes(5);
        Assert.False(PasswordResetTokens.IsUsable(usedAt, expires, usedAt));                 // the same instant it was used
        Assert.False(PasswordResetTokens.IsUsable(usedAt, expires, usedAt.AddSeconds(1)));   // a replay a second later
        Assert.False(PasswordResetTokens.IsUsable(usedAt, expires, issued.AddMinutes(1)));   // even with a clock that went backwards
    }

    [Fact]
    public void An_unused_link_still_dies_at_thirty_minutes()
    {
        var issued = new DateTime(2026, 9, 19, 9, 0, 0, DateTimeKind.Utc);
        var expires = issued + PasswordResetTokens.Lifetime;

        Assert.Equal(TimeSpan.FromMinutes(30), PasswordResetTokens.Lifetime);
        Assert.True(PasswordResetTokens.IsUsable(null, expires, expires.AddSeconds(-1)));
        Assert.False(PasswordResetTokens.IsUsable(null, expires, expires));
        Assert.False(PasswordResetTokens.IsUsable(null, expires, expires.AddDays(1)));
    }
}
