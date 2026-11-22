using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Sign-in protection beyond the IP firewall: a published, per-account wait after repeated wrong
/// passwords, and a cut-off that ends every older sign-in when the owner asks or the password changes.
/// </summary>
public class SignInProtectionTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_fifth_wrong_password_inside_the_window_makes_the_account_wait_fifteen_minutes()
    {
        var throttle = new SignInThrottle();
        for (var i = 0; i < 4; i++) Assert.False(throttle.RecordFailure("Owner@Example.org", T0.AddMinutes(i)));
        Assert.Null(throttle.LockedFor("owner@example.org", T0.AddMinutes(4)));

        Assert.True(throttle.RecordFailure("owner@example.org ", T0.AddMinutes(4)));   // same account, however it is typed
        var wait = throttle.LockedFor("OWNER@example.org", T0.AddMinutes(5));
        Assert.NotNull(wait);
        Assert.Equal(14, SignInThrottle.MinutesToWait(wait!.Value));
        Assert.Null(throttle.LockedFor("owner@example.org", T0.AddMinutes(4).Add(SignInThrottle.Lockout).AddSeconds(1)));
    }

    [Fact]
    public void Wrong_passwords_spread_over_a_long_time_never_add_up()
    {
        var throttle = new SignInThrottle();
        for (var i = 0; i < 12; i++) Assert.False(throttle.RecordFailure("a@example.org", T0.AddMinutes(i * 6)));   // 4 an hour
        Assert.Null(throttle.LockedFor("a@example.org", T0.AddHours(2)));
    }

    [Fact]
    public void One_account_waiting_does_not_slow_another()
    {
        var throttle = new SignInThrottle();
        for (var i = 0; i < SignInThrottle.MaxFailures; i++) throttle.RecordFailure("a@example.org", T0);
        Assert.NotNull(throttle.LockedFor("a@example.org", T0));
        Assert.Null(throttle.LockedFor("b@example.org", T0));
    }

    [Fact]
    public void A_correct_sign_in_or_a_completed_reset_clears_the_count_and_the_wait()
    {
        var throttle = new SignInThrottle();
        for (var i = 0; i < SignInThrottle.MaxFailures; i++) throttle.RecordFailure("a@example.org", T0);
        throttle.Clear("A@example.org");
        Assert.Null(throttle.LockedFor("a@example.org", T0));
        Assert.False(throttle.RecordFailure("a@example.org", T0));
    }

    [Fact]
    public void A_flood_of_invented_addresses_cannot_grow_the_table_without_limit()
    {
        var throttle = new SignInThrottle();
        for (var i = 0; i < SignInThrottle.MaxTracked + 500; i++) throttle.RecordFailure($"x{i}@example.org", T0);
        // Entries past the cap are not tracked while the rest are live; an hour later the old ones are pruned and tracking resumes.
        for (var i = 0; i < SignInThrottle.MaxFailures; i++) throttle.RecordFailure("late@example.org", T0.AddHours(1));
        Assert.NotNull(throttle.LockedFor("late@example.org", T0.AddHours(1)));
    }

    [Fact]
    public void Blank_input_is_ignored()
    {
        var throttle = new SignInThrottle();
        for (var i = 0; i < 10; i++) Assert.False(throttle.RecordFailure("  ", T0));
        Assert.Null(throttle.LockedFor(null, T0));
    }

    // ── Wiring (source checks) ───────────────────────────────────────────────

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Theory]
    [InlineData("Blog.Web", "Controllers", "AccountController.cs")]
    [InlineData("Blog.Web", "Controllers", "Api", "AuthApiController.cs")]
    public void Both_sign_in_paths_check_the_wait_before_the_password_and_spare_the_operators_own_address(params string[] path)
    {
        var src = Read(path);
        var locked = src.IndexOf("throttle.LockedFor(", StringComparison.Ordinal);
        var password = src.IndexOf(path[^1] == "AccountController.cs" ? "VerifyPassword(password, user.PasswordHash)" : "ValidateLoginAsync(", StringComparison.Ordinal);
        Assert.True(locked > 0 && locked < password, "The wait must be checked before the password is.");
        Assert.Contains("IsAllowlistedAsync(HttpContext)", src);
        Assert.Contains("throttle.RecordFailure(", src);
        Assert.Contains("throttle.Clear(", src);
    }

    [Fact]
    public void A_password_change_a_reset_and_sign_out_everywhere_all_end_older_sessions()
    {
        var account = Read("Blog.Web", "Controllers", "AccountController.cs");
        Assert.Contains("[HttpPost(\"sign-out-everywhere\")]", account);
        Assert.Contains("AuditActions.AuthSessionsEnded", account);
        Assert.True(account.Split("SetSessionsValidFromAsync(").Length - 1 >= 2);                     // reset + helper
        Assert.Contains("if (!string.IsNullOrWhiteSpace(newPassword)) await EndOtherSessionsAsync(user.Id);", account);

        var validator = Read("Blog.Web", "Services", "DeskSessionValidator.cs");
        Assert.Contains("IssuedBefore(ctx,", validator);
        // The cache holds the cut-off, not a yes, so an old cookie is refused while a new one is valid.
        Assert.DoesNotContain("out bool known", validator);

        var tokens = Read("Blog.Web", "Services", "ApiTokenService.cs");
        Assert.Contains("JwtRegisteredClaimNames.Iat", tokens);
        Assert.Contains("user.SessionsValidFrom", tokens);
    }

    [Fact]
    public void The_security_page_offers_it_through_the_shared_dialog()
    {
        var view = Read("Blog.Web", "Views", "TwoFactor", "Security.cshtml");
        Assert.Contains("action=\"/account/sign-out-everywhere\"", view);
        Assert.Contains("data-confirm-title=\"Sign out everywhere else?\"", view);
        Assert.Contains("data-confirm-action=\"Sign out everywhere else\"", view);
    }
}
