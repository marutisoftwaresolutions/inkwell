using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// Pure schedule arithmetic for <see cref="ScheduledScript"/>: when is the next run after a given
/// moment. Three shapes only, on purpose (hourly at a minute, daily at a UTC time, weekly on a day at
/// a UTC time): they cover database housekeeping without a cron grammar an operator has to learn.
/// All times are UTC (Architecture rule).
/// </summary>
public static class ScriptSchedule
{
    /// <summary>The first due moment strictly after <paramref name="afterUtc"/>.</summary>
    public static DateTime NextRun(ScheduledScript s, DateTime afterUtc)
    {
        var t = s.TimeOfDayUtc;
        switch (s.Frequency)
        {
            case ScriptFrequencies.Hourly:
            {
                var minute = Math.Clamp(t.Minutes, 0, 59);
                var candidate = new DateTime(afterUtc.Year, afterUtc.Month, afterUtc.Day, afterUtc.Hour, minute, 0, DateTimeKind.Utc);
                return candidate > afterUtc ? candidate : candidate.AddHours(1);
            }
            case ScriptFrequencies.Weekly:
            {
                var day = Math.Clamp(s.DayOfWeek ?? 0, 0, 6);
                var candidate = afterUtc.Date.Add(Clamp(t));
                var delta = (day - (int)candidate.DayOfWeek + 7) % 7;
                candidate = candidate.AddDays(delta);
                if (candidate <= afterUtc) candidate = candidate.AddDays(7);
                return DateTime.SpecifyKind(candidate, DateTimeKind.Utc);
            }
            default: // daily
            {
                var candidate = afterUtc.Date.Add(Clamp(t));
                if (candidate <= afterUtc) candidate = candidate.AddDays(1);
                return DateTime.SpecifyKind(candidate, DateTimeKind.Utc);
            }
        }
    }

    /// <summary>Human-readable form for the Desk: "Weekly, Sunday 03:00 UTC".</summary>
    public static string Describe(ScheduledScript s) => s.Frequency switch
    {
        ScriptFrequencies.Hourly => $"Hourly at :{Math.Clamp(s.TimeOfDayUtc.Minutes, 0, 59):00}",
        ScriptFrequencies.Weekly => $"Weekly, {(DayOfWeek)Math.Clamp(s.DayOfWeek ?? 0, 0, 6)} {Clamp(s.TimeOfDayUtc):hh\\:mm} UTC",
        _                        => $"Daily at {Clamp(s.TimeOfDayUtc):hh\\:mm} UTC",
    };

    /// <summary>Validation the Desk and the config importer share. Null = valid.</summary>
    public static string? Validate(string? name, string? file, string? frequency, int? dayOfWeek, TimeSpan time)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            return "Give the schedule a name of up to 100 characters.";
        if (string.IsNullOrWhiteSpace(file) || file.IndexOfAny(['/', '\\']) >= 0 || file.Contains("..") || !file.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            return "Choose a .sql file from the scripts folder (a file name, not a path).";
        if (!ScriptFrequencies.IsSupported(frequency))
            return "Frequency must be hourly, daily or weekly.";
        if (frequency == ScriptFrequencies.Weekly && (dayOfWeek is null || dayOfWeek < 0 || dayOfWeek > 6))
            return "A weekly schedule needs a day of the week.";
        if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
            return "Time must be between 00:00 and 23:59.";
        return null;
    }

    private static TimeSpan Clamp(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t >= TimeSpan.FromDays(1) ? new TimeSpan(23, 59, 0) : new TimeSpan(t.Hours, t.Minutes, 0);
}
