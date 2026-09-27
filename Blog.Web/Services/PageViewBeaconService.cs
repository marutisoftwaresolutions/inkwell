using System.Globalization;
using System.Runtime.CompilerServices;
using Blog.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace Blog.Web.Services;

/// <summary>
/// Page views are counted from a beacon the page's own script sends, not from the server seeing a
/// GET. A client that never runs JavaScript — the flood tools that recorded 1.3M homepage "views" on
/// the live site in September 2026 — never sends it, which is how GA already counts.
///
/// The page carries a token naming its path and when it was issued, protected by Data Protection,
/// so a beacon cannot be forged for a path or backdated. It is valid for <see cref="Lifetime"/>:
/// long enough that a page served from the browser cache (a 304 keeps the old HTML) still counts.
/// A token can be replayed within that window, so each address is also capped at
/// <see cref="MaxPerAddressPerMinute"/> beacons. This raises the cost of faking views; it does not
/// make it impossible.
/// </summary>
public sealed class PageViewBeaconService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    public const int MaxPerAddressPerMinute = 30;

    private readonly IDataProtector _protector;
    private readonly IMemoryCache _cache;

    public PageViewBeaconService(IDataProtectionProvider provider, IMemoryCache cache)
    {
        _protector = provider.CreateProtector("Inkwell.PageViewBeacon.v1");
        _cache = cache;
    }

    /// <summary>
    /// The token to embed in this response, or null when the response must not count as a view:
    /// not a GET, an error status, a signed-in user, or a path that is not a post, page or home.
    /// </summary>
    public string? TokenFor(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)) return null;
        if (context.Response.StatusCode >= 400) return null;
        if (context.User.Identity?.IsAuthenticated == true) return null;
        var path = context.Request.Path.Value;
        return PageViewPolicy.IsTrackablePath(path) ? Issue(path!) : null;
    }

    public string Issue(string path, DateTime? utcNow = null)
    {
        var issued = (utcNow ?? DateTime.UtcNow).Ticks.ToString(CultureInfo.InvariantCulture);
        return _protector.Protect(issued + "|" + path.ToLowerInvariant());
    }

    /// <summary>The path the token was issued for, or null when it is invalid, expired or for another path.</summary>
    public string? Validate(string? token, string? path, DateTime? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(token) || !PageViewPolicy.IsTrackablePath(path)) return null;
        try
        {
            var raw = _protector.Unprotect(token);
            var bar = raw.IndexOf('|');
            if (bar <= 0) return null;
            if (!long.TryParse(raw[..bar], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) return null;
            var issued = new DateTime(ticks, DateTimeKind.Utc);
            var now = utcNow ?? DateTime.UtcNow;
            if (issued > now.AddMinutes(5) || now - issued > Lifetime) return null;

            var tokenPath = raw[(bar + 1)..];
            return string.Equals(tokenPath, path!.ToLowerInvariant(), StringComparison.Ordinal) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Counts a beacon against the address's one-minute budget; false once it is spent.</summary>
    public bool TryConsumeBudget(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return true;
        var key = "pvb:" + ip;
        var counter = _cache.GetOrCreate(key, e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return new StrongBox<int>();
        })!;
        return Interlocked.Increment(ref counter.Value) <= MaxPerAddressPerMinute;
    }
}
