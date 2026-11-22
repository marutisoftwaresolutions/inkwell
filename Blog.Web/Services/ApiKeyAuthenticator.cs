using System.Security.Claims;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;

namespace Blog.Web.Services;

/// <summary>
/// Turns an API key into a caller. The key acts as the account that created it, re-read on every
/// request, so disabling the account, changing its role or withdrawing a permission takes effect at
/// once. The caller's permission claims are the account's permissions narrowed by the key's scopes
/// (<see cref="ApiKeyFormat.AllowedPermissions"/>): a key can never do more than its owner, and
/// settings, themes, users and media are never reachable with a key at all. The key's id, name and
/// scopes travel as claims so the audit trail can say which key acted.
/// </summary>
public static class ApiKeyAuthenticator
{
    public const string KeyIdClaim = "ApiKeyId";
    public const string KeyNameClaim = "ApiKeyName";
    public const string ScopeClaim = "ApiKeyScope";

    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    /// <summary>The caller for this key, or null when the key is unknown, expired, revoked, or its account cannot sign in.</summary>
    public static async Task<ClaimsPrincipal?> AuthenticateAsync(IServiceProvider services, string key)
    {
        if (!ApiKeyFormat.LooksLikeKey(key)) return null;

        var keys = services.GetRequiredService<IApiKeyRepository>();
        var stored = await keys.GetByHashAsync(ApiKeyFormat.Hash(key));
        var now = DateTime.UtcNow;
        if (stored is null || !stored.IsUsable(now)) return null;

        var user = await services.GetRequiredService<IUserRepository>().GetByIdAsync(stored.UserId);
        if (user is null || !user.IsActive) return null;

        var scopes = ApiKeyFormat.ParseScopes(stored.Scopes);
        var ownerPermissions = await services.GetRequiredService<IRoleRepository>().GetPermissionNamesForUserAsync(user.Id);
        var claims = UserSignInService.Claims(user, ApiKeyFormat.AllowedPermissions(scopes, ownerPermissions)).ToList();
        claims.Add(new Claim(KeyIdClaim, stored.Id.ToString()));
        claims.Add(new Claim(KeyNameClaim, stored.Name));
        claims.AddRange(scopes.Select(s => new Claim(ScopeClaim, s)));

        // "Last used" is for people reading the key list, so one write per key every few minutes is
        // enough; a request never waits on it and never fails because of it.
        var cache = services.GetService<IMemoryCache>();
        var gate = "apikey-touch:" + stored.Id;
        if (cache is null || !cache.TryGetValue(gate, out _))
        {
            cache?.Set(gate, true, TouchInterval);
            try { await keys.TouchAsync(stored.Id, now); } catch { /* best effort */ }
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme));
    }

    public static bool IsKeyCaller(ClaimsPrincipal user) => user.HasClaim(c => c.Type == KeyIdClaim);

    public static bool HasScope(ClaimsPrincipal user, string scope) => user.HasClaim(ScopeClaim, scope);
}
