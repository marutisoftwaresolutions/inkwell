using System.Collections.Concurrent;
using System.Net;
using Blog.Core.Domain;
using Blog.Core.Interfaces;

namespace Blog.Web.Services.Security;

/// <summary>
/// Effective firewall configuration, projected from <see cref="UserSettings"/> and clamped to
/// sane bounds so a bad value can never make the site unreachable.
/// </summary>
public sealed record FirewallOptions(
    bool Enabled, int ThresholdScore, int WindowMinutes, int BlockHours,
    bool Escalate, bool ProtectCrawlers, bool TrustProxyHeaders,
    string Allowlist, bool NotifyOnBlock)
{
    /// <summary>Fail-open default used until settings load — the site keeps serving, nothing is blocked.</summary>
    public static readonly FirewallOptions Inert =
        new(false, 10, 10, 24, true, true, false, "", false);

    public static FirewallOptions From(UserSettings s) => new(
        Enabled:           s.FirewallEnabled,
        ThresholdScore:    Math.Clamp(s.FirewallThresholdScore, 3, 1000),
        WindowMinutes:     Math.Clamp(s.FirewallWindowMinutes, 1, 1440),
        BlockHours:        Math.Clamp(s.FirewallBlockHours, 1, 8760),
        Escalate:          s.FirewallEscalateRepeatOffenders,
        ProtectCrawlers:   s.FirewallProtectSearchCrawlers,
        TrustProxyHeaders: s.FirewallTrustProxyHeaders,
        Allowlist:         s.FirewallIpAllowlist ?? "",
        NotifyOnBlock:     s.FirewallNotifyOnBlock);
}

/// <summary>
/// The IP firewall engine: decides whether an address is blocked, scores failing requests, and
/// auto-blocks addresses that cross the threshold. Registered as a singleton.
///
/// Hot path is memory-only — rules and settings are cached in a snapshot refreshed roughly once a
/// minute, and denied-request counters are buffered and flushed in one batch — so an ongoing
/// attack costs no per-request database work. Every method swallows its own failures: a firewall
/// problem must never take the site down (fail open).
/// </summary>
public sealed class IpFirewallService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PurgeInterval   = TimeSpan.FromHours(1);
    private const int MaxWatchEntries = 20_000;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<IpFirewallService> _log;

    private sealed record Snapshot(
        FirewallOptions Options,
        ConcurrentDictionary<string, IpFirewallRule> Rules,
        IpAllowlist Allowlist);

    private sealed class Watch
    {
        public int       Score;
        public int       Requests;
        public DateTime  WindowStart;
        public DateTime  LastSeen;
        public string?   LastPath;
        public string?   LastReason;
    }

    private volatile Snapshot? _snapshot;
    private DateTime _nextRefreshUtc = DateTime.MinValue;
    private DateTime _nextPurgeUtc   = DateTime.MinValue;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    // Addresses accumulating a score but not yet blocked, and denied-request counters awaiting flush.
    private readonly ConcurrentDictionary<string, Watch> _watch   = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _pendingHits = new(StringComparer.OrdinalIgnoreCase);

    public IpFirewallService(IServiceScopeFactory scopes, ILogger<IpFirewallService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    // ── Client address ────────────────────────────────────────────────────────

    /// <summary>
    /// The address to police. Proxy headers are honoured only when the operator has declared the
    /// site to sit behind a trusted proxy — otherwise any client could forge a header and either
    /// dodge a block or frame an innocent address.
    /// </summary>
    public string ResolveClientIp(HttpContext ctx)
    {
        try
        {
            if (_snapshot?.Options.TrustProxyHeaders == true)
            {
                var cf = ctx.Request.Headers["CF-Connecting-IP"].ToString();
                if (TryNormalize(cf, out var cfIp)) return cfIp;

                var xff = ctx.Request.Headers["X-Forwarded-For"].ToString();
                if (!string.IsNullOrWhiteSpace(xff) && TryNormalize(xff.Split(',')[0], out var xffIp))
                    return xffIp;
            }

            var remote = ctx.Connection.RemoteIpAddress;
            return remote is null ? "" : Normalize(remote);
        }
        catch { return ""; }
    }

    private static bool TryNormalize(string? raw, out string ip)
    {
        ip = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var candidate = raw.Trim();
        // Strip an IPv4 "host:port" suffix; bracketed IPv6 keeps its own form.
        if (candidate.Count(c => c == ':') == 1 && candidate.Contains('.'))
            candidate = candidate[..candidate.IndexOf(':')];
        if (!IPAddress.TryParse(candidate, out var addr)) return false;
        ip = Normalize(addr);
        return true;
    }

    private static string Normalize(IPAddress addr) =>
        (addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4() : addr).ToString();

    // ── Enforcement ───────────────────────────────────────────────────────────

    /// <summary>The active block for this address, or null when it may pass.</summary>
    public async ValueTask<IpFirewallRule?> GetActiveBlockAsync(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return null;
        try
        {
            var snap = await GetSnapshotAsync();
            if (!snap.Options.Enabled) return null;
            // The never-block list outranks any rule, including one added by hand — an operator
            // who allowlists an address must not be able to lock it out by accident.
            if (snap.Allowlist.Contains(ip)) return null;
            if (!snap.Rules.TryGetValue(ip, out var rule)) return null;
            if (rule.Kind != IpRuleKinds.Block) return null;
            if (rule.ExpiresAt is not null && rule.ExpiresAt <= DateTime.UtcNow)
            {
                snap.Rules.TryRemove(ip, out _); // lapsed between refreshes — let it through now
                return null;
            }
            return rule;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Firewall block lookup failed (fail-open).");
            return null;
        }
    }

    /// <summary>Buffer a refused request; counters are flushed to the database on the next refresh.</summary>
    public void NoteDenied(string ip) =>
        _pendingHits.AddOrUpdate(ip, 1, (_, v) => v + 1);

    // ── Detection ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Weigh a completed request and auto-block the address once its score crosses the threshold.
    /// Signed-in users, allowlisted addresses, and already-ruled addresses are skipped.
    /// </summary>
    public async Task RegisterRequestAsync(HttpContext ctx, string ip, int statusCode)
    {
        try
        {
            if (string.IsNullOrEmpty(ip)) return;

            var snap = await GetSnapshotAsync();
            if (!snap.Options.Enabled) return;

            // Authentication middleware has run by now, so staff traffic is identifiable —
            // an admin clicking a dead link must never contribute to a block.
            if (ctx.User?.Identity?.IsAuthenticated == true) return;

            if (snap.Allowlist.Contains(ip)) return;
            if (snap.Rules.ContainsKey(ip)) return; // already allowed or already blocked

            var path  = ctx.Request.Path.Value ?? "/";
            var query = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : null;
            var ua    = ctx.Request.Headers.UserAgent.ToString();
            var isCrawler = snap.Options.ProtectCrawlers && ThreatScorer.IsProtectedCrawler(ua);

            var verdict = ThreatScorer.Score(path, query, statusCode, isCrawler);
            if (!verdict.IsThreat) return;

            await RegisterThreatAsync(ip, verdict, path, ua, snap);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Firewall request scoring failed (non-fatal).");
        }
    }

    /// <summary>Record a rejected sign-in — brute-force attempts score even though they return 200.</summary>
    public async Task RegisterFailedLoginAsync(HttpContext ctx)
    {
        try
        {
            var ip = ResolveClientIp(ctx);
            if (string.IsNullOrEmpty(ip)) return;

            var snap = await GetSnapshotAsync();
            if (!snap.Options.Enabled) return;
            if (snap.Allowlist.Contains(ip)) return;
            if (snap.Rules.ContainsKey(ip)) return;

            var verdict = new ThreatVerdict(ThreatScorer.FailedLoginScore, "Failed admin sign-in attempt");
            await RegisterThreatAsync(ip, verdict, ctx.Request.Path.Value,
                ctx.Request.Headers.UserAgent.ToString(), snap);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Firewall failed-login scoring failed (non-fatal).");
        }
    }

    /// <summary>
    /// Record a comment submission the spam filter discarded. The response is a 302 identical to a
    /// real submission's, so — like a failed sign-in — the signal is invisible to status-code scoring
    /// and has to be reported here explicitly. Same guards as every other path: fail open, never score
    /// an allowlisted address, never re-score one that is already blocked.
    /// </summary>
    public async Task RegisterCommentSpamAsync(HttpContext ctx, string reason)
    {
        try
        {
            var ip = ResolveClientIp(ctx);
            if (string.IsNullOrEmpty(ip)) return;
            await RegisterCommentSpamAsync(ip, reason, ctx.Request.Path.Value, ctx.Request.Headers.UserAgent.ToString());
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Firewall comment-spam scoring failed (non-fatal).");
        }
    }

    /// <summary>
    /// The same signal for a comment a moderator marks as spam after the fact: the address that
    /// posted it (stored on the comment) is scored as if the filter had caught it. Same guards.
    /// </summary>
    public async Task RegisterCommentSpamAsync(string ip, string reason, string? path = null, string? userAgent = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ip)) return;
            var snap = await GetSnapshotAsync();
            if (!snap.Options.Enabled) return;
            if (snap.Allowlist.Contains(ip)) return;
            if (snap.Rules.ContainsKey(ip)) return;

            var verdict = new ThreatVerdict(ThreatScorer.CommentSpamScore, $"Comment spam: {reason}");
            await RegisterThreatAsync(ip, verdict, path, userAgent, snap);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Firewall comment-spam scoring failed (non-fatal).");
        }
    }

    private async Task RegisterThreatAsync(string ip, ThreatVerdict verdict, string? path, string? ua, Snapshot snap)
    {
        var now    = DateTime.UtcNow;
        var window = TimeSpan.FromMinutes(snap.Options.WindowMinutes);

        int score;
        var entry = _watch.GetOrAdd(ip, _ => new Watch { WindowStart = now });
        lock (entry)
        {
            if (now - entry.WindowStart > window)
            {
                entry.Score = 0;
                entry.Requests = 0;
                entry.WindowStart = now;
            }
            entry.Score     += verdict.Score;
            entry.Requests  += 1;
            entry.LastSeen   = now;
            entry.LastPath   = path;
            entry.LastReason = verdict.Reason;
            score = entry.Score;
        }

        if (_watch.Count > MaxWatchEntries) PruneWatch(window);

        if (score < snap.Options.ThresholdScore) return;

        _watch.TryRemove(ip, out _);
        var reason = $"{verdict.Reason} (score {score} in {snap.Options.WindowMinutes} min)";
        await CreateBlockAsync(ip, IpRuleSources.Auto, reason, score, path, ua, "system", null, snap.Options);
    }

    private void PruneWatch(TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        foreach (var kv in _watch)
            if (kv.Value.LastSeen < cutoff) _watch.TryRemove(kv.Key, out _);
    }

    /// <summary>Addresses currently under observation, highest score first.</summary>
    public IReadOnlyList<IpWatchEntry> GetWatchlist(int top = 25)
    {
        var window = TimeSpan.FromMinutes(_snapshot?.Options.WindowMinutes ?? 10);
        var cutoff = DateTime.UtcNow - window;
        return _watch
            .Where(kv => kv.Value.LastSeen >= cutoff)
            .Select(kv => new IpWatchEntry
            {
                IpAddress   = kv.Key,
                Score       = kv.Value.Score,
                Requests    = kv.Value.Requests,
                LastPath    = kv.Value.LastPath,
                LastReason  = kv.Value.LastReason,
                WindowStart = kv.Value.WindowStart,
                LastSeenAt  = kv.Value.LastSeen
            })
            .OrderByDescending(e => e.Score)
            .ThenByDescending(e => e.LastSeenAt)
            .Take(top)
            .ToList();
    }

    // ── Rule management (admin) ───────────────────────────────────────────────

    /// <summary>Block an address by hand. <paramref name="duration"/> null = permanent.</summary>
    public async Task<IpFirewallRule> BlockAsync(
        string ip, string? reason, TimeSpan? duration, string createdBy, bool permanent = false)
    {
        var options = (await GetSnapshotAsync()).Options;
        var expires = permanent ? (DateTime?)null : DateTime.UtcNow.Add(duration ?? TimeSpan.FromHours(options.BlockHours));
        return await CreateBlockAsync(ip, IpRuleSources.Manual, reason, 0, null, null, createdBy,
            expires, options, notify: false);
    }

    /// <summary>Allowlist an address permanently — it is never auto-blocked and any block is lifted.</summary>
    public async Task<IpFirewallRule> AllowAsync(string ip, string? reason, string createdBy)
    {
        var rule = new IpFirewallRule
        {
            IpAddress = ip,
            Kind      = IpRuleKinds.Allow,
            Source    = IpRuleSources.Manual,
            Reason    = Trim(reason, 500),
            CreatedBy = Trim(createdBy, 200),
            CreatedAt = DateTime.UtcNow
        };

        using var scope = _scopes.CreateScope();
        var repo  = scope.ServiceProvider.GetRequiredService<IIpFirewallRepository>();
        var saved = await repo.UpsertAsync(rule);

        _watch.TryRemove(ip, out _);
        ApplyLocally(saved);
        return saved;
    }

    public async Task<bool> RemoveRuleAsync(long id)
    {
        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIpFirewallRepository>();
        var removed = await repo.DeleteAsync(id);
        if (removed) Invalidate();
        return removed;
    }

    public async Task<IpFirewallRule?> GetRuleAsync(string ip)
    {
        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIpFirewallRepository>();
        return await repo.GetByIpAsync(ip);
    }

    public async Task<FirewallOptions> GetOptionsAsync() => (await GetSnapshotAsync()).Options;

    /// <summary>Allowlist entries that failed to parse — shown as a warning in the admin UI.</summary>
    public async Task<IReadOnlyList<string>> GetInvalidAllowlistEntriesAsync() =>
        (await GetSnapshotAsync()).Allowlist.InvalidEntries;

    /// <summary>Force the next request to reload rules and settings (call after any admin change).</summary>
    public void Invalidate() => _nextRefreshUtc = DateTime.MinValue;

    private async Task<IpFirewallRule> CreateBlockAsync(
        string ip, string source, string? reason, int score, string? path, string? userAgent,
        string createdBy, DateTime? expiresAt, FirewallOptions options, bool notify = true)
    {
        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIpFirewallRepository>();

        // A returning offender keeps its history: second block lasts a week, third is permanent.
        var existing = await repo.GetByIpAsync(ip);
        var offense  = (existing?.OffenseCount ?? 0) + 1;

        if (source == IpRuleSources.Auto)
        {
            expiresAt = options.Escalate
                ? offense switch
                {
                    1 => DateTime.UtcNow.AddHours(options.BlockHours),
                    2 => DateTime.UtcNow.AddHours(options.BlockHours * 7),
                    _ => null // permanent
                }
                : DateTime.UtcNow.AddHours(options.BlockHours);
        }

        var rule = new IpFirewallRule
        {
            IpAddress     = ip,
            Kind          = IpRuleKinds.Block,
            Source        = source,
            Reason        = Trim(reason, 500),
            Score         = score,
            OffenseCount  = offense,
            LastPath      = Trim(path, 1024),
            LastUserAgent = Trim(userAgent, 512),
            CreatedBy     = Trim(createdBy, 200),
            CreatedAt     = DateTime.UtcNow,
            ExpiresAt     = expiresAt
        };

        var saved = await repo.UpsertAsync(rule);
        ApplyLocally(saved);
        _watch.TryRemove(ip, out _);

        _log.LogWarning("IP firewall blocked {Ip} ({Source}) until {Expires}: {Reason}",
            ip, source, saved.ExpiresAt?.ToString("u") ?? "permanent", saved.Reason);

        await AuditBlockAsync(scope.ServiceProvider, saved, source);
        if (notify && source == IpRuleSources.Auto && options.NotifyOnBlock)
            await NotifyBlockAsync(scope.ServiceProvider, saved);

        return saved;
    }

    /// <summary>Apply a rule to the live snapshot so it takes effect on the very next request.</summary>
    private void ApplyLocally(IpFirewallRule rule)
    {
        var snap = _snapshot;
        if (snap is null) { Invalidate(); return; }
        snap.Rules[rule.IpAddress] = rule;
    }

    private async Task AuditBlockAsync(IServiceProvider sp, IpFirewallRule rule, string source)
    {
        try
        {
            var audit = sp.GetRequiredService<IAuditRepository>();
            await audit.LogAsync(new AuditLog
            {
                OwnerId    = Guid.Empty,
                UserId     = Guid.Empty,
                UserName   = rule.CreatedBy ?? "system",
                Action     = AuditActions.SecurityIpBlocked,
                EntityType = "Security",
                EntityId   = rule.IpAddress,
                EntityName = rule.IpAddress,
                NewValues  = System.Text.Json.JsonSerializer.Serialize(new
                {
                    rule.Kind, rule.Source, rule.Reason, rule.Score, rule.OffenseCount,
                    ExpiresAt = rule.ExpiresAt?.ToString("u") ?? "permanent", rule.LastPath
                }),
                IpAddress  = rule.IpAddress,
                UserAgent  = rule.LastUserAgent,
                CreatedAt  = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Firewall audit write failed (non-fatal).");
        }
    }

    private async Task NotifyBlockAsync(IServiceProvider sp, IpFirewallRule rule)
    {
        try
        {
            var users = sp.GetRequiredService<IUserRepository>();
            var admin = await users.GetFirstAdminAsync();
            if (admin is null) return;

            var settings = await sp.GetRequiredService<ISettingRepository>().GetSettingsAsync(admin.Id);
            var recipients = (settings.ErrorNotificationEmails ?? "")
                .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => a.Contains('@')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (recipients.Count == 0) return;

            var email = sp.GetRequiredService<IEmailService>();
            var enc = (string? s) => System.Net.WebUtility.HtmlEncode(s ?? "-");
            var subject = $"[Security] IP {rule.IpAddress} auto-blocked";
            var body =
                $"<p>The IP firewall blocked an address after repeated hostile requests.</p>" +
                $"<table cellpadding='4' style='font-family:sans-serif;font-size:14px'>" +
                $"<tr><td><b>IP</b></td><td>{enc(rule.IpAddress)}</td></tr>" +
                $"<tr><td><b>Reason</b></td><td>{enc(rule.Reason)}</td></tr>" +
                $"<tr><td><b>Offense</b></td><td>#{rule.OffenseCount}</td></tr>" +
                $"<tr><td><b>Blocked until</b></td><td>{(rule.ExpiresAt?.ToString("u") ?? "permanent")}</td></tr>" +
                $"<tr><td><b>Last path</b></td><td>{enc(rule.LastPath)}</td></tr>" +
                $"<tr><td><b>User agent</b></td><td>{enc(rule.LastUserAgent)}</td></tr>" +
                $"</table>" +
                $"<p style='color:#666;font-size:12px'>Review or lift blocks in Admin &rarr; Security.</p>";

            foreach (var to in recipients)
            {
                try { await email.SendAsync(to, to, subject, body); }
                catch (Exception mailEx) { _log.LogDebug(mailEx, "Firewall alert email to {To} failed.", to); }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Firewall block notification failed (non-fatal).");
        }
    }

    // ── Snapshot ──────────────────────────────────────────────────────────────

    private async ValueTask<Snapshot> GetSnapshotAsync()
    {
        var current = _snapshot;
        if (current is not null && DateTime.UtcNow < _nextRefreshUtc) return current;

        // Only one request refreshes; everyone else keeps serving from the previous snapshot.
        if (current is not null && !await _refreshGate.WaitAsync(0)) return current;
        if (current is null) await _refreshGate.WaitAsync();

        try
        {
            if (_snapshot is not null && DateTime.UtcNow < _nextRefreshUtc) return _snapshot;

            var fresh = await LoadAsync();
            _snapshot = fresh;
            _nextRefreshUtc = DateTime.UtcNow.Add(RefreshInterval);
            return fresh;
        }
        catch (Exception ex)
        {
            // Database unreachable: keep the old snapshot (or stay inert) and retry after the
            // interval. The firewall must never be the reason a page fails to render.
            _log.LogWarning(ex, "Firewall snapshot refresh failed — continuing with cached rules.");
            _nextRefreshUtc = DateTime.UtcNow.Add(RefreshInterval);
            return _snapshot ??= new Snapshot(
                FirewallOptions.Inert,
                new ConcurrentDictionary<string, IpFirewallRule>(StringComparer.OrdinalIgnoreCase),
                IpAllowlist.Parse(null));
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<Snapshot> LoadAsync()
    {
        using var scope = _scopes.CreateScope();
        var sp = scope.ServiceProvider;

        await FlushPendingHitsAsync(sp);

        var options = FirewallOptions.Inert;
        try
        {
            var admin = await sp.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
            if (admin is not null)
            {
                var settings = await sp.GetRequiredService<ISettingRepository>().GetSettingsAsync(admin.Id);
                options = FirewallOptions.From(settings);
            }
        }
        catch (Exception ex)
        {
            // No settings yet (fresh install / setup wizard) — stay inert rather than guess.
            _log.LogDebug(ex, "Firewall settings load failed; firewall inert this cycle.");
        }

        var repo = sp.GetRequiredService<IIpFirewallRepository>();

        if (DateTime.UtcNow >= _nextPurgeUtc)
        {
            _nextPurgeUtc = DateTime.UtcNow.Add(PurgeInterval);
            try { await repo.PurgeExpiredAsync(); }
            catch (Exception ex) { _log.LogDebug(ex, "Firewall expired-rule purge failed (non-fatal)."); }
        }

        var rules = new ConcurrentDictionary<string, IpFirewallRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in await repo.GetActiveAsync())
            rules[rule.IpAddress] = rule;

        return new Snapshot(options, rules, IpAllowlist.Parse(options.Allowlist));
    }

    private async Task FlushPendingHitsAsync(IServiceProvider sp)
    {
        if (_pendingHits.IsEmpty) return;
        try
        {
            var batch = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _pendingHits.Keys)
                if (_pendingHits.TryRemove(key, out var count) && count > 0) batch[key] = count;

            if (batch.Count > 0)
                await sp.GetRequiredService<IIpFirewallRepository>().RecordHitsAsync(batch);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Firewall hit-counter flush failed (non-fatal).");
        }
    }

    private static string? Trim(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length > max ? s[..max] : s);
}
