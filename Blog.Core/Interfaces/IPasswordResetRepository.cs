using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IPasswordResetRepository
{
    Task CreateAsync(PasswordResetToken token);

    /// <summary>The token row for this hash, used or not, expired or not — the caller decides usability.</summary>
    Task<PasswordResetToken?> GetByHashAsync(string tokenHash);

    /// <summary>Marks one token consumed. A token is single-use.</summary>
    Task MarkUsedAsync(Guid id);

    /// <summary>Consumes every outstanding token for the user — after a successful reset, or when a new link is issued.</summary>
    Task InvalidateForUserAsync(Guid userId);

    /// <summary>Links issued for the user inside the window, for the per-address request limit.</summary>
    Task<int> CountRecentForUserAsync(Guid userId, TimeSpan window);

    /// <summary>Removes rows that expired before the cut-off. Run by the scheduler.</summary>
    Task<int> PruneExpiredAsync(DateTime olderThanUtc);
}
