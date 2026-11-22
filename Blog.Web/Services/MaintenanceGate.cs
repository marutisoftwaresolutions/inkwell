using Blog.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Blog.Web.Services;

/// <summary>
/// Host-level switches for two features an operator may not want on a shared or public install
/// (the demo site, a multi-tenant host). Read from configuration, so no Desk user can change them.
/// </summary>
public static class HostSwitches
{
    /// <summary>
    /// Site code (Settings → Custom code). <c>Site:AllowCodeInjection</c> decides; unset, it is allowed on a
    /// single-site install and not in cloud mode, where one tenant's script would run on a shared origin.
    /// </summary>
    public static bool CodeInjectionAllowed(IConfiguration config, ITenantContext tenant) =>
        config.GetValue<bool?>("Site:AllowCodeInjection") ?? !tenant.IsCloudMode;

    /// <summary>Maintenance mode. <c>Site:AllowMaintenanceMode=false</c> removes the switch and ignores a stored "on".</summary>
    public static bool MaintenanceModeAllowed(IConfiguration config) => config.GetValue("Site:AllowMaintenanceMode", true);
}

/// <summary>
/// Whether maintenance mode is on for a site, remembered for half a minute so the check costs readers
/// no database call. Fails open: if the setting cannot be read, the site is served. Saving Settings
/// calls <see cref="Forget"/> so the switch takes effect at once on this instance.
/// </summary>
public sealed class MaintenanceGate
{
    public sealed record State(bool On, string SiteName, string Message, string Language);

    public static readonly State Off = new(false, "", "", "en");
    private static readonly TimeSpan RememberFor = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopes;
    private readonly bool _allowed;
    private int _version;

    public MaintenanceGate(IMemoryCache cache, IServiceScopeFactory scopes, IConfiguration config)
    {
        _cache = cache;
        _scopes = scopes;
        _allowed = HostSwitches.MaintenanceModeAllowed(config);
    }

    /// <summary>The state for a tenant (<see cref="Guid.Empty"/> on a single-site install: the first Admin's settings).</summary>
    public async Task<State> GetAsync(Guid tenantId)
    {
        if (!_allowed) return Off;
        var key = ("maintenance", Volatile.Read(ref _version), tenantId);
        if (_cache.TryGetValue(key, out State? known) && known is not null) return known;

        State state;
        try
        {
            using var scope = _scopes.CreateScope();
            var ownerId = tenantId;
            if (ownerId == Guid.Empty)
                ownerId = (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync())?.Id ?? Guid.Empty;
            var s = await scope.ServiceProvider.GetRequiredService<ISettingRepository>().GetSettingsAsync(ownerId);
            state = s.MaintenanceModeEnabled
                ? new State(true, s.SiteName ?? "", s.MaintenanceMessage ?? "", string.IsNullOrWhiteSpace(s.SiteLanguage) ? "en" : s.SiteLanguage.Trim())
                : Off;
        }
        catch
        {
            state = Off;   // unreadable settings never take the site down
        }
        Remember(tenantId, state);
        return state;
    }

    /// <summary>Remembers a state for half a minute (also how a test sets one without a database).</summary>
    public void Remember(Guid tenantId, State state) => _cache.Set(("maintenance", Volatile.Read(ref _version), tenantId), state, RememberFor);

    /// <summary>Drops what is remembered, after Settings are saved.</summary>
    public void Forget() => Interlocked.Increment(ref _version);
}
