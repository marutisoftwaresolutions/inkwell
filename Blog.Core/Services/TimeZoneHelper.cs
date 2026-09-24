namespace Blog.Core.Services;

/// <summary>
/// Storage is UTC everywhere; display is the tenant's zone. One helper so every screen converts the
/// same way, and an unknown or empty zone id shows UTC rather than throwing.
/// </summary>
public static class TimeZoneHelper
{
    public const string Utc = "UTC";

    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Equals(Utc, StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim()); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    /// <summary>True when the id names a zone this host knows, so Settings can refuse a typo.</summary>
    public static bool IsKnown(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Equals(Utc, StringComparison.OrdinalIgnoreCase)) return true;
        try { TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim()); return true; }
        catch { return false; }
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
