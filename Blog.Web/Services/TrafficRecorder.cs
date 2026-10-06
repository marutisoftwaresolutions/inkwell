using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace Blog.Web.Services;

/// <summary>
/// Buffers page views and crawler visits in memory and writes them in batches. Before this, every
/// counted request cost its own database insert plus, for page views, an outbound geo-IP call — so
/// the 2026-09-04..06 homepage flood on the live site (≈730k requests/day) turned into the same
/// number of inserts and HTTP calls. Now a request only enqueues; one flush at a time drains the
/// buffers at most every <see cref="FlushInterval"/> (or sooner at <see cref="FlushThreshold"/>
/// items), resolves geo for a capped number of new addresses, and checks each claimed crawler
/// against its operator's published network (<see cref="CrawlerVerifier"/>).
///
/// Same shape as the IP firewall's buffered hit counters: flush is triggered from the request path,
/// single-flight, fire-and-forget into a root scope, and fail-open throughout. The trade-offs are
/// explicit: items are dropped (and counted) once a buffer is full, and whatever is still buffered
/// when the process dies is lost — both are acceptable for analytics.
/// </summary>
public sealed class TrafficRecorder
{
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);
    public const int FlushThreshold = 500;
    public const int Capacity = 50_000;
    public const int MaxBatch = 5_000;
    /// <summary>ip-api.com allows 45 requests a minute; two flushes a minute at 20 stays under it.</summary>
    public const int MaxGeoLookupsPerFlush = 20;
    /// <summary>Reverse-DNS checks per flush; published-range checks are in memory and unbounded.</summary>
    public const int MaxDnsChecksPerFlush = 30;

    private static readonly HttpClient GeoHttp = new() { Timeout = TimeSpan.FromSeconds(4) };

    private static readonly string[] PrivatePrefixes =
        ["127.", "::1", "10.", "192.168.", "172.16.", "172.17.", "172.18.", "172.19.",
         "172.20.", "172.21.", "172.22.", "172.23.", "172.24.", "172.25.", "172.26.",
         "172.27.", "172.28.", "172.29.", "172.30.", "172.31.", "0:0:0:0:0:0:0:1", "fe80"];

    private readonly BoundedBatchBuffer<PendingView> _views = new(Capacity);
    private readonly BoundedBatchBuffer<PendingCrawler> _crawlers = new(Capacity);
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly IServiceScopeFactory _scopes;
    private readonly IMemoryCache _cache;
    private readonly CrawlerVerifier _verifier;
    private readonly ILogger<TrafficRecorder> _log;
    private DateTime _lastFlushUtc = DateTime.UtcNow;

    /// <summary>A view waiting for its flush. The raw IP lives only in memory, for the geo lookup.</summary>
    private sealed record PendingView(PageView View, string Ip);

    /// <summary>A crawler visit waiting for its flush; the raw IP is kept only to verify the crawler.</summary>
    private sealed record PendingCrawler(CrawlerVisit Visit, string Ip);

    public TrafficRecorder(IServiceScopeFactory scopes, IMemoryCache cache, CrawlerVerifier verifier, ILogger<TrafficRecorder> log)
    {
        _scopes = scopes;
        _cache = cache;
        _verifier = verifier;
        _log = log;
    }

    public void RecordPageView(PageView view, string ip)
    {
        _views.TryAdd(new PendingView(view, ip));
        FlushIfDue();
    }

    public void RecordCrawlerVisit(CrawlerVisit visit, string ip)
    {
        _crawlers.TryAdd(new PendingCrawler(visit, ip));
        FlushIfDue();
    }

    private void FlushIfDue()
    {
        var pending = Math.Max(_views.Count, _crawlers.Count);
        if (!BoundedBatchBuffer<PendingView>.IsFlushDue(pending, FlushThreshold, _lastFlushUtc, DateTime.UtcNow, FlushInterval))
            return;
        if (!_flushGate.Wait(0)) return;   // a flush is already running; this request moves on
        _ = RunFlushAsync();
    }

    private async Task RunFlushAsync()
    {
        try { await FlushCoreAsync(); }
        catch (Exception ex) { _log.LogDebug(ex, "Traffic flush failed (non-fatal)."); }
        finally
        {
            _lastFlushUtc = DateTime.UtcNow;
            _flushGate.Release();
        }
    }

    /// <summary>Drains everything now. Used on shutdown and by tests; waits for a running flush.</summary>
    public async Task FlushAsync()
    {
        await _flushGate.WaitAsync();
        try
        {
            while (_views.Count > 0 || _crawlers.Count > 0)
                if (await FlushCoreAsync() == 0) break;
        }
        catch (Exception ex) { _log.LogDebug(ex, "Traffic flush failed (non-fatal)."); }
        finally
        {
            _lastFlushUtc = DateTime.UtcNow;
            _flushGate.Release();
        }
    }

    private async Task<int> FlushCoreAsync()
    {
        var crawlers = _crawlers.Drain(MaxBatch);
        var views = _views.Drain(MaxBatch);
        var dropped = _views.TakeDropped() + _crawlers.TakeDropped();
        if (dropped > 0)
            _log.LogWarning("Traffic buffers were full; {Dropped} page view / crawler row(s) dropped.", dropped);
        if (crawlers.Count == 0 && views.Count == 0) return 0;

        using var scope = _scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var written = 0;

        if (crawlers.Count > 0)
        {
            await VerifyCrawlersAsync(crawlers);
            try { written += await sp.GetRequiredService<ICrawlerVisitRepository>().RecordManyAsync(crawlers.Select(c => c.Visit).ToList()); }
            catch (Exception ex) { _log.LogDebug(ex, "Crawler visit batch not recorded (non-fatal)."); }
        }

        if (views.Count > 0)
        {
            await ResolveGeoAsync(views);
            try { written += await sp.GetRequiredService<IPageViewRepository>().RecordManyAsync(views.Select(v => v.View).ToList()); }
            catch (Exception ex) { _log.LogDebug(ex, "Page view batch not recorded (non-fatal)."); }

            // "Most read" counts the same beacons, added per post in one pass for the whole batch.
            try { await sp.GetRequiredService<IPostRepository>().AddViewCountsAsync(PostViewTally.Count(views.Select(v => (v.View.OwnerId, v.View.Path)))); }
            catch (Exception ex) { _log.LogDebug(ex, "Post view counts not updated (non-fatal)."); }
        }

        return written;
    }

    private async Task VerifyCrawlersAsync(IReadOnlyList<PendingCrawler> crawlers)
    {
        var dnsBudget = MaxDnsChecksPerFlush;
        foreach (var group in crawlers.GroupBy(c => (c.Visit.Crawler, c.Ip)))
        {
            bool? verdict = null;
            try
            {
                var cached = _verifier.TryGetCached(group.Key.Crawler, group.Key.Ip, out verdict);
                if (!cached)
                {
                    var method = CrawlerVerification.For(group.Key.Crawler);
                    var allowDns = method is { DnsSuffixes.Count: > 0 } && dnsBudget > 0;
                    if (allowDns) dnsBudget--;
                    verdict = await _verifier.VerifyAsync(group.Key.Crawler, group.Key.Ip, allowDns);
                }
            }
            catch (Exception ex) { _log.LogDebug(ex, "Crawler verification skipped (non-fatal)."); }

            foreach (var c in group) c.Visit.Verified = verdict;
        }
    }

    /// <summary>
    /// Same rule the Analytics and AI Crawlers screens read with: the tenant in cloud mode, otherwise
    /// <see cref="Guid.Empty"/>. Resolve it during the request, while the tenant context still exists.
    /// </summary>
    public static Guid OwnerOf(HttpContext context)
    {
        var tenant = context.RequestServices?.GetService<ITenantContext>();
        return tenant is { IsCloudMode: true, IsResolved: true } ? tenant.UserId : Guid.Empty;
    }

    private async Task ResolveGeoAsync(IReadOnlyList<PendingView> views)
    {
        var lookups = 0;
        foreach (var group in views.GroupBy(v => v.Ip))
        {
            var ip = group.Key;
            if (string.IsNullOrEmpty(ip) || PrivatePrefixes.Any(p => ip.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;

            var cacheKey = $"geo:{ip}";
            if (!_cache.TryGetValue(cacheKey, out (string? Country, string? Region) geo))
            {
                if (lookups >= MaxGeoLookupsPerFlush) continue;   // over budget: stored without a country
                lookups++;
                var looked = await LookupAsync(ip);
                if (looked is null) break;                       // service down or throttling — stop for this flush
                geo = looked.Value;
                _cache.Set(cacheKey, geo, TimeSpan.FromHours(24));
            }

            foreach (var v in group)
            {
                v.View.Country = geo.Country;
                v.View.Region = geo.Region;
            }
        }
    }

    private static async Task<(string? Country, string? Region)?> LookupAsync(string ip)
    {
        try
        {
            var json = await GeoHttp.GetStringAsync($"http://ip-api.com/json/{ip}?fields=status,countryCode,regionName");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var status) && status.GetString() == "success")
            {
                var country = root.TryGetProperty("countryCode", out var cc) ? cc.GetString() : null;
                var region  = root.TryGetProperty("regionName",  out var rn) ? rn.GetString() : null;
                return (country, region);
            }
            return (null, null);   // answered but unknown (reserved range etc.) — cache the miss
        }
        catch
        {
            return null;
        }
    }
}
