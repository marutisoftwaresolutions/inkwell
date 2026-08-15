using Blog.Web.Services.Security;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Guards the two pure pieces of the IP firewall: what counts as an attack, and who can never be
/// blocked. Both decide whether real visitors — or Googlebot — get locked out, so the safety
/// cases matter as much as the detection cases.
/// </summary>
public class ThreatScorerTests
{
    [Theory]
    [InlineData("/wp-login.php")]
    [InlineData("/7.php")]
    [InlineData("/xmlrpc.php")]
    [InlineData("/wp-admin/setup-config.php")]
    [InlineData("/phpmyadmin/index.php")]
    [InlineData("/.env")]
    [InlineData("/.git/config")]
    [InlineData("/vendor/phpunit/phpunit/src/Util/PHP/eval-stdin.php")]
    [InlineData("/cgi-bin/test.cgi")]
    [InlineData("/../../etc/passwd")]
    [InlineData("/actuator/env")]
    [InlineData("/autodiscover/autodiscover.xml")]
    [InlineData("/backup.sql")]
    [InlineData("/wp/")]          // seen in production: bare WordPress root scan
    [InlineData("/wordpress/")]
    public void Probe_paths_score_as_exploit_attempts(string path)
    {
        var verdict = ThreatScorer.Score(path, null, 404, isProtectedCrawler: false);
        Assert.Equal(ThreatScorer.ProbeScore, verdict.Score);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/best-optical-shop-pos-software-2026")]
    [InlineData("/category/optical-software")]
    [InlineData("/author/hitarth")]
    [InlineData("/uploads/2026/07/cover.webp")]
    [InlineData("/.well-known/security.txt")]
    [InlineData("/feed")]
    [InlineData("/sitemap.xml")]
    public void Ordinary_pages_never_score_when_they_succeed(string path)
    {
        var verdict = ThreatScorer.Score(path, null, 200, isProtectedCrawler: false);
        Assert.False(verdict.IsThreat);
    }

    [Fact]
    public void A_missing_page_is_only_worth_one_point()
    {
        var verdict = ThreatScorer.Score("/old-post-that-moved", null, 404, isProtectedCrawler: false);
        Assert.Equal(ThreatScorer.MissScore, verdict.Score);
    }

    [Fact]
    public void Two_probes_are_enough_to_cross_the_default_threshold()
    {
        // Default threshold is 10: two exploit probes (5 each) block, ten stray 404s block.
        Assert.True(ThreatScorer.ProbeScore * 2 >= 10);
        Assert.True(ThreatScorer.MissScore * 9 < 10);
    }

    [Fact]
    public void A_head_request_is_never_scored_because_this_site_405s_every_head()
    {
        // Production answers HEAD / and HEAD /robots.txt with 405, so scoring 405 would block
        // uptime monitors and link checkers rather than attackers.
        Assert.False(ThreatScorer.Score("/", null, 405, isProtectedCrawler: false).IsThreat);
        Assert.False(ThreatScorer.Score("/robots.txt", null, 405, isProtectedCrawler: false).IsThreat);

        // A HEAD-based scanner still trips on the path itself.
        Assert.Equal(ThreatScorer.ProbeScore,
            ThreatScorer.Score("/wp/", null, 405, isProtectedCrawler: false).Score);
    }

    [Fact]
    public void Injection_payloads_in_the_query_string_score()
    {
        var verdict = ThreatScorer.Score("/", "?id=1 UNION SELECT password FROM users", 200, isProtectedCrawler: false);
        Assert.Equal(ThreatScorer.ProbeScore, verdict.Score);
    }

    [Fact]
    public void Site_search_may_contain_anything_a_visitor_types()
    {
        // A reader searching for these words must not be treated as an attacker.
        var verdict = ThreatScorer.Score("/search", "?q=how to union select lens data", 200, isProtectedCrawler: false);
        Assert.False(verdict.IsThreat);
    }

    [Fact]
    public void Protected_crawlers_are_never_penalised_for_dead_links()
    {
        var verdict = ThreatScorer.Score("/a-post-we-deleted", null, 404, isProtectedCrawler: true);
        Assert.False(verdict.IsThreat);
    }

    [Fact]
    public void A_spoofed_crawler_user_agent_does_not_excuse_an_exploit_probe()
    {
        // Real search engines never request wp-login.php, so claiming to be one earns no pass here.
        var verdict = ThreatScorer.Score("/wp-login.php", null, 404, isProtectedCrawler: true);
        Assert.Equal(ThreatScorer.ProbeScore, verdict.Score);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")]
    [InlineData("Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)")]
    [InlineData("Mozilla/5.0 (compatible; ClaudeBot/1.0; +claudebot@anthropic.com)")]
    [InlineData("Mozilla/5.0 AppleWebKit/537.36 (compatible; GPTBot/1.1; +https://openai.com/gptbot)")]
    public void Search_and_AI_crawlers_are_recognised(string userAgent) =>
        Assert.True(ThreatScorer.IsProtectedCrawler(userAgent));

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/126.0 Safari/537.36")]
    [InlineData("python-requests/2.31.0")]
    [InlineData("")]
    public void Everything_else_is_treated_as_an_ordinary_client(string userAgent) =>
        Assert.False(ThreatScorer.IsProtectedCrawler(userAgent));
}

public class IpAllowlistTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("192.168.1.50")]
    [InlineData("10.20.30.40")]
    [InlineData("172.16.9.9")]
    public void Loopback_and_private_ranges_are_always_allowed(string ip) =>
        Assert.True(IpAllowlist.Parse(null).Contains(ip));

    [Fact]
    public void Public_addresses_are_not_allowed_by_default() =>
        Assert.False(IpAllowlist.Parse(null).Contains("203.0.113.7"));

    [Fact]
    public void Single_addresses_and_cidr_ranges_both_match()
    {
        var list = IpAllowlist.Parse("203.0.113.7\n198.51.100.0/24");

        Assert.True(list.Contains("203.0.113.7"));
        Assert.True(list.Contains("198.51.100.42"));
        Assert.False(list.Contains("203.0.113.8"));
        Assert.False(list.Contains("198.51.101.42"));
    }

    [Fact]
    public void Entries_may_be_separated_by_commas_or_newlines()
    {
        var list = IpAllowlist.Parse("203.0.113.7, 203.0.113.8;203.0.113.9");

        Assert.True(list.Contains("203.0.113.8"));
        Assert.True(list.Contains("203.0.113.9"));
    }

    [Fact]
    public void An_ipv4_client_on_a_dual_stack_socket_still_matches_its_ipv4_entry()
    {
        var list = IpAllowlist.Parse("203.0.113.7");
        Assert.True(list.Contains(System.Net.IPAddress.Parse("::ffff:203.0.113.7")));
    }

    [Fact]
    public void Unparseable_entries_are_reported_rather_than_silently_dropped()
    {
        var list = IpAllowlist.Parse("203.0.113.7, not-an-ip, 10.0.0.0/8");

        Assert.True(list.Contains("203.0.113.7"));
        Assert.Equal(["not-an-ip"], list.InvalidEntries);
    }
}
