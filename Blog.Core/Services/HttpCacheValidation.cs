using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Blog.Core.Services;

/// <summary>
/// Conditional-GET arithmetic for public pages (RFC 7232). A page's validators are derived from
/// everything that changes its markup — the post, its comments, the theme, the site settings and
/// the deployed build — so a 304 is only ever sent for a byte-identical page. Pure and testable.
/// </summary>
public static class HttpCacheValidation
{
    /// <summary>A weak ETag over the given parts. Weak because whitespace and ordering are not guaranteed.</summary>
    public static string WeakETag(params object?[] parts)
    {
        var joined = string.Join("", parts.Select(p => p switch
        {
            null => "",
            DateTime d => d.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => p.ToString(),
        }));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return "W/\"" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant() + "\"";
    }

    /// <summary>HTTP dates carry whole seconds; a Last-Modified must be truncated so the round trip compares equal.</summary>
    public static DateTime ToHttpDate(DateTime utc)
    {
        var u = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return new DateTime(u.Ticks - (u.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }

    /// <summary>
    /// True when the client's copy is current. If-None-Match wins when present; If-Modified-Since
    /// is consulted only without it. Either header absent or unparseable means "not fresh".
    /// </summary>
    public static bool IsFresh(string? ifNoneMatch, string etag, string? ifModifiedSince, DateTime lastModifiedUtc)
    {
        if (!string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            if (ifNoneMatch.Trim() == "*") return true;
            foreach (var candidate in ifNoneMatch.Split(','))
                if (TagsMatch(candidate.Trim(), etag)) return true;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ifModifiedSince)
            && DateTime.TryParse(ifModifiedSince, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var since))
            return ToHttpDate(lastModifiedUtc) <= since;

        return false;
    }

    // Weak comparison: W/"x" matches "x" and W/"x".
    private static bool TagsMatch(string a, string b) => Strip(a) == Strip(b) && Strip(a).Length > 0;
    private static string Strip(string tag) => (tag.StartsWith("W/", StringComparison.OrdinalIgnoreCase) ? tag[2..] : tag).Trim().Trim('"');
}
