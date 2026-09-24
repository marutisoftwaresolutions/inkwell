using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Blog.Web.Services.SearchConsole;

/// <summary>What every screen needs to know before drawing search numbers.</summary>
public sealed record SearchDataState(bool Configured, bool HasData, DateTime? AsOf)
{
    public static readonly SearchDataState NotConfigured = new(false, false, null);
    /// <summary>Configured, pulled, and something to show.</summary>
    public bool Ready => Configured && HasData;
}

/// <summary>
/// Read side of the Search Console integration for the editor, Content Health and the dashboard.
/// Everything here is fail-open: any error yields "no data", never a broken admin page.
///
/// The per-page summaries are the expensive read (a 56-day GROUP BY over every page × query row)
/// and the dashboard, Content Health and every editor panel ask for the same answer, so it is held
/// in memory per owner for <see cref="CacheTtl"/>. The entry is dropped by the sync job after a
/// successful pull and ignored on its own if the newest stored day has moved, so a fresh pull is
/// never hidden behind a stale summary.
/// </summary>
public sealed class SearchPerformanceService
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ISearchPerformanceRepository _rows;
    private readonly ISettingRepository _settings;
    private readonly SearchConsoleCredentialStore _credentials;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenant;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SearchPerformanceService> _log;

    public SearchPerformanceService(ISearchPerformanceRepository rows, ISettingRepository settings,
        SearchConsoleCredentialStore credentials, IUserRepository users, ITenantContext tenant,
        IMemoryCache cache, ILogger<SearchPerformanceService> log)
    {
        _rows = rows;
        _settings = settings;
        _credentials = credentials;
        _users = users;
        _tenant = tenant;
        _cache = cache;
        _log = log;
    }

    /// <summary>Cache key of the owner's page summaries; one entry per owner, never shared across tenants.</summary>
    public static string PageSummariesCacheKey(Guid ownerId) => $"search-performance:pages:{ownerId:N}";

    /// <summary>Drop the owner's cached summaries — called by the sync job once new rows are stored.</summary>
    public static void Invalidate(IMemoryCache cache, Guid ownerId) => cache.Remove(PageSummariesCacheKey(ownerId));

    /// <summary>
    /// The owner whose settings hold the Search Console connection — the same key the Settings
    /// screen writes under: the signed-in tenant in cloud mode, else the first admin.
    /// </summary>
    public async Task<Guid> ResolveOwnerAsync()
    {
        if (_tenant.IsCloudMode && _tenant.IsResolved) return _tenant.UserId;
        try { return (await _users.GetFirstAdminAsync())?.Id ?? Guid.Empty; }
        catch { return Guid.Empty; }
    }

    public async Task<SearchDataState> GetStateAsync(Guid ownerId)
    {
        try
        {
            var settings = await _settings.GetSettingsAsync(ownerId);
            if (!_credentials.IsConfigured(settings)) return SearchDataState.NotConfigured;
            var latest = await _rows.GetLatestDateAsync(ownerId);
            return new SearchDataState(true, latest is not null, latest);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Search data state unavailable.");
            return SearchDataState.NotConfigured;
        }
    }

    /// <summary>One summary per page over the last 28 days against the 28 before, newest data date as reference.</summary>
    public async Task<(SearchDataState State, IReadOnlyList<PageSearchSummary> Pages)> GetPageSummariesAsync(Guid ownerId)
    {
        var state = await GetStateAsync(ownerId);
        if (!state.Ready) return (state, Array.Empty<PageSearchSummary>());

        var asOf = state.AsOf!.Value.Date;
        var key = PageSummariesCacheKey(ownerId);
        if (_cache.TryGetValue(key, out CachedSummaries? hit) && hit is not null && hit.AsOf == asOf)
            return (state, hit.Pages);

        try
        {
            var from = asOf.AddDays(-(SearchPerformanceAnalyzer.WindowDays * 2 - 1));
            var days = await _rows.GetPageDaysAsync(ownerId, from, asOf);
            var pages = SearchPerformanceAnalyzer.Summarise(days, asOf);
            _cache.Set(key, new CachedSummaries(asOf, pages), CacheTtl);
            return (state, pages);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Search page summaries unavailable.");
            return (state, Array.Empty<PageSearchSummary>());
        }
    }

    /// <summary>The panel for one post: its summary plus the queries that brought it impressions.</summary>
    public async Task<(SearchDataState State, PageSearchSummary? Summary, IReadOnlyList<SearchQueryStat> Queries)> GetForSlugAsync(Guid ownerId, string slug)
    {
        var (state, pages) = await GetPageSummariesAsync(ownerId);
        if (!state.Ready) return (state, null, Array.Empty<SearchQueryStat>());

        var summary = SearchPerformanceAnalyzer.ForSlug(pages, slug);
        if (summary is null) return (state, null, Array.Empty<SearchQueryStat>());
        try
        {
            var asOf = state.AsOf!.Value.Date;
            var queries = await _rows.GetPageQueriesAsync(ownerId, summary.Page, asOf.AddDays(-(SearchPerformanceAnalyzer.WindowDays - 1)), asOf, 10);
            return (state, summary, queries);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Search queries unavailable for {Slug}.", slug);
            return (state, summary, Array.Empty<SearchQueryStat>());
        }
    }

    /// <summary>What is cached: the summaries and the data date they were built for.</summary>
    private sealed record CachedSummaries(DateTime AsOf, IReadOnlyList<PageSearchSummary> Pages);
}
