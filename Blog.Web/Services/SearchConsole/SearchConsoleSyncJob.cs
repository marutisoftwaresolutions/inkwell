using Blog.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Blog.Web.Services.SearchConsole;

/// <summary>
/// Nightly pull of Search Console rows for every owner with a connected property.
///
/// First run backfills the whole retention window month by month; later runs re-pull the last few
/// days (Search Console keeps revising a day for about 72 hours) plus anything newer, then prune
/// rows past retention. One owner failing never stops the next: each is caught and reported.
/// Search Console publishes a day roughly two days late, so "today minus 2" is the newest date asked for.
/// </summary>
public sealed class SearchConsoleSyncJob : IScheduledJob
{
    public const int PublishLagDays = 2;
    public const int RefetchDays = 3;
    public const int DefaultRetentionMonths = 16;

    private readonly SearchConsoleClient _client;
    private readonly SearchConsoleCredentialStore _credentials;
    private readonly ISearchPerformanceRepository _rows;
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SearchConsoleSyncJob> _log;

    public SearchConsoleSyncJob(SearchConsoleClient client, SearchConsoleCredentialStore credentials,
        ISearchPerformanceRepository rows, ISettingRepository settings, IUserRepository users,
        IMemoryCache cache, ILogger<SearchConsoleSyncJob> log)
    {
        _client = client;
        _credentials = credentials;
        _rows = rows;
        _settings = settings;
        _users = users;
        _cache = cache;
        _log = log;
    }

    public string Name => "search-console-sync";
    public TimeSpan Interval => TimeSpan.FromHours(24);

    /// <summary>The inclusive date range one run asks Search Console for, plus the retention floor it prunes to.</summary>
    public readonly record struct SyncWindow(DateTime From, DateTime To, DateTime Floor)
    {
        /// <summary>Nothing to fetch: the store already holds every day Search Console can serve.</summary>
        public bool IsEmpty => From > To;
    }

    /// <summary>
    /// Pure window arithmetic, so it can be pinned by tests without a clock or a database:
    /// <list type="bullet">
    ///   <item>newest = today (UTC) minus <see cref="PublishLagDays"/>;</item>
    ///   <item>floor = newest minus retention months, plus one day (so exactly that many months are kept);</item>
    ///   <item>first run (no stored day): floor .. newest;</item>
    ///   <item>later runs: max(floor, latest − <see cref="RefetchDays"/>) .. newest;</item>
    ///   <item>a store ahead of newest yields an empty window.</item>
    /// </list>
    /// Retention outside 1–16 months (unset, nonsense, or more than Search Console keeps) becomes the default.
    /// </summary>
    public static SyncWindow Window(DateTime? latest, DateTime nowUtc, int retentionMonths)
    {
        var months = retentionMonths <= 0 ? DefaultRetentionMonths : Math.Clamp(retentionMonths, 1, DefaultRetentionMonths);
        var newest = nowUtc.Date.AddDays(-PublishLagDays);
        var floor = newest.AddMonths(-months).AddDays(1);
        var from = latest is null ? floor : Max(floor, latest.Value.Date.AddDays(-RefetchDays));
        return new SyncWindow(from, newest, floor);
    }

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var owners = await CandidateOwnersAsync();
        int connected = 0, failed = 0; long stored = 0;
        var notes = new List<string>();

        foreach (var ownerId in owners)
        {
            ct.ThrowIfCancellationRequested();
            var settings = await _settings.GetSettingsAsync(ownerId);
            var credential = _credentials.Read(settings);
            if (credential is null || string.IsNullOrWhiteSpace(settings.SearchConsoleProperty)) continue;
            connected++;

            try
            {
                stored += await SyncOwnerAsync(ownerId, settings.SearchConsoleProperty, credential,
                    settings.SearchPerformanceRetentionMonths, ct);
                // New rows are in; whatever the Desk summarised before this run is stale.
                SearchPerformanceService.Invalidate(_cache, ownerId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                failed++;
                notes.Add($"{settings.SearchConsoleProperty}: {ex.Message}");
                _log.LogWarning(ex, "Search Console sync failed for {Property}.", settings.SearchConsoleProperty);
            }
        }

        if (connected == 0) return "No property connected.";
        var summary = $"{connected} propert{(connected == 1 ? "y" : "ies")}, {stored} rows stored";
        if (failed > 0) throw new SearchConsoleException($"{summary}; {failed} failed — {string.Join(" | ", notes)}");
        return summary + ".";
    }

    private async Task<long> SyncOwnerAsync(Guid ownerId, string property, Blog.Core.Services.ServiceAccountCredential credential, int retentionMonths, CancellationToken ct)
    {
        var latest = await _rows.GetLatestDateAsync(ownerId);
        var window = Window(latest, DateTime.UtcNow, retentionMonths);
        if (window.IsEmpty) return 0;

        var token = await _client.GetAccessTokenAsync(credential, ct);
        long stored = 0;

        // Month-sized chunks keep each API call and each transaction bounded on the first backfill.
        for (var chunkStart = window.From; chunkStart <= window.To; chunkStart = chunkStart.AddMonths(1))
        {
            ct.ThrowIfCancellationRequested();
            var chunkEnd = Min(chunkStart.AddMonths(1).AddDays(-1), window.To);
            var rows = await _client.QueryAsync(token, property, chunkStart, chunkEnd, ct);

            foreach (var day in rows.GroupBy(r => r.Date.Date))
            {
                await _rows.ReplaceDayAsync(ownerId, day.Key, day.ToList());
                stored += day.Count();
            }
            // Days inside the range that returned nothing must still be cleared, or a revised-away
            // day would linger. Only relevant for the refetch window; cheap enough to do always.
            var seen = rows.Select(r => r.Date.Date).ToHashSet();
            for (var d = chunkStart; d <= chunkEnd; d = d.AddDays(1))
                if (!seen.Contains(d) && latest is not null && d >= latest.Value.Date.AddDays(-RefetchDays))
                    await _rows.ReplaceDayAsync(ownerId, d, Array.Empty<Blog.Core.Domain.SearchPerformanceRow>());
        }

        await _rows.PruneAsync(ownerId, window.Floor);
        return stored;
    }

    /// <summary>Self-hosted keeps settings under Guid.Empty; cloud mode under each admin. Both are tried.</summary>
    private async Task<List<Guid>> CandidateOwnersAsync()
    {
        var ids = new List<Guid> { Guid.Empty };
        try
        {
            foreach (var u in await _users.GetAllUsersAsync())
                if (string.Equals(u.Role, "Admin", StringComparison.OrdinalIgnoreCase) && !ids.Contains(u.Id))
                    ids.Add(u.Id);
        }
        catch (Exception ex) { _log.LogDebug(ex, "Could not enumerate admin users for Search Console sync."); }
        return ids;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
