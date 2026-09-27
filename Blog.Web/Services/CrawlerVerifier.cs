using System.Net;
using Blog.Core.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Blog.Web.Services;

/// <summary>
/// Checks whether a request that claims to be a known crawler came from that operator's network,
/// using the operator's published IP list or reverse DNS confirmed by a forward lookup. Runs only
/// inside <see cref="TrafficRecorder"/>'s batched flush, never on the request path. Fail-soft:
/// an unreachable list or a DNS timeout yields "unknown" (null), never "spoofed".
///
/// Results: <c>true</c> verified, <c>false</c> the check ran and the address is not the
/// operator's, <c>null</c> the operator publishes no method or the check could not run.
/// </summary>
public sealed class CrawlerVerifier
{
    public static readonly TimeSpan ResultLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan RangeLifetime  = TimeSpan.FromHours(24);
    public static readonly TimeSpan RangeRetryAfterFailure = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(3);

    private static readonly HttpClient RangeHttp = CreateHttp();

    private readonly IMemoryCache _cache;
    private readonly ILogger<CrawlerVerifier> _log;

    public CrawlerVerifier(IMemoryCache cache, ILogger<CrawlerVerifier> log)
    {
        _cache = cache;
        _log = log;
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Inkwell-CrawlerVerifier/1.0");
        return http;
    }

    /// <summary>A cached verdict for this crawler and address, if one exists.</summary>
    public bool TryGetCached(string crawler, string ip, out bool? verdict) =>
        _cache.TryGetValue(ResultKey(crawler, ip), out verdict);

    /// <summary>
    /// For the IP firewall, on the request path: memory only, never a lookup. True only when this
    /// address already failed a two-signal check for the crawler its User-Agent names
    /// (<see cref="CrawlerVerification.IsProvenImpostor"/>). Unknown, unchecked or list-only
    /// failures return false, so a real crawler keeps its protection.
    /// </summary>
    public bool IsKnownImpostor(string? userAgent, string ip)
    {
        try
        {
            var crawler = CrawlerIdentifier.Identify(userAgent ?? "")?.Name;
            if (crawler is null || string.IsNullOrEmpty(ip)) return false;
            return TryGetCached(crawler, ip, out var verdict) && CrawlerVerification.IsProvenImpostor(crawler, verdict);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies one crawler/address pair. <paramref name="allowDns"/> false skips the reverse-DNS
    /// step (the caller's per-flush budget is spent); a range match still verifies.
    /// </summary>
    public async Task<bool?> VerifyAsync(string crawler, string ip, bool allowDns)
    {
        if (TryGetCached(crawler, ip, out var cached)) return cached;

        var method = CrawlerVerification.For(crawler);
        if (method is null || !CrawlerVerification.IsCheckableAddress(ip)) return Remember(crawler, ip, null);

        var anyListLoaded = false;
        foreach (var url in method.RangeUrls)
        {
            var ranges = await GetRangesAsync(url);
            if (ranges is null) continue;
            anyListLoaded = true;
            if (CrawlerVerification.InRanges(ranges, ip)) return Remember(crawler, ip, true);
        }

        if (method.DnsSuffixes.Count > 0)
        {
            if (!allowDns) return null;   // undecided; not cached so a later flush can finish it
            var dns = await ReverseForwardConfirmAsync(ip, method.DnsSuffixes);
            if (dns is not null) return Remember(crawler, ip, dns);
            return anyListLoaded ? null : Remember(crawler, ip, null);
        }

        // Range-only operators: a loaded list that does not contain the address is a failed check.
        return Remember(crawler, ip, anyListLoaded ? false : null);
    }

    private bool? Remember(string crawler, string ip, bool? verdict)
    {
        _cache.Set(ResultKey(crawler, ip), verdict, ResultLifetime);
        return verdict;
    }

    public static string ResultKey(string crawler, string ip) => $"cv:{crawler}:{ip}";

    private async Task<IReadOnlyList<IPNetwork>?> GetRangesAsync(string url)
    {
        var key = "cv-ranges:" + url;
        if (_cache.TryGetValue(key, out IReadOnlyList<IPNetwork>? ranges)) return ranges;

        try
        {
            var json = await RangeHttp.GetStringAsync(url);
            ranges = CrawlerVerification.ParsePrefixes(json);
            if (ranges.Count == 0) throw new InvalidDataException("List contained no prefixes.");
            _cache.Set(key, ranges, RangeLifetime);
            return ranges;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Crawler IP list {Url} unavailable; visits stay unverified for now.", url);
            _cache.Set<IReadOnlyList<IPNetwork>?>(key, null, RangeRetryAfterFailure);
            return null;
        }
    }

    /// <summary>true/false when DNS answered; null when it timed out or failed.</summary>
    private static async Task<bool?> ReverseForwardConfirmAsync(string ip, IReadOnlyList<string> suffixes)
    {
        try
        {
            var reverse = await Dns.GetHostEntryAsync(ip).WaitAsync(DnsTimeout);
            var host = reverse.HostName;
            if (!CrawlerVerification.HostMatches(host, suffixes)) return false;

            var forward = await Dns.GetHostAddressesAsync(host).WaitAsync(DnsTimeout);
            var address = IPAddress.Parse(ip);
            return forward.Any(a => a.Equals(address) || (a.IsIPv4MappedToIPv6 && a.MapToIPv4().Equals(address)));
        }
        catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound)
        {
            return false;   // no PTR record: the address has no crawler host name
        }
        catch
        {
            return null;
        }
    }
}
