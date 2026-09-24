using System.Security.Cryptography;
using System.Text;

namespace Blog.Core.Services;

/// <summary>
/// Token handling for self-service password reset. The raw token travels only in the email link;
/// the database stores its SHA-256 hash, so a leaked table cannot be replayed. Pure and testable.
/// </summary>
public static class PasswordResetTokens
{
    /// <summary>A link is good for this long. Short enough that a forwarded email goes stale, long enough to read it.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>Requests one address may make inside <see cref="RequestWindow"/> before further ones are ignored.</summary>
    public const int MaxRequestsPerWindow = 3;
    public static readonly TimeSpan RequestWindow = TimeSpan.FromHours(1);

    public const int MinPasswordLength = 8;

    /// <summary>256 bits of randomness, URL-safe, no padding — safe inside a query string.</summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Lower-case hex SHA-256 of the raw token — what the table stores and what lookups use.</summary>
    public static string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Whether another reset may be issued given how many were already issued for the address inside
    /// <see cref="RequestWindow"/>. The fourth in an hour is silently ignored (the page never reveals
    /// whether the address exists). A negative count — a broken query — is treated as none.
    /// </summary>
    public static bool CanRequest(int recentCount) => recentCount < MaxRequestsPerWindow;

    /// <summary>A token is usable when it has not been used and has not expired.</summary>
    public static bool IsUsable(DateTime? usedAtUtc, DateTime expiresAtUtc, DateTime nowUtc) =>
        usedAtUtc is null && nowUtc < expiresAtUtc;

    /// <summary>
    /// The origin a reset link is built on. Behind a TLS-terminating proxy the request arrives as
    /// <c>http://</c> on an internal host, so when the operator has configured a canonical host that
    /// wins, always as <c>https</c> — the same host the canonical-host redirect enforces. A bare host
    /// (<c>www.example.com</c>) or a full origin (<c>https://www.example.com</c>) are both accepted;
    /// a trailing slash is dropped. Without one the request's own scheme and host are used.
    /// </summary>
    public static string ResolveLinkBase(string? canonicalHost, string scheme, string host)
    {
        var canonical = (canonicalHost ?? string.Empty).Trim().TrimEnd('/');
        if (canonical.Length > 0)
        {
            if (canonical.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                canonical.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return canonical;
            return "https://" + canonical;
        }

        var s = string.IsNullOrWhiteSpace(scheme) ? "https" : scheme.Trim();
        return $"{s}://{(host ?? string.Empty).Trim()}";
    }

    /// <summary>Server-side password policy, applied to the reset form and stated in its hint.</summary>
    public static string? ValidateNewPassword(string? password, string? confirm)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            return $"Password must be at least {MinPasswordLength} characters.";
        if (!string.Equals(password, confirm, StringComparison.Ordinal))
            return "Passwords do not match.";
        return null;
    }
}
