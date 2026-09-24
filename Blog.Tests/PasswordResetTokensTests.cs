using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

public class PasswordResetTokensTests
{
    [Fact]
    public void Tokens_are_long_random_and_url_safe()
    {
        var a = PasswordResetTokens.Generate();
        var b = PasswordResetTokens.Generate();

        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 43, $"token too short: {a.Length}");
        Assert.DoesNotContain('+', a);
        Assert.DoesNotContain('/', a);
        Assert.DoesNotContain('=', a);
    }

    [Fact]
    public void Hash_is_stable_lower_case_hex_and_differs_per_token()
    {
        var token = PasswordResetTokens.Generate();
        var h1 = PasswordResetTokens.Hash(token);
        var h2 = PasswordResetTokens.Hash(token);

        Assert.Equal(h1, h2);
        Assert.Equal(64, h1.Length);
        Assert.Equal(h1, h1.ToLowerInvariant());
        Assert.NotEqual(h1, PasswordResetTokens.Hash(PasswordResetTokens.Generate()));
    }

    [Fact]
    public void A_token_is_usable_only_while_unused_and_unexpired()
    {
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        var expires = now.AddMinutes(10);

        Assert.True(PasswordResetTokens.IsUsable(null, expires, now));
        Assert.False(PasswordResetTokens.IsUsable(now.AddMinutes(-1), expires, now));   // used
        Assert.False(PasswordResetTokens.IsUsable(null, now.AddMinutes(-1), now));      // expired
        Assert.False(PasswordResetTokens.IsUsable(null, now, now));                     // expires exactly now
    }

    [Theory]
    [InlineData(null, null, "at least 8")]
    [InlineData("short", "short", "at least 8")]
    [InlineData("longenough", "different", "do not match")]
    public void New_password_policy_rejects_bad_input(string? pw, string? confirm, string expectedFragment)
    {
        var error = PasswordResetTokens.ValidateNewPassword(pw, confirm);
        Assert.NotNull(error);
        Assert.Contains(expectedFragment, error);
    }

    [Fact]
    public void New_password_policy_accepts_a_matching_pair_of_sufficient_length()
    {
        Assert.Null(PasswordResetTokens.ValidateNewPassword("correct horse battery", "correct horse battery"));
    }

    [Theory]
    [InlineData("www.example.com",            "http",  "10.0.0.5:5000",    "https://www.example.com")]   // behind a TLS-terminating proxy
    [InlineData("https://www.example.com/",   "http",  "internal-host",    "https://www.example.com")]   // full origin, trailing slash dropped
    [InlineData("http://staging.example.com", "https", "whatever",         "http://staging.example.com")] // an explicit scheme is respected
    [InlineData("  www.example.com  ",        "http",  "10.0.0.5",         "https://www.example.com")]   // whitespace tolerated
    public void Reset_link_base_prefers_the_configured_canonical_host_over_what_the_proxy_shows(string canonical, string scheme, string host, string expected)
    {
        Assert.Equal(expected, PasswordResetTokens.ResolveLinkBase(canonical, scheme, host));
    }

    [Theory]
    [InlineData(null, "http",  "localhost:5000",  "http://localhost:5000")]
    [InlineData("",   "https", "www.example.com", "https://www.example.com")]
    [InlineData("  ", "https", "www.example.com", "https://www.example.com")]
    [InlineData(null, "",      "www.example.com", "https://www.example.com")]  // no scheme at all → assume https
    public void Reset_link_base_falls_back_to_the_request_when_no_canonical_host_is_configured(string? canonical, string scheme, string host, string expected)
    {
        Assert.Equal(expected, PasswordResetTokens.ResolveLinkBase(canonical, scheme, host));
    }
}
