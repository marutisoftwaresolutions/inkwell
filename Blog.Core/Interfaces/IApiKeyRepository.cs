using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IApiKeyRepository
{
    Task<Guid> CreateAsync(ApiKey key);
    /// <summary>The key with this hash, revoked or expired ones included (the caller decides), or null.</summary>
    Task<ApiKey?> GetByHashAsync(string keyHash);
    /// <summary>A user's keys, newest first, revoked ones included so the list shows what happened to them.</summary>
    Task<List<ApiKey>> ListForUserAsync(Guid userId);
    /// <summary>Revokes one of the user's own keys; false when it is not theirs or is already revoked.</summary>
    Task<bool> RevokeAsync(Guid id, Guid userId);
    Task TouchAsync(Guid id, DateTime usedAtUtc);
    Task<int> CountActiveForUserAsync(Guid userId);
    /// <summary>Revokes every usable key of an account (after a password reset: whoever had the old password may have made one). Returns how many.</summary>
    Task<int> RevokeAllForUserAsync(Guid userId, DateTime nowUtc);
}
