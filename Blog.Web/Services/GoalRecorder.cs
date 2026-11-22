using System.Collections.Concurrent;
using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Blog.Web.Services;

/// <summary>
/// Counts goals (Admin → Analytics → Goals) without a database write per request. A hit is added to
/// an in-memory tally; the tally is written as daily counters at most every
/// <see cref="FlushInterval"/>, by one flush at a time, and a failed write is dropped rather than
/// retried into a pile-up. The tally cannot grow without bound: a key is counted only when it is one
/// of the site's goals or a built-in conversion, so there are at most a few dozen entries per site
/// per day. Like page views, a hit still in memory when the process dies is lost. Fails open: nothing
/// here can break a page, a sign-up or a form.
/// </summary>
public sealed class GoalRecorder
{
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GoalsCacheFor = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopes;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GoalRecorder> _log;
    private readonly ConcurrentDictionary<(Guid Owner, DateTime Day, string Key), int> _tally = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private DateTime _lastFlushUtc = DateTime.UtcNow;

    public GoalRecorder(IServiceScopeFactory scopes, IMemoryCache cache, ILogger<GoalRecorder> log)
    {
        _scopes = scopes;
        _cache = cache;
        _log = log;
    }

    public int Pending => _tally.Count;

    /// <summary>
    /// The site's goals. <paramref name="owner"/> is the tenant in cloud mode and <see cref="Guid.Empty"/>
    /// otherwise (the same key page views are stored under); outside cloud mode the settings are the
    /// first Admin's. Cached briefly, so adding a goal takes effect within a minute. Never throws.
    /// </summary>
    public async Task<IReadOnlyList<Goal>> GoalsAsync(Guid owner)
    {
        try
        {
            return await _cache.GetOrCreateAsync("goals:" + owner, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = GoalsCacheFor;
                using var scope = _scopes.CreateScope();
                var settingsOwner = owner != Guid.Empty
                    ? owner
                    : (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync())?.Id ?? Guid.Empty;
                var site = await scope.ServiceProvider.GetRequiredService<ISettingRepository>().GetSettingsAsync(settingsOwner);
                return (IReadOnlyList<Goal>)Goals.Normalize(site.Goals);
            }) ?? [];
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Goals not loaded (non-fatal).");
            return [];
        }
    }

    /// <summary>Forget the cached list after the Desk changes it, so the change shows at once on this server.</summary>
    public void Forget(Guid owner) => _cache.Remove("goals:" + owner);

    /// <summary>The click goals as the page script needs them: [{"k": key, "u": start of the link}], or null when there are none.</summary>
    public async Task<string?> ClickTargetsJsonAsync(Guid owner)
    {
        var clicks = (await GoalsAsync(owner)).Where(g => g.Kind == Goals.Click).Select(g => new { k = g.Key, u = g.Target }).ToList();
        return clicks.Count == 0 ? null : JsonSerializer.Serialize(clicks);
    }

    /// <summary>A built-in conversion happened (a sign-up confirmed, a form sent).</summary>
    public void Conversion(Guid owner, string builtInKey)
    {
        if (!Goals.IsBuiltIn(builtInKey)) return;
        Add(owner, builtInKey, 1);
        FlushIfDue();
    }

    /// <summary>The page script reported a click. Counted only when the key is one of the site's click goals. Returns whether it was.</summary>
    public async Task<bool> ClickAsync(Guid owner, string? key)
    {
        if (!Goals.IsClickGoal(await GoalsAsync(owner), key)) return false;
        Add(owner, key!, 1);
        FlushIfDue();
        return true;
    }

    /// <summary>A batch of page views was recorded: count the visit goals among them, then write what is due.</summary>
    public async Task PageViewsAsync(IEnumerable<(Guid Owner, string Path)> views)
    {
        try
        {
            foreach (var byOwner in views.GroupBy(v => v.Owner))
            {
                var goals = await GoalsAsync(byOwner.Key);
                if (!goals.Any(g => g.Kind == Goals.Visit)) continue;
                foreach (var view in byOwner)
                    foreach (var key in Goals.VisitKeysFor(goals, view.Path))
                        Add(byOwner.Key, key, 1);
            }
            FlushIfDue();
        }
        catch (Exception ex) { _log.LogDebug(ex, "Visit goals not counted (non-fatal)."); }
    }

    private void Add(Guid owner, string key, int hits) =>
        _tally.AddOrUpdate((owner, DateTime.UtcNow.Date, key), hits, (_, current) => current + hits);

    private void FlushIfDue()
    {
        if (_tally.IsEmpty || DateTime.UtcNow - _lastFlushUtc < FlushInterval) return;
        if (!_flushGate.Wait(0)) return;   // a flush is already running; this request moves on
        _ = RunFlushAsync();
    }

    private async Task RunFlushAsync()
    {
        try { await FlushCoreAsync(); }
        finally { _lastFlushUtc = DateTime.UtcNow; _flushGate.Release(); }
    }

    /// <summary>Writes everything now. Used on shutdown and by tests; waits for a running flush.</summary>
    public async Task FlushAsync()
    {
        await _flushGate.WaitAsync();
        try { await FlushCoreAsync(); }
        finally { _lastFlushUtc = DateTime.UtcNow; _flushGate.Release(); }
    }

    private async Task FlushCoreAsync()
    {
        try
        {
            var hits = new List<GoalHit>();
            foreach (var key in _tally.Keys.ToList())
                if (_tally.TryRemove(key, out var count)) hits.Add(new GoalHit(key.Owner, key.Day, key.Key, count));
            if (hits.Count == 0) return;

            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IGoalCountRepository>().AddAsync(hits);
        }
        catch (Exception ex) { _log.LogDebug(ex, "Goal counts not written (non-fatal)."); }
    }
}
