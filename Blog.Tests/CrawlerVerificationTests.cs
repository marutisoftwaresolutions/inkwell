using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// A crawler's User-Agent is self-declared; these pin the checks that tell the real one from
/// someone using its name, and the fail-soft rules that keep a proxy or an outage from marking
/// every real crawler as spoofed.
/// </summary>
public class CrawlerVerificationTests
{
    private const string Sample = """
        {"creationTime":"2026-09-22T02:00:07","prefixes":[
          {"ipv4Prefix":"20.171.206.0/24"},
          {"ipv6Prefix":"2001:4860:4801:10::/64"},
          {"ipv4Prefix":"not-a-prefix"},
          {"somethingElse":"1.2.3.0/24"}
        ]}
        """;

    [Fact]
    public void The_shared_prefix_format_is_parsed_and_bad_entries_skipped()
    {
        var ranges = CrawlerVerification.ParsePrefixes(Sample);

        Assert.Equal(2, ranges.Count);
        Assert.True(CrawlerVerification.InRanges(ranges, "20.171.206.44"));
        Assert.True(CrawlerVerification.InRanges(ranges, "::ffff:20.171.206.44"));   // IPv4-mapped
        Assert.True(CrawlerVerification.InRanges(ranges, "2001:4860:4801:10::1"));
        Assert.False(CrawlerVerification.InRanges(ranges, "20.171.207.1"));
        Assert.False(CrawlerVerification.InRanges(ranges, "garbage"));
    }

    [Fact]
    public void A_list_without_prefixes_is_empty_and_malformed_json_throws()
    {
        Assert.Empty(CrawlerVerification.ParsePrefixes("""{"creationTime":"x"}"""));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => CrawlerVerification.ParsePrefixes("<html>"));
    }

    [Theory]
    [InlineData("crawl-66-249-66-1.googlebot.com", true)]
    [InlineData("crawl-66-249-66-1.googlebot.com.", true)]           // fully-qualified trailing dot
    [InlineData("rate-limited-proxy-66-249-90-1.google.com", true)]
    [InlineData("googlebot.com", false)]                             // the bare suffix is not a crawler host
    [InlineData("crawl.googlebot.com.attacker.net", false)]
    [InlineData("evilgooglebot.com", false)]
    [InlineData("", false)]
    public void Reverse_dns_hosts_must_end_with_the_published_suffix(string host, bool expected) =>
        Assert.Equal(expected, CrawlerVerification.HostMatches(host, [".googlebot.com", ".google.com"]));

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("20.171.206.44", true)]
    [InlineData("2001:4860:4801:10::1", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.5", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("", false)]
    public void Only_public_addresses_can_be_checked(string ip, bool expected) =>
        Assert.Equal(expected, CrawlerVerification.IsCheckableAddress(ip));

    [Fact]
    public void Methods_exist_only_where_the_operator_publishes_one()
    {
        Assert.NotNull(CrawlerVerification.For("GPTBot"));
        Assert.NotNull(CrawlerVerification.For("Googlebot"));
        Assert.NotNull(CrawlerVerification.For("Applebot"));
        Assert.Null(CrawlerVerification.For("Meta-ExternalAgent"));
        Assert.Null(CrawlerVerification.For("ClaudeBot"));
        Assert.Null(CrawlerVerification.For("NoSuchBot"));
    }

    [Fact]
    public void Every_verifiable_crawler_is_one_the_identifier_can_name()
    {
        var known = CrawlerIdentifier.Known.Select(k => k.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Googlebot", "Bingbot", "Applebot", "Applebot-Extended", "GPTBot",
                                     "OAI-SearchBot", "ChatGPT-User", "PerplexityBot", "Perplexity-User" })
        {
            Assert.Contains(name, known);
            Assert.NotNull(CrawlerVerification.For(name));
        }
        Assert.All(CrawlerVerification.AllRangeUrls, url => Assert.StartsWith("https://", url));
    }

    // ── Firewall protection (security rule, invariant 3) ───────────────────────

    [Theory]
    [InlineData("Googlebot", false, true)]    // outside Google's list AND reverse DNS is not Google's
    [InlineData("Bingbot",   false, true)]
    [InlineData("Applebot",  false, true)]
    [InlineData("GPTBot",    false, false)]   // list-only: a stale list must not cost a real crawler
    [InlineData("PerplexityBot", false, false)]
    [InlineData("Googlebot", true,  false)]
    [InlineData("Googlebot", null,  false)]   // unchecked keeps protection
    [InlineData("Meta-ExternalAgent", false, false)]
    public void Only_a_two_signal_failure_withdraws_crawler_protection(string crawler, bool? verdict, bool expected) =>
        Assert.Equal(expected, CrawlerVerification.IsProvenImpostor(crawler, verdict));

    [Fact]
    public void The_firewall_sees_an_impostor_only_after_a_cached_failed_check()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var verifier = new CrawlerVerifier(cache, NullLogger<CrawlerVerifier>.Instance);
        const string ua = "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)";

        Assert.False(verifier.IsKnownImpostor(ua, "198.51.100.4"));     // never checked: protected

        cache.Set(CrawlerVerifier.ResultKey("Googlebot", "198.51.100.4"), (bool?)false);
        cache.Set(CrawlerVerifier.ResultKey("Googlebot", "66.249.66.1"), (bool?)true);
        cache.Set(CrawlerVerifier.ResultKey("GPTBot", "198.51.100.4"), (bool?)false);

        Assert.True(verifier.IsKnownImpostor(ua, "198.51.100.4"));
        Assert.False(verifier.IsKnownImpostor(ua, "66.249.66.1"));
        Assert.False(verifier.IsKnownImpostor("Mozilla/5.0 (compatible; GPTBot/1.1; +https://openai.com/gptbot)", "198.51.100.4"));
        Assert.False(verifier.IsKnownImpostor("Mozilla/5.0 Chrome/128", "198.51.100.4"));   // not a crawler at all
        Assert.False(verifier.IsKnownImpostor(ua, ""));
    }

    [Fact]
    public void Firewall_and_traffic_recording_use_the_resolved_client_address()
    {
        var root = RepoRoot();
        var firewall = File.ReadAllText(Path.Combine(root, "Blog.Web", "Services", "Security", "IpFirewallService.cs"));
        Assert.Contains("_crawlerVerifier.IsKnownImpostor(ua, ip)", firewall);

        // Behind a trusted proxy the connection address is the proxy's: crawler verification would
        // fail every real crawler and the beacon budget would throttle a whole region.
        foreach (var file in new[] { Path.Combine("Middleware", "CrawlerVisitMiddleware.cs"),
                                     Path.Combine("Controllers", "PageViewBeaconController.cs") })
        {
            var src = File.ReadAllText(Path.Combine(root, "Blog.Web", file));
            Assert.Contains("ResolveClientIp(", src);
            Assert.DoesNotContain("RemoteIpAddress", src);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public async Task A_private_address_is_unknown_not_spoofed_and_needs_no_network()
    {
        var verifier = new CrawlerVerifier(new MemoryCache(new MemoryCacheOptions()), NullLogger<CrawlerVerifier>.Instance);

        Assert.Null(await verifier.VerifyAsync("GPTBot", "127.0.0.1", allowDns: true));
        Assert.Null(await verifier.VerifyAsync("GPTBot", "10.0.0.8", allowDns: true));
        Assert.Null(await verifier.VerifyAsync("Meta-ExternalAgent", "8.8.8.8", allowDns: true));   // no published method
        Assert.True(verifier.TryGetCached("GPTBot", "127.0.0.1", out var cached));
        Assert.Null(cached);
    }
}
