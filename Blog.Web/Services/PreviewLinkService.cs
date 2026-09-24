using System.Globalization;
using Microsoft.AspNetCore.DataProtection;

namespace Blog.Web.Services;

/// <summary>
/// Signed, expiring links that show an unpublished post to someone without an account. The token
/// is the post id and an expiry, protected by Data Protection: nothing is stored, nothing can be
/// forged, and a rotated key ring simply makes old links stop working.
/// </summary>
public sealed class PreviewLinkService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private readonly IDataProtector _protector;

    public PreviewLinkService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Inkwell.PreviewLink.v1");
    }

    public string Issue(Guid postId, DateTime? utcNow = null)
    {
        var expires = (utcNow ?? DateTime.UtcNow) + Lifetime;
        var payload = postId.ToString("N") + "|" + expires.Ticks.ToString(CultureInfo.InvariantCulture);
        return _protector.Protect(payload);
    }

    /// <summary>The post the token names and when it stops working, or null for anything invalid or expired.</summary>
    public (Guid PostId, DateTime ExpiresUtc)? Validate(string? token, DateTime? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var parts = _protector.Unprotect(token).Split('|');
            if (parts.Length != 2) return null;
            if (!Guid.TryParseExact(parts[0], "N", out var id)) return null;
            if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) return null;
            var expires = new DateTime(ticks, DateTimeKind.Utc);
            return (utcNow ?? DateTime.UtcNow) < expires ? (id, expires) : null;
        }
        catch
        {
            return null;
        }
    }
}
