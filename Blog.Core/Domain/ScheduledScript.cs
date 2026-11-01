namespace Blog.Core.Domain;

/// <summary>
/// A SQL script file the operator placed on the server, run by the in-process scheduler on a simple
/// recurring schedule (Admin → Scheduled scripts). Scripts come only from the configured folder
/// (<c>ScheduledScripts:Folder</c>, default <c>App_Data/jobs</c>), which the Desk cannot write to, so a
/// Desk Admin can choose what runs and when but never type SQL. Install-wide, not per tenant: it is
/// housekeeping for the database, like backups.
/// </summary>
public class ScheduledScript
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Unique, operator-facing name; the key config-defined entries are matched on.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>File name inside the scripts folder (no path separators).</summary>
    public string ScriptFile { get; set; } = string.Empty;

    /// <summary><see cref="ScriptFrequencies"/>.</summary>
    public string Frequency { get; set; } = ScriptFrequencies.Daily;

    /// <summary>0 = Sunday … 6 = Saturday; only for weekly.</summary>
    public int? DayOfWeek { get; set; }

    /// <summary>Time of day in UTC for daily/weekly; for hourly only the minute component is used.</summary>
    public TimeSpan TimeOfDayUtc { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary><see cref="ScriptSources"/>: config-defined rows are read-only in the Desk.</summary>
    public string Source { get; set; } = ScriptSources.Desk;

    public DateTime? NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public bool? LastSucceeded { get; set; }
    public string? LastOutput { get; set; }
    public int? LastDurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsFromConfig => Source == ScriptSources.Config;
}

/// <summary>Stored values; never rename one.</summary>
public static class ScriptFrequencies
{
    public const string Hourly = "hourly";
    public const string Daily  = "daily";
    public const string Weekly = "weekly";
    public static readonly string[] All = [Hourly, Daily, Weekly];
    public static bool IsSupported(string? value) => value is not null && Array.IndexOf(All, value) >= 0;
}

/// <summary>Where a schedule row came from. Config rows are owned by appsettings and cannot be edited or deleted in the Desk.</summary>
public static class ScriptSources
{
    public const string Desk   = "desk";
    public const string Config = "config";
}
