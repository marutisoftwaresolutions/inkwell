using System.Net;
using System.Text.Json;

namespace Blog.Core.Services;

/// <summary>How an operator lets a site prove a request really came from its crawler.</summary>
/// <param name="RangeUrls">Published IP-prefix lists (the shared <c>{"prefixes":[{"ipv4Prefix"|"ipv6Prefix"}]}</c> format).</param>
/// <param name="DnsSuffixes">Reverse-DNS host suffixes, confirmed by a forward lookup back to the same address.</param>
public sealed record CrawlerVerificationMethod(IReadOnlyList<string> RangeUrls, IReadOnlyList<string> DnsSuffixes);

/// <summary>
/// A User-Agent is self-asserted: in September 2026 the live site logged 3.7M "GPTBot" requests, all
/// but a handful recorded as the homepage, and the table could not say whether they were real. This maps each crawler
/// <see cref="CrawlerIdentifier"/> names to the verification its operator publishes, and holds the
/// pure matching logic. Sources (checked 2026-09-27): Google "Verify requests from Google crawlers",
/// OpenAI "Overview of OpenAI crawlers", Apple "About Applebot", Bing "Verify Bingbot", Perplexity
/// crawler docs. Operators that publish no machine-checkable method (Meta, Anthropic, Amazon's HTML
/// list, Common Crawl, …) have none here, and their visits stay unverified rather than guessed.
/// </summary>
public static class CrawlerVerification
{
    private static readonly Dictionary<string, CrawlerVerificationMethod> Methods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Googlebot"] = new(
            ["https://developers.google.com/static/crawling/ipranges/common-crawlers.json",
             "https://developers.google.com/static/crawling/ipranges/special-crawlers.json"],
            [".googlebot.com", ".google.com"]),
        ["Bingbot"] = new(["https://www.bing.com/toolbox/bingbot.json"], [".search.msn.com"]),
        ["Applebot"] = new(["https://search.developer.apple.com/applebot.json"], [".applebot.apple.com"]),
        ["Applebot-Extended"] = new(["https://search.developer.apple.com/applebot.json"], [".applebot.apple.com"]),
        ["GPTBot"] = new(["https://openai.com/gptbot.json"], []),
        ["OAI-SearchBot"] = new(["https://openai.com/searchbot.json"], []),
        ["ChatGPT-User"] = new(["https://openai.com/chatgpt-user.json"], []),
        ["PerplexityBot"] = new(["https://www.perplexity.com/perplexitybot.json"], []),
        ["Perplexity-User"] = new(["https://www.perplexity.com/perplexity-user.json"], []),
    };

    /// <summary>The published method for this crawler, or null when its operator publishes none.</summary>
    public static CrawlerVerificationMethod? For(string crawler) =>
        Methods.TryGetValue(crawler, out var m) ? m : null;

    /// <summary>Every range list any method uses, for warming or testing.</summary>
    public static IEnumerable<string> AllRangeUrls => Methods.Values.SelectMany(m => m.RangeUrls).Distinct();

    /// <summary>Parses the shared prefix-list format; malformed entries are skipped, malformed JSON throws.</summary>
    public static IReadOnlyList<IPNetwork> ParsePrefixes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<IPNetwork>();
        if (!doc.RootElement.TryGetProperty("prefixes", out var prefixes) || prefixes.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var entry in prefixes.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            foreach (var name in new[] { "ipv4Prefix", "ipv6Prefix" })
                if (entry.TryGetProperty(name, out var value) && IPNetwork.TryParse(value.GetString(), out var net))
                    list.Add(net);
        }
        return list;
    }

    public static bool InRanges(IReadOnlyList<IPNetwork> ranges, string? ip)
    {
        if (!IPAddress.TryParse(ip, out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        foreach (var net in ranges)
            if (net.Contains(address)) return true;
        return false;
    }

    /// <summary>
    /// Whether a failed check is strong enough to withdraw the IP firewall's crawler protection.
    /// Only for operators that publish both an IP list and reverse-DNS names: there a failure means
    /// the address is outside the list and its reverse DNS is not the operator's, two independent
    /// signals. A list-only failure (OpenAI, Perplexity) could be a stale list, and wrongly blocking a
    /// real crawler costs far more than the marginal risk (security rule, invariant 3), so those keep
    /// the protection.
    /// </summary>
    public static bool IsProvenImpostor(string? crawler, bool? verdict) =>
        verdict == false && crawler is not null && For(crawler) is { DnsSuffixes.Count: > 0 };

    /// <summary>
    /// Only a public address can be checked. Loopback, private and link-local addresses mean the site
    /// sees a proxy or itself, not the client — judging those would mark every real crawler spoofed.
    /// </summary>
    public static bool IsCheckableAddress(string? ip)
    {
        if (!IPAddress.TryParse(ip, out var a)) return false;
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (IPAddress.IsLoopback(a) || a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6UniqueLocal) return false;
        if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = a.GetAddressBytes();
            if (b[0] == 10 || b[0] == 127 || b[0] == 0) return false;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
            if (b[0] == 192 && b[1] == 168) return false;
            if (b[0] == 169 && b[1] == 254) return false;
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;   // carrier-grade NAT
        }
        return true;
    }

    /// <summary>True when the reverse-DNS host ends with one of the suffixes (the suffix alone is not a crawler host).</summary>
    public static bool HostMatches(string? host, IReadOnlyList<string> suffixes)
    {
        if (string.IsNullOrEmpty(host)) return false;
        var h = host.TrimEnd('.');
        foreach (var suffix in suffixes)
            if (h.Length > suffix.Length && h.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
