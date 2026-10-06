using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// The pure decisions behind webhooks, kept here so each is unit-tested without a network or a
/// database: which addresses may be called (SSRF guard), how a payload is signed, when a failed
/// delivery is retried, and which event a post save produces.
/// </summary>
public static class WebhookRules
{
    public const int MaxAttempts = 5;
    public const int MaxUrlLength = 2048;

    // ── Targets (server-side request forgery guard) ──────────────────────────

    /// <summary>
    /// Validates a webhook URL as typed: absolute http(s), no embedded credentials, not a local name,
    /// and not a literal internal address. Host names are resolved and checked again at connect time
    /// (<see cref="IsForbiddenAddress"/>), which is what stops DNS tricks. Returns null when valid.
    /// </summary>
    public static string? ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Enter the address that should receive the webhook.";
        url = url.Trim();
        if (url.Length > MaxUrlLength) return $"Addresses can be at most {MaxUrlLength} characters.";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return "Enter a full address starting with https:// (or http://).";
        if (!string.IsNullOrEmpty(uri.UserInfo)) return "Put credentials in the signing secret, not in the address.";

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host == "localhost" || host.EndsWith(".localhost") || host.EndsWith(".local") || host.EndsWith(".internal") || !host.Contains('.') && !IPAddress.TryParse(host, out _))
            return "Webhooks can only be sent to public addresses, not to this server or a local network name.";
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal) && IsForbiddenAddress(literal))
            return "Webhooks can only be sent to public addresses, not to this server or a private network.";
        return null;
    }

    /// <summary>
    /// True for any address a webhook must never reach: loopback, private, link-local (cloud metadata
    /// lives at 169.254.169.254), carrier-grade NAT, multicast, unspecified, reserved, and the IPv6
    /// equivalents (unique-local, link-local, site-local, IPv4-mapped forms of the above).
    /// </summary>
    public static bool IsForbiddenAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0 || b[0] == 10 || b[0] == 127
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)       // carrier-grade NAT
                || (b[0] == 169 && b[1] == 254)                     // link-local, cloud metadata
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)          // IETF protocol assignments
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))      // benchmarking
                || b[0] >= 224;                                     // multicast and reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC;                           // unique-local fc00::/7
        }

        return true;   // anything else is not something a webhook should call
    }

    /// <summary>
    /// The connect-time rule: a host is reachable only when it resolved to at least one address and
    /// every address it resolved to is public. One private answer among public ones (a common DNS
    /// rebinding set-up) refuses the whole host.
    /// </summary>
    public static bool AreAllPublic(IReadOnlyCollection<IPAddress> addresses) =>
        addresses.Count > 0 && addresses.All(a => !IsForbiddenAddress(a));

    // ── Signing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>X-Inkwell-Signature</c> value: <c>t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA256 of "t.body"&gt;</c>.
    /// Signing the timestamp with the body lets a receiver reject replays of an old delivery.
    /// </summary>
    public static string Sign(string secret, long unixSeconds, string body)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{unixSeconds}.{body}"));
        return $"t={unixSeconds},v1={Convert.ToHexString(mac).ToLowerInvariant()}";
    }

    /// <summary>A new signing secret: 32 random bytes, URL-safe base64 (43 characters).</summary>
    public static string NewSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ── Retries ──────────────────────────────────────────────────────────────

    /// <summary>
    /// When to try again after failed attempt number <paramref name="attempts"/> (1-based), or null
    /// when every attempt is used: 1 min, 5 min, 30 min, 2 h, then give up.
    /// </summary>
    public static TimeSpan? RetryDelay(int attempts) => attempts switch
    {
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(30),
        4 => TimeSpan.FromHours(2),
        _ => null,
    };

    public static bool IsSuccess(int statusCode) => statusCode is >= 200 and < 300;

    // ── Which event a save produces ──────────────────────────────────────────

    /// <summary>Readers can see the post: published, or scheduled with its time already passed.</summary>
    public static bool IsLive(PostStatus status, DateTime? scheduledAtUtc, DateTime nowUtc) =>
        EditorialWorkflow.IsLive(status, scheduledAtUtc, nowUtc);

    /// <summary>
    /// The event a post save produces, from what readers could see before and after, or null for
    /// none (a draft saved as a draft, or a post scheduled for later — the delivery job announces
    /// that one when its time comes).
    /// </summary>
    public static string? EventForPostSave(bool wasLive, bool isLive) => (wasLive, isLive) switch
    {
        (false, true) => WebhookEvents.PostPublished,
        (true, true)  => WebhookEvents.PostUpdated,
        (true, false) => WebhookEvents.PostUnpublished,
        _             => null,
    };
}
