using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Blog.Core.Services;
using Blog.Infrastructure.Data;

namespace Blog.Web.Services.Backups;

/// <summary>Where backups live and where media is read from and restored to.</summary>
public sealed record BackupPaths(string Folder, string UploadsRoot, int Keep);

/// <summary>One backup file in the backups folder, with its manifest when it could be read.</summary>
public sealed record BackupFile(string Name, long SizeBytes, DateTime CreatedAtUtc, BackupManifest? Manifest, string? Problem);

/// <summary>A queued "make a backup" request, waiting for <see cref="Jobs.BackupJob"/>.</summary>
public sealed record BackupRequest(bool IncludeAnalytics, Guid RequestedBy, string RequestedByName, DateTime RequestedAtUtc);

public sealed record RestoreOutcome(BackupManifest Manifest, IReadOnlyDictionary<string, long> Rows, int MediaFiles, IReadOnlyList<string> Warnings);

/// <summary>
/// Whole-site backup and restore (ADR-0019). Backups are ZIPs in a folder outside the web root
/// (<c>Backups:Path</c>, default <c>App_Data/backups</c>); only names this service generates are ever
/// opened, so no request can reach another path. Creating one is queued (a marker file) and done by
/// <see cref="Jobs.BackupJob"/>, never inside a request. Restoring is allowed only into a fresh
/// install, from the setup wizard.
/// </summary>
public sealed class BackupService
{
    private const string RequestMarker = "backup.request";
    private const string PartialSuffix = ".partial";

    private readonly BackupDataStore _store;
    private readonly BackupPaths _paths;
    private readonly ILogger<BackupService> _log;

    public BackupService(BackupDataStore store, BackupPaths paths, ILogger<BackupService> log)
    {
        _store = store;
        _paths = paths;
        _log = log;
    }

    public BackupPaths Paths => _paths;

    public static string AppVersion => Blog.Web.Services.AppVersion.Current;

    private string PathOf(string name)
    {
        if (!BackupFormat.IsBackupFileName(name)) throw new ArgumentException("Not a backup file name.", nameof(name));
        return Path.Combine(_paths.Folder, name);
    }

    // ── Requests ────────────────────────────────────────────────────────────────

    /// <summary>Queues a backup. False when one is already queued or running.</summary>
    public bool Request(BackupRequest request)
    {
        Directory.CreateDirectory(_paths.Folder);
        if (IsRunning()) return false;
        try
        {
            using var fs = new FileStream(Path.Combine(_paths.Folder, RequestMarker), FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(fs, request);
            return true;
        }
        catch (IOException) { return false; }     // already queued
    }

    public BackupRequest? PendingRequest()
    {
        try
        {
            var path = Path.Combine(_paths.Folder, RequestMarker);
            return File.Exists(path) ? JsonSerializer.Deserialize<BackupRequest>(File.ReadAllText(path)) : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Takes the queued request, so only one job run acts on it. Null when nothing is queued.</summary>
    public BackupRequest? TakeRequest()
    {
        var path = Path.Combine(_paths.Folder, RequestMarker);
        var taken = path + ".taken";
        try
        {
            if (!File.Exists(path)) return null;
            File.Move(path, taken, overwrite: true);
            var request = JsonSerializer.Deserialize<BackupRequest>(File.ReadAllText(taken));
            File.Delete(taken);
            return request;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Backup request could not be read; discarded.");
            try { File.Delete(taken); } catch { /* best effort */ }
            return null;
        }
    }

    /// <summary>A partial file untouched this long was left by a crash or a recycle, not a running backup.</summary>
    public static readonly TimeSpan StalePartialAge = TimeSpan.FromMinutes(30);

    public bool IsRunning() =>
        Directory.Exists(_paths.Folder) &&
        Directory.EnumerateFiles(_paths.Folder, "*" + PartialSuffix)
                 .Any(p => DateTime.UtcNow - File.GetLastWriteTimeUtc(p) < StalePartialAge);

    /// <summary>Removes partial files left by an interrupted backup. Returns how many.</summary>
    public int RemoveStalePartials()
    {
        if (!Directory.Exists(_paths.Folder)) return 0;
        var removed = 0;
        foreach (var p in Directory.EnumerateFiles(_paths.Folder, "*" + PartialSuffix))
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(p) < StalePartialAge) continue;
            try { File.Delete(p); removed++; } catch { /* in use: not stale after all */ }
        }
        return removed;
    }

    // ── Creating ────────────────────────────────────────────────────────────────

    /// <summary>Writes a new backup and returns its name and manifest. The file appears only once complete.</summary>
    public async Task<(string Name, BackupManifest Manifest)> CreateAsync(bool includeAnalytics, CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.Folder);
        var now = DateTime.UtcNow;
        var name = BackupFormat.FileNameFor(now);
        var final = PathOf(name);
        var partial = final + PartialSuffix;

        var manifest = new BackupManifest { AppVersion = AppVersion, CreatedAtUtc = now, IncludesAnalytics = includeAnalytics };
        try
        {
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var existing = new HashSet<string>(await _store.ExistingTablesAsync(), StringComparer.OrdinalIgnoreCase);
                foreach (var table in BackupFormat.TablesToBackUp(includeAnalytics).Where(existing.Contains))
                {
                    var entry = zip.CreateEntry(BackupFormat.DataEntry(table), CompressionLevel.Optimal);
                    await using var es = entry.Open();
                    manifest.Tables[table] = await _store.WriteTableAsync(table, es, ct);
                }

                if (Directory.Exists(_paths.UploadsRoot))
                {
                    foreach (var path in Directory.EnumerateFiles(_paths.UploadsRoot, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        var rel = Path.GetRelativePath(_paths.UploadsRoot, path).Replace('\\', '/');
                        // Images are already compressed; storing them avoids burning CPU for nothing.
                        var entry = zip.CreateEntry(BackupFormat.MediaPrefix + rel, CompressionLevel.NoCompression);
                        entry.LastWriteTime = File.GetLastWriteTimeUtc(path);
                        await using var src = File.OpenRead(path);
                        await using var dst = entry.Open();
                        await src.CopyToAsync(dst, ct);
                        manifest.MediaFiles++;
                        manifest.MediaBytes += src.Length;
                    }
                }

                var m = zip.CreateEntry(BackupFormat.ManifestEntry, CompressionLevel.Optimal);
                await using (var ms = m.Open())
                    await JsonSerializer.SerializeAsync(ms, manifest, BackupManifest.Json, ct);
            }
            File.Move(partial, final);
            return (name, manifest);
        }
        catch
        {
            try { File.Delete(partial); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>Deletes the backups beyond the newest <see cref="BackupPaths.Keep"/>. Returns the names removed.</summary>
    public IReadOnlyList<string> Prune()
    {
        if (!Directory.Exists(_paths.Folder)) return Array.Empty<string>();
        var names = Directory.EnumerateFiles(_paths.Folder, "*.zip").Select(Path.GetFileName).OfType<string>();
        var remove = BackupFormat.BeyondRetention(names, _paths.Keep);
        foreach (var n in remove)
            try { File.Delete(PathOf(n)); } catch (Exception ex) { _log.LogWarning(ex, "Could not delete old backup {Name}.", n); }
        return remove;
    }

    // ── Listing, download, delete ───────────────────────────────────────────────

    public IReadOnlyList<BackupFile> List()
    {
        if (!Directory.Exists(_paths.Folder)) return Array.Empty<BackupFile>();
        return Directory.EnumerateFiles(_paths.Folder, "*.zip")
            .Select(Path.GetFileName).OfType<string>()
            .Where(BackupFormat.IsBackupFileName)
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .Select(Describe)
            .ToList();
    }

    public BackupFile? Find(string name) =>
        BackupFormat.IsBackupFileName(name) && File.Exists(PathOf(name)) ? Describe(name) : null;

    private BackupFile Describe(string name)
    {
        var info = new FileInfo(PathOf(name));
        BackupManifest? manifest = null;
        string? problem;
        try
        {
            using var zip = ZipFile.OpenRead(info.FullName);
            manifest = ReadManifest(zip);
            problem = manifest is null ? "The file has no manifest; it is not an Inkwell backup." : manifest.RestoreProblem();
        }
        catch (Exception) { problem = "The file could not be opened as a ZIP archive."; }
        return new BackupFile(name, info.Length, manifest?.CreatedAtUtc ?? info.LastWriteTimeUtc, manifest, problem);
    }

    private static BackupManifest? ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry(BackupFormat.ManifestEntry);
        if (entry is null) return null;
        using var s = entry.Open();
        return JsonSerializer.Deserialize<BackupManifest>(s);
    }

    public Stream OpenRead(string name) => File.OpenRead(PathOf(name));

    public bool Delete(string name)
    {
        var path = PathOf(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    // ── Restoring ───────────────────────────────────────────────────────────────

    public Task<bool> IsEmptyInstallAsync() => _store.IsEmptyInstallAsync();

    /// <summary>
    /// Restores <paramref name="name"/> into this (empty) install: the database in one transaction, then
    /// the media files. Throws <see cref="InvalidOperationException"/> with an operator-readable message
    /// when the backup cannot be restored; the database is untouched in that case.
    /// </summary>
    public async Task<RestoreOutcome> RestoreAsync(string name, CancellationToken ct)
    {
        if (!BackupFormat.IsBackupFileName(name) || !File.Exists(PathOf(name)))
            throw new InvalidOperationException("That backup file is not in the backups folder.");

        using var zip = ZipFile.OpenRead(PathOf(name));
        var manifest = ReadManifest(zip)
            ?? throw new InvalidOperationException("The file has no manifest; it is not an Inkwell backup.");
        var problem = manifest.RestoreProblem();
        if (problem is not null) throw new InvalidOperationException(problem);

        var load = await _store.RestoreAsync(table =>
            manifest.Tables.ContainsKey(table) ? zip.GetEntry(BackupFormat.DataEntry(table))?.Open() : null, ct);

        // Media after the database has committed: a file that fails to write is reported, not fatal,
        // because the site renders without it and the operator can copy it by hand.
        var warnings = load.Warnings.ToList();
        var mediaFiles = 0;
        var skipped = 0;
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(BackupFormat.MediaPrefix, StringComparison.Ordinal)) continue;
            var target = BackupFormat.MediaTarget(_paths.UploadsRoot, entry.FullName);
            if (target is null) { if (!entry.FullName.EndsWith('/')) skipped++; continue; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                mediaFiles++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Restore could not write media file {Entry}.", entry.FullName);
                skipped++;
            }
        }
        if (skipped > 0)
            warnings.Add($"{skipped} media file(s) could not be restored. Copy the uploads folder from the backup by hand if images are missing.");

        return new RestoreOutcome(manifest, load.Rows, mediaFiles, warnings);
    }
}
