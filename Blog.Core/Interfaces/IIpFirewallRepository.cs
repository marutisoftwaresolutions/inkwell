using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

/// <summary>Persistence for IP firewall rules (see <see cref="IpFirewallRule"/>).</summary>
public interface IIpFirewallRepository
{
    /// <summary>All rules that still apply — allow rules plus unexpired blocks. Used to build the in-memory snapshot.</summary>
    Task<IReadOnlyList<IpFirewallRule>> GetActiveAsync();

    Task<IpFirewallRule?> GetByIpAsync(string ipAddress);

    Task<IpFirewallRule?> GetByIdAsync(long id);

    /// <summary>Insert or update the rule for <see cref="IpFirewallRule.IpAddress"/>. Returns the stored row.</summary>
    Task<IpFirewallRule> UpsertAsync(IpFirewallRule rule);

    /// <summary>Delete a rule (unblock / remove allow). Returns true when a row was removed.</summary>
    Task<bool> DeleteAsync(long id);

    Task<bool> DeleteByIpAsync(string ipAddress);

    /// <summary>Flush buffered denied-request counters: IP → number of refused requests.</summary>
    Task RecordHitsAsync(IReadOnlyDictionary<string, int> hits);

    Task<(IReadOnlyList<IpFirewallRule> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? kind, string? search);

    Task<FirewallStats> GetStatsAsync();

    /// <summary>Delete expired block rules. Returns the number removed.</summary>
    Task<int> PurgeExpiredAsync();
}
