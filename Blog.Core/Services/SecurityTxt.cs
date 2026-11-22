using System.Globalization;
using System.Text;

namespace Blog.Core.Services;

/// <summary>
/// Builds <c>/.well-known/security.txt</c> (RFC 9116): where a researcher should report a problem
/// with this site. The contact is the operator's, taken from configuration; without one there is no
/// file, because a security.txt that points nowhere is worse than none. <c>Expires</c> is required by
/// the RFC and is always generated a fixed number of days ahead, so the file can never go stale the
/// way a hand-written one does. Pure.
/// </summary>
public static class SecurityTxt
{
    /// <summary>How far ahead Expires is set each time the file is served. The RFC advises under a year.</summary>
    public const int ValidDays = 180;

    /// <summary>
    /// The file's text, or null when no usable contact is configured. Contacts are separated by commas;
    /// an email address becomes a <c>mailto:</c> link, and anything that is not an email address or an
    /// https address is dropped.
    /// </summary>
    public static string? Build(string? contacts, string? policyUrl, string? canonicalUrl, string? language, DateTime nowUtc)
    {
        var list = (contacts ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeContact)
            .Where(c => c is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (list.Count == 0) return null;

        var sb = new StringBuilder();
        foreach (var contact in list) sb.Append("Contact: ").Append(contact).Append('\n');
        sb.Append("Expires: ").Append(nowUtc.AddDays(ValidDays).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append('\n');

        var lang = (language ?? "").Trim();
        if (lang.Length is >= 2 and <= 12 && lang.All(c => char.IsAsciiLetter(c) || c == '-'))
            sb.Append("Preferred-Languages: ").Append(lang.ToLowerInvariant()).Append('\n');

        if (IsHttps(canonicalUrl)) sb.Append("Canonical: ").Append(canonicalUrl!.Trim()).Append('\n');
        if (IsHttps(policyUrl)) sb.Append("Policy: ").Append(policyUrl!.Trim()).Append('\n');
        return sb.ToString();
    }

    private static string? NormalizeContact(string raw)
    {
        if (raw.Any(char.IsControl) || raw.Any(char.IsWhiteSpace)) return null;
        if (raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return IsEmail(raw[7..]) ? "mailto:" + raw[7..] : null;
        if (IsHttps(raw)) return raw;
        return IsEmail(raw) ? "mailto:" + raw : null;
    }

    private static bool IsEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 3 && value.IndexOf('.', at) > at + 1 && !value.EndsWith('.');
    }

    private static bool IsHttps(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl)
        && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
