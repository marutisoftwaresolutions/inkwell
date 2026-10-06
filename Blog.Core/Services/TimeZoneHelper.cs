namespace Blog.Core.Services;

/// <summary>
/// Storage is UTC everywhere; display is the tenant's zone. One helper so every screen converts the
/// same way, and an unknown or empty zone id shows UTC rather than throwing.
/// </summary>
public static class TimeZoneHelper
{
    public const string Utc = "UTC";

    /// <summary>
    /// IANA zones renamed in recent tzdata releases, with the name they had before (tzdata "backward" links).
    /// A host whose time-zone data predates a rename knows only the old name; the rules are the same.
    /// </summary>
    private static readonly Dictionary<string, string> RenamedFrom = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Europe/Kyiv"] = "Europe/Kiev",
        ["Pacific/Kanton"] = "Pacific/Enderbury",
        ["Asia/Kolkata"] = "Asia/Calcutta",
        ["Asia/Yangon"] = "Asia/Rangoon",
        ["Asia/Ho_Chi_Minh"] = "Asia/Saigon",
        ["Asia/Kathmandu"] = "Asia/Katmandu",
        ["America/Nuuk"] = "America/Godthab",
        ["Atlantic/Faroe"] = "Atlantic/Faeroe",
        ["Pacific/Chuuk"] = "Pacific/Truk",
        ["Pacific/Pohnpei"] = "Pacific/Ponape",
        ["Asia/Urumqi"] = "Asia/Kashgar",
    };

    public static TimeZoneInfo Resolve(string? timeZoneId) => TryFind(timeZoneId) ?? TimeZoneInfo.Utc;

    /// <summary>True when the id names a zone this host knows, so Settings can refuse a typo.</summary>
    public static bool IsKnown(string? timeZoneId) => TryFind(timeZoneId) is not null;

    private static TimeZoneInfo? TryFind(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Equals(Utc, StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Utc;
        var id = timeZoneId.Trim();
        foreach (var candidate in RenamedFrom.TryGetValue(id, out var old) ? new[] { id, old } : new[] { id })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return null;
    }

    /// <summary>
    /// The IANA id for a stored zone id: settings saved before ADR-0027 may hold a Windows id
    /// ("India Standard Time"), which becomes "Asia/Kolkata". Unknown or IANA ids come back unchanged.
    /// </summary>
    public static string ToIana(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Equals(Utc, StringComparison.OrdinalIgnoreCase)) return Utc;
        var id = timeZoneId.Trim();
        if (id.Contains('/') || !TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana)) return id;
        // ICU can answer with a pre-rename name ("Asia/Calcutta"); store the current one.
        return RenamedFrom.FirstOrDefault(kv => string.Equals(kv.Value, iana, StringComparison.OrdinalIgnoreCase)).Key ?? iana;
    }

    /// <summary>A UTC instant in the tenant's zone. Unspecified kinds are treated as UTC, which is what storage holds.</summary>
    public static DateTime ToDisplay(DateTime utc, string? timeZoneId)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, Resolve(timeZoneId));
    }

    public static DateTime? ToDisplay(DateTime? utc, string? timeZoneId) =>
        utc is null ? null : ToDisplay(utc.Value, timeZoneId);

    /// <summary>A wall-clock time the operator typed in their zone, as the UTC instant storage wants.</summary>
    public static DateTime FromDisplay(DateTime local, string? timeZoneId)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Resolve(timeZoneId));
    }
}
