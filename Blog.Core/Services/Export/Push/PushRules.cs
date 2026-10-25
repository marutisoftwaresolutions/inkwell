using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Blog.Core.Services.Export.Push;

/// <summary>What a push does with one post or page, given what the target already has.</summary>
public enum PushAction { Create, Update, Skip }

/// <summary>
/// The decisions behind a push to WordPress or Ghost (ADR-0029), kept pure so they can be tested without a
/// network: which addresses may be pushed to, how a Ghost Admin API key becomes a token, what an existing
/// slug means under each conflict policy, how long to wait after a failure or a rate limit, and the order
/// pages and posts are sent in.
/// </summary>
public static class PushRules
{
    public const int MaxAttempts = 3;
    public const int MaxSiteUrlLength = 300;
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(1);

    // ── Target address ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates the site address typed on the push form and returns it normalised (scheme, host, port and
    /// path, no trailing slash), or an error a person can act on. HTTPS only and public hosts only, unless the
    /// server allows private targets for testing (<c>Exports:PushAllowPrivateTargets</c>). Host names are
    /// resolved and checked again when the connection is made, which is what stops DNS tricks.
    /// </summary>
    public static (string? Url, string? Error) NormaliseSiteUrl(string? input, bool allowPrivate)
    {
        if (string.IsNullOrWhiteSpace(input)) return (null, "Enter the address of the site to push to, for example https://www.example.com.");
        var text = input.Trim();
        if (!text.Contains("://")) text = "https://" + text;
        if (text.Length > MaxSiteUrlLength) return (null, $"Addresses can be at most {MaxSiteUrlLength} characters.");
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return (null, "Enter a full address starting with https://.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) return (null, "Leave the user name out of the address; it goes in its own box.");
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return (null, "Enter the site's address without anything after a ? or #.");
        if (uri.Scheme != Uri.UriSchemeHttps && !allowPrivate)
            return (null, "The address must start with https://, so the password or key is never sent unencrypted.");

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (!allowPrivate)
        {
            if (host == "localhost" || host.EndsWith(".localhost") || host.EndsWith(".local") || host.EndsWith(".internal")
                || (!host.Contains('.') && !IPAddress.TryParse(host.Trim('[', ']'), out _)))
                return (null, "Push only goes to a public site, not to this server or a local network name.");
            if (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && WebhookRules.IsForbiddenAddress(ip))
                return (null, "Push only goes to a public site, not to this server or a private network.");
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        return (uri.GetLeftPart(UriPartial.Authority) + path, null);
    }

    // ── Ghost Admin API key ──────────────────────────────────────────────────

    /// <summary>
    /// Splits a Ghost Admin API key (<c>24-hex id</c><c>:</c><c>64-hex secret</c>, from Ghost Admin →
    /// Settings → Integrations). Returns null with an error when the shape is wrong, so a Content API key
    /// (no colon) is caught before anything is sent.
    /// </summary>
    public static (string? Id, byte[]? Secret, string? Error) ParseGhostKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return (null, null, "Enter the Admin API key from Ghost Admin → Settings → Integrations.");
        var parts = key.Trim().Split(':');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return (null, null, "That is not an Admin API key. It has two parts separated by a colon; the Content API key has none.");
        if (parts[1].Length % 2 != 0 || !parts[1].All(Uri.IsHexDigit))
            return (null, null, "The part after the colon should contain only the letters a–f and digits. Copy the Admin API key again.");
        return (parts[0], Convert.FromHexString(parts[1]), null);
    }

    /// <summary>
    /// A Ghost Admin API token: an HS256 JWT with the key id as <c>kid</c>, audience <c>/admin/</c>, valid for
    /// five minutes (Ghost refuses longer), signed with the hex-decoded secret.
    /// </summary>
    public static string GhostToken(string keyId, byte[] secret, DateTimeOffset now)
    {
        static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["alg"] = "HS256", ["typ"] = "JWT", ["kid"] = keyId });
        var iat = now.ToUnixTimeSeconds();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["iat"] = iat, ["exp"] = iat + 300, ["aud"] = "/admin/" });
        var unsigned = B64(header) + "." + B64(payload);
        var signature = HMACSHA256.HashData(secret, Encoding.ASCII.GetBytes(unsigned));
        return unsigned + "." + B64(signature);
    }

    // ── Conflicts ─────────────────────────────────────────────────────────────

    /// <summary>What to do with an item, given the item the target already has with the same slug (if any).</summary>
    public static PushAction Decide(RemoteItem? existing, PushConflictPolicy policy) => existing is null
        ? PushAction.Create
        : policy switch
        {
            PushConflictPolicy.Update => PushAction.Update,
            PushConflictPolicy.Duplicate => PushAction.Create,
            _ => PushAction.Skip,
        };

    // ── Failures and rate limits ─────────────────────────────────────────────

    /// <summary>Wait before the next attempt at an item, by attempts already made: 1 minute, then 5.</summary>
    public static TimeSpan RetryDelay(int attempts) => attempts <= 1 ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(5);

    /// <summary>True for responses that mean "slow down" rather than "this item is wrong".</summary>
    public static bool IsThrottle(int status) => status is 429 or 503;

    /// <summary>True for responses that mean the credential is no good (or lacks a permission).</summary>
    public static bool IsAuthFailure(int status) => status is 401 or 403;

    /// <summary>
    /// How long to pause after a 429 or 503: the <c>Retry-After</c> header as seconds or an HTTP date, capped at
    /// <see cref="MaxRetryAfter"/>; one minute when it is absent or unreadable.
    /// </summary>
    public static TimeSpan RetryAfter(string? header, DateTimeOffset now)
    {
        TimeSpan wait = DefaultRetryAfter;
        if (!string.IsNullOrWhiteSpace(header))
        {
            var h = header.Trim();
            if (int.TryParse(h, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                wait = TimeSpan.FromSeconds(seconds);
            else if (DateTimeOffset.TryParseExact(h, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                wait = at - now;
        }
        if (wait < TimeSpan.FromSeconds(5)) wait = TimeSpan.FromSeconds(5);
        return wait > MaxRetryAfter ? MaxRetryAfter : wait;
    }

    // ── Order ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The order a push sends things in: pages first, each parent before its children (so WordPress can link
    /// them), then posts in the order given. A page whose parent is not part of the push goes with the top level.
    /// </summary>
    public static IReadOnlyList<NewPushItem> Order(IEnumerable<ExportItemRef> items)
    {
        var list = items.ToList();
        var pages = list.Where(i => i.Kind == PushItemKind.Page).ToList();
        var pageIds = pages.Select(p => p.Id).ToHashSet();
        var children = pages.Where(p => p.ParentId is Guid g && pageIds.Contains(g) && g != p.Id)
                            .GroupBy(p => p.ParentId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var ordered = new List<ExportItemRef>();
        var seen = new HashSet<Guid>();
        void Visit(ExportItemRef p)
        {
            if (!seen.Add(p.Id)) return;
            ordered.Add(p);
            if (children.TryGetValue(p.Id, out var kids)) foreach (var k in kids) Visit(k);
        }
        foreach (var root in pages.Where(p => p.ParentId is not Guid g || !pageIds.Contains(g) || g == p.Id)) Visit(root);
        foreach (var p in pages) Visit(p);   // a cycle in the data: still send every page once
        ordered.AddRange(list.Where(i => i.Kind == PushItemKind.Post));
        return ordered.Select((i, n) => new NewPushItem(i.Kind, i.Id.ToString(), Trim(i.Title, 300), n + 1)).ToList();
    }

    // ── Media ────────────────────────────────────────────────────────────────

    /// <summary>The media type an upload is sent with, from its extension; null for files a push does not send.</summary>
    /// <summary>
    /// The upload type for an image, or null when it should not be uploaded (the post then links to it on this site).
    /// WordPress refuses SVG unless a plugin allows it, answering with a server error, so SVG is never sent there.
    /// </summary>
    public static string? ImageContentType(string path, ExportTarget? target = null) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".svg" when target == ExportTarget.WordPress => null,
        ".webp" => "image/webp",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".avif" => "image/avif",
        _ => null,
    };

    /// <summary>Shortens a message or title for storage, at a word where possible.</summary>
    public static string? Trim(string? s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        var cut = s[..(max - 1)];
        var space = cut.LastIndexOf(' ');
        return (space > max / 2 ? cut[..space] : cut).TrimEnd() + "…";
    }
}
