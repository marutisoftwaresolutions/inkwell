using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Blog.Core.Services;

/// <summary>What a whole-site backup does with each table.</summary>
public enum BackupTableRole
{
    /// <summary>Always in the backup: the site cannot be rebuilt without it.</summary>
    Content,
    /// <summary>In the backup only when the operator asks for it (large, and the site works without it).</summary>
    Optional,
    /// <summary>Never in the backup: derived, transient, re-fetchable or secret-bearing.</summary>
    Excluded,
}

public sealed record BackupTable(string Name, BackupTableRole Role, string Reason);

/// <summary>
/// The Inkwell backup format (ADR-0019). A backup is a ZIP holding <c>manifest.json</c>, one
/// <c>data/{Table}.jsonl</c> per table (one JSON object per row, column name → value) and the
/// uploads folder under <c>media/uploads/</c>. Every table in the schema is classified here; a test
/// fails when a table is added without a decision, so a new table can never silently be left out of
/// backups.
/// </summary>
public static class BackupFormat
{
    public const string FormatName    = "inkwell-backup";
    public const int    FormatVersion = 1;
    public const string ManifestEntry = "manifest.json";
    public const string DataPrefix    = "data/";
    public const string MediaPrefix   = "media/uploads/";

    public static readonly IReadOnlyList<BackupTable> Tables = new[]
    {
        // People and access
        new BackupTable("Users",            BackupTableRole.Content, "accounts, including password hashes"),
        new BackupTable("Roles",            BackupTableRole.Content, "role definitions"),
        new BackupTable("Permissions",      BackupTableRole.Content, "permission definitions"),
        new BackupTable("PermissionsRoles", BackupTableRole.Content, "role grants"),
        new BackupTable("RolesUsers",       BackupTableRole.Content, "role membership"),
        // Site configuration
        new BackupTable("Settings",            BackupTableRole.Content, "site settings; Data Protection–encrypted values inside need re-entry"),
        new BackupTable("CustomThemeSettings", BackupTableRole.Content, "theme choices"),
        new BackupTable("MenuItems",           BackupTableRole.Content, "header and footer menus"),
        new BackupTable("Redirects",           BackupTableRole.Content, "redirects"),
        new BackupTable("Snippets",            BackupTableRole.Content, "reusable snippets"),
        new BackupTable("Webhooks",            BackupTableRole.Content, "webhook endpoints; signing secrets need re-entry"),
        new BackupTable("IpFirewallRules",     BackupTableRole.Content, "manual blocks and allows"),
        // Content
        new BackupTable("Posts",          BackupTableRole.Content, "posts"),
        new BackupTable("Pages",          BackupTableRole.Content, "pages"),
        new BackupTable("Categories",     BackupTableRole.Content, "categories"),
        new BackupTable("Tags",           BackupTableRole.Content, "tags"),
        new BackupTable("PostCategories", BackupTableRole.Content, "post ↔ category links"),
        new BackupTable("PostTags",       BackupTableRole.Content, "post ↔ tag links"),
        new BackupTable("Series",         BackupTableRole.Content, "series"),
        new BackupTable("SeriesPosts",    BackupTableRole.Content, "series order"),
        new BackupTable("Media",          BackupTableRole.Content, "media library rows (files travel under media/)"),
        new BackupTable("Revisions",      BackupTableRole.Content, "revision history"),
        new BackupTable("Comments",       BackupTableRole.Content, "comments"),
        // Audience
        new BackupTable("Members",           BackupTableRole.Content, "subscribers"),
        new BackupTable("Labels",            BackupTableRole.Content, "subscriber labels"),
        new BackupTable("MemberLabels",      BackupTableRole.Content, "subscriber ↔ label links"),
        new BackupTable("Newsletters",       BackupTableRole.Content, "newsletters"),
        new BackupTable("MemberNewsletters", BackupTableRole.Content, "subscriptions"),
        new BackupTable("Emails",            BackupTableRole.Content, "unused legacy issue log"),
        new BackupTable("NewsletterIssues",  BackupTableRole.Content, "newsletter issues, drafts and what was sent (ADR-0028)"),
        // Record
        new BackupTable("AuditLogs", BackupTableRole.Content, "the audit trail is a record, so it moves with the site"),
        // Analytics
        new BackupTable("PageViews", BackupTableRole.Optional, "reader analytics; can run to millions of rows"),
        // Never
        new BackupTable("CrawlerVisits",       BackupTableRole.Excluded, "bot traffic log, pruned by retention"),
        new BackupTable("ErrorLogs",           BackupTableRole.Excluded, "diagnostics for this server"),
        new BackupTable("Jobs",                BackupTableRole.Excluded, "scheduler ledger for this server"),
        new BackupTable("PasswordResetTokens", BackupTableRole.Excluded, "short-lived secrets"),
        new BackupTable("SearchPerformance",   BackupTableRole.Excluded, "re-fetched from Search Console"),
        new BackupTable("WebhookDeliveries",   BackupTableRole.Excluded, "delivery log; replaying it would re-send events"),
        new BackupTable("ImportJobs",          BackupTableRole.Excluded, "import staging"),
        new BackupTable("ImportItems",         BackupTableRole.Excluded, "import staging"),
        new BackupTable("NewsletterDeliveries", BackupTableRole.Excluded, "send log; replaying it would re-send newsletters"),
        new BackupTable("Countries",           BackupTableRole.Excluded, "reference catalog every install seeds (IANA/CLDR)"),
        new BackupTable("TimeZones",           BackupTableRole.Excluded, "reference catalog every install seeds (IANA/CLDR)"),
        new BackupTable("DateLocales",         BackupTableRole.Excluded, "reference catalog every install seeds (IANA/CLDR)"),
    };

    private static readonly Dictionary<string, BackupTable> ByName =
        Tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    public static BackupTable? Find(string table) => ByName.GetValueOrDefault(table);

    /// <summary>The tables a backup writes, in catalog order.</summary>
    public static IReadOnlyList<string> TablesToBackUp(bool includeAnalytics) =>
        Tables.Where(t => t.Role == BackupTableRole.Content || (includeAnalytics && t.Role == BackupTableRole.Optional))
              .Select(t => t.Name).ToList();

    /// <summary>The tables a restore empties first: everything a backup can carry, whether or not this one does.</summary>
    public static IReadOnlyList<string> TablesToClearOnRestore() =>
        Tables.Where(t => t.Role != BackupTableRole.Excluded).Select(t => t.Name).ToList();

    public static string DataEntry(string table) => DataPrefix + table + ".jsonl";

    // ── File names ──────────────────────────────────────────────────────────────

    private static readonly Regex FileNamePattern =
        new(@"^inkwell-backup-\d{8}-\d{6}\.zip$", RegexOptions.CultureInvariant);

    /// <summary><c>inkwell-backup-20260927-143000.zip</c>, from a UTC instant. Sorts by time.</summary>
    public static string FileNameFor(DateTime utc) =>
        "inkwell-backup-" + utc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip";

    /// <summary>
    /// True only for a name this feature generates. Download, delete and restore accept nothing else,
    /// so no request can reach a path outside the backups folder.
    /// </summary>
    public static bool IsBackupFileName(string? name) => name is not null && FileNamePattern.IsMatch(name);

    /// <summary>The backups beyond the newest <paramref name="keep"/>, which retention deletes.</summary>
    public static IReadOnlyList<string> BeyondRetention(IEnumerable<string> names, int keep) =>
        names.Where(IsBackupFileName).OrderByDescending(n => n, StringComparer.Ordinal)
             .Skip(Math.Max(1, keep)).ToList();

    // ── Media paths ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a ZIP entry under <see cref="MediaPrefix"/> to a file under <paramref name="uploadsRoot"/>,
    /// or null when the entry is not media or would land outside the root (zip-slip: <c>../</c>,
    /// absolute paths, drive letters).
    /// </summary>
    public static string? MediaTarget(string uploadsRoot, string entryName)
    {
        if (string.IsNullOrEmpty(entryName)) return null;
        var normalized = entryName.Replace('\\', '/');
        if (!normalized.StartsWith(MediaPrefix, StringComparison.Ordinal)) return null;
        var relative = normalized[MediaPrefix.Length..];
        if (relative.Length == 0 || relative.EndsWith('/')) return null;         // a directory entry
        if (relative.Contains(':') || relative.StartsWith('/')) return null;

        var root = Path.GetFullPath(uploadsRoot);
        if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? target : null;
    }

    // ── Values ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes one column value so that <see cref="ReadValue"/> gives back exactly what was read from
    /// the database: GUIDs and dates as invariant strings (dates keep their full precision and no
    /// time-zone suffix is invented), numbers as JSON numbers, bytes as base64.
    /// </summary>
    public static void WriteValue(Utf8JsonWriter w, object? value)
    {
        switch (value)
        {
            case null or DBNull:     w.WriteNullValue(); break;
            case string s:           w.WriteStringValue(s); break;
            case Guid g:             w.WriteStringValue(g.ToString("D")); break;
            case bool b:             w.WriteBooleanValue(b); break;
            case int i:              w.WriteNumberValue(i); break;
            case long l:             w.WriteNumberValue(l); break;
            case short sh:           w.WriteNumberValue(sh); break;
            case byte by:            w.WriteNumberValue(by); break;
            case decimal m:          w.WriteNumberValue(m); break;
            case double d:           w.WriteNumberValue(d); break;
            case float f:            w.WriteNumberValue(f); break;
            case DateTime dt:        w.WriteStringValue(dt.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture)); break;
            case DateTimeOffset dto: w.WriteStringValue(dto.ToString("O", CultureInfo.InvariantCulture)); break;
            case TimeSpan ts:        w.WriteStringValue(ts.ToString("c", CultureInfo.InvariantCulture)); break;
            case byte[] bytes:       w.WriteBase64StringValue(bytes); break;
            default: throw new NotSupportedException($"Backup cannot encode a value of type {value.GetType().Name}.");
        }
    }

    /// <summary>Reads a value written by <see cref="WriteValue"/> as the CLR type of the destination column.</summary>
    public static object ReadValue(JsonElement e, Type target)
    {
        if (e.ValueKind == JsonValueKind.Null) return DBNull.Value;
        if (target == typeof(string))         return e.GetString()!;
        if (target == typeof(Guid))           return Guid.Parse(e.GetString()!);
        if (target == typeof(bool))           return e.GetBoolean();
        if (target == typeof(int))            return e.GetInt32();
        if (target == typeof(long))           return e.GetInt64();
        if (target == typeof(short))          return e.GetInt16();
        if (target == typeof(byte))           return e.GetByte();
        if (target == typeof(decimal))        return e.GetDecimal();
        if (target == typeof(double))         return e.GetDouble();
        if (target == typeof(float))          return e.GetSingle();
        if (target == typeof(DateTime))       return DateTime.Parse(e.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.None);
        if (target == typeof(DateTimeOffset)) return DateTimeOffset.Parse(e.GetString()!, CultureInfo.InvariantCulture);
        if (target == typeof(TimeSpan))       return TimeSpan.ParseExact(e.GetString()!, "c", CultureInfo.InvariantCulture);
        if (target == typeof(byte[]))         return e.GetBytesFromBase64();
        throw new NotSupportedException($"Backup cannot restore a column of type {target.Name}.");
    }
}

/// <summary>The <c>manifest.json</c> at the root of every backup.</summary>
public sealed class BackupManifest
{
    public string Format { get; set; } = BackupFormat.FormatName;
    public int FormatVersion { get; set; } = BackupFormat.FormatVersion;
    public string AppVersion { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public bool IncludesAnalytics { get; set; }
    /// <summary>Row count per table written.</summary>
    public Dictionary<string, long> Tables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int MediaFiles { get; set; }
    public long MediaBytes { get; set; }

    public long RowsIn(string table) => Tables.TryGetValue(table, out var n) ? n : 0;

    /// <summary>Why this manifest cannot be restored by this version, or null when it can.</summary>
    public string? RestoreProblem()
    {
        if (!string.Equals(Format, BackupFormat.FormatName, StringComparison.Ordinal))
            return "This file is not an Inkwell backup.";
        if (FormatVersion > BackupFormat.FormatVersion)
            return $"This backup was made by a newer Inkwell ({AppVersion}). Upgrade this install first, then restore.";
        if (FormatVersion < 1)
            return "This backup's format version is not recognised.";
        var unknown = Tables.Keys.FirstOrDefault(t => BackupFormat.Find(t) is not { Role: not BackupTableRole.Excluded });
        if (unknown is not null)
            return $"This backup contains a table this version does not restore ({unknown}).";
        if (RowsIn("Users") == 0 || RowsIn("Settings") == 0)
            return "This backup has no users or no settings, so the restored site could not be signed in to.";
        return null;
    }

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
}
