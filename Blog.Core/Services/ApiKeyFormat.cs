using System.Security.Cryptography;
using System.Text;

namespace Blog.Core.Services;

/// <summary>
/// The shape of an API key and what its scopes allow. A key looks like
/// <c>ink_ab12cd34_&lt;43 random characters&gt;</c>: a fixed marker (so a leaked key is recognisable to
/// secret scanners), a short public prefix the user sees in the Desk, and 32 random bytes. The key is
/// high-entropy random data, so one SHA-256 is the right hash (a slow password hash would add nothing
/// and cost a hash per request). Scopes can only narrow what the key's owner may do, never widen it.
/// Pure.
/// </summary>
public static class ApiKeyFormat
{
    public const string Marker = "ink_";
    public const int PrefixLength = 8;

    public const string Read = "read";
    public const string Write = "write";
    public const string Publish = "publish";
    public const string Comments = "comments";
    public const string Subscribers = "subscribers";

    /// <summary>Every scope, in the order the Desk lists them, with what each one means to a person.</summary>
    public static readonly IReadOnlyList<(string Scope, string Label, string Meaning)> AllScopes =
    [
        (Read, "Read", "Read posts, pages, categories, tags and comments, including your own drafts."),
        (Write, "Write drafts", "Create posts and pages as drafts and edit drafts. Cannot publish, and cannot change or delete anything already published."),
        (Publish, "Publish", "Publish, schedule, unpublish and delete posts, if your own account may."),
        (Comments, "Moderate comments", "Approve and delete comments, if your own account may."),
        (Subscribers, "Read subscribers", "Read the subscriber list, email addresses included. Only for an Admin account; every read is in the audit trail."),
    ];

    /// <summary>Desk permissions each scope lets through. Anything not listed (settings, themes, users, media) never reaches a key.</summary>
    private static readonly Dictionary<string, string[]> PermissionsByScope = new(StringComparer.OrdinalIgnoreCase)
    {
        [Read] = [],
        [Write] = ["posts.edit", "pages.manage"],
        [Publish] = ["posts.publish", "posts.delete"],
        [Comments] = ["comments.manage"],
        [Subscribers] = [],   // no Desk permission: the subscribers endpoint checks the Admin role and this scope itself
    };

    /// <summary>A new key and its public prefix. The caller stores <see cref="Hash"/> of the key, never the key.</summary>
    public static (string Key, string Prefix) Generate()
    {
        var prefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(PrefixLength / 2)).ToLowerInvariant();
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        return ($"{Marker}{prefix}_{secret}", prefix);
    }

    /// <summary>Lower-case hex SHA-256 of the whole key.</summary>
    public static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    /// <summary>True when the text has the shape of a key; lets the API tell a key from a sign-in token without a database call.</summary>
    public static bool LooksLikeKey(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Marker, StringComparison.Ordinal)) return false;
        var rest = value.AsSpan(Marker.Length);
        if (rest.Length != PrefixLength + 1 + 43 || rest[PrefixLength] != '_') return false;
        foreach (var c in rest[..PrefixLength]) if (!char.IsAsciiHexDigitLower(c)) return false;
        foreach (var c in rest[(PrefixLength + 1)..]) if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return false;
        return true;
    }

    /// <summary>Known scopes only, lower case, de-duplicated, in the canonical order; "read" is always present.</summary>
    public static IReadOnlyList<string> NormalizeScopes(IEnumerable<string>? scopes)
    {
        var wanted = new HashSet<string>((scopes ?? []).Select(s => (s ?? "").Trim()), StringComparer.OrdinalIgnoreCase) { Read };
        return AllScopes.Select(s => s.Scope).Where(wanted.Contains).ToList();
    }

    public static IReadOnlyList<string> ParseScopes(string? stored) =>
        NormalizeScopes((stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));

    public static string JoinScopes(IEnumerable<string>? scopes) => string.Join(",", NormalizeScopes(scopes));

    /// <summary>
    /// The owner's permissions that survive this key's scopes: the intersection, so a key never holds a
    /// permission its owner lacks, and never one its scopes do not name.
    /// </summary>
    public static IReadOnlyList<string> AllowedPermissions(IEnumerable<string> scopes, IEnumerable<string> ownerPermissions)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in NormalizeScopes(scopes))
            foreach (var p in PermissionsByScope[scope]) allowed.Add(p);
        return ownerPermissions.Where(allowed.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A name a person typed for a key, trimmed to something safe to show and to log.</summary>
    public static string CleanName(string? name)
    {
        var cleaned = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length > 60 ? cleaned[..60].TrimEnd() : cleaned;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
