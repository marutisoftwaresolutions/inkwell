using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);
    Task<User?> GetByEmailAsync(string email);
    Task<Guid> CreateAsync(User user);
    Task UpdateAsync(User user);
    Task<bool> AnyUsersExistAsync();
    Task<User?> GetBySlugAsync(string slug);
    Task<User?> GetFirstAdminAsync();
    Task<List<User>> GetAllUsersAsync();
    Task DeleteAsync(Guid id);

    /// <summary>Generates a URL-safe slug from <paramref name="baseName"/> that is unique across Users
    /// (appending -2, -3, … on collision). Pass the user's own Id as <paramref name="excludeId"/> when editing.</summary>
    Task<string> GenerateUniqueSlugAsync(string baseName, Guid? excludeId = null);

    /// <summary>Assigns a unique slug to every user that currently has none (NULL/empty). Idempotent.
    /// Returns the number of users updated. Enables author E-E-A-T pages for pre-existing users.</summary>
    Task<int> BackfillMissingSlugsAsync();

    // ── Two-factor sign-in (ADR-0021) ─────────────────────────────────────────
    Task<UserTwoFactor?> GetTwoFactorAsync(Guid userId);
    /// <summary>Turns two-factor on with a new secret and recovery codes (replacing any previous ones).</summary>
    Task EnableTwoFactorAsync(Guid userId, string secretProtected, IReadOnlyList<string> recoveryHashes, long firstStep, DateTime enabledAtUtc);
    Task DisableTwoFactorAsync(Guid userId);
    Task SaveRecoveryHashesAsync(Guid userId, IReadOnlyList<string> recoveryHashes);
    /// <summary>
    /// Records an accepted code's time step, only if it is newer than the last one. False when another request
    /// already used this or a later step, so one code can never sign in twice, even in a race.
    /// </summary>
    Task<bool> TryAdvanceTwoFactorStepAsync(Guid userId, long step);
}
