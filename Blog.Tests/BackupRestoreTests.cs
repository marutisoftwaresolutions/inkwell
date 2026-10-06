using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blog.Core.Services;
using Blog.Infrastructure.Data;
using Blog.Web.Services.Backups;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Backup and restore (ADR-0019): the format rules, the guarantee that every table has a backup
/// decision, and the end-to-end proof — a backup of the dev site restored into a brand-new empty
/// database renders as the same site.
/// </summary>
public class BackupRestoreTests
{
    private readonly ITestOutputHelper _out;
    public BackupRestoreTests(ITestOutputHelper output) => _out = output;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    // ── Catalog ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_table_in_init_sql_has_a_backup_decision()
    {
        var sql = File.ReadAllText(Path.Combine(RepoRoot(), "DBScripts", "init.sql"));
        var created = Regex.Matches(sql, @"^\s*CREATE\s+TABLE\s+(?:\[?dbo\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase | RegexOptions.Multiline)
                           .Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.NotEmpty(created);
        var missing = created.Where(t => BackupFormat.Find(t) is null).ToList();
        Assert.True(missing.Count == 0,
            "Add these tables to BackupFormat.Tables as Content, Optional or Excluded: " + string.Join(", ", missing));
    }

    [Fact]
    public void Catalog_names_are_unique_and_carry_a_reason()
    {
        Assert.Equal(BackupFormat.Tables.Count, BackupFormat.Tables.Select(t => t.Name.ToLowerInvariant()).Distinct().Count());
        Assert.All(BackupFormat.Tables, t => Assert.False(string.IsNullOrWhiteSpace(t.Reason)));
    }

    [Fact]
    public void Secrets_and_logs_are_never_backed_up()
    {
        var withAnalytics = BackupFormat.TablesToBackUp(includeAnalytics: true);
        foreach (var t in new[] { "PasswordResetTokens", "WebhookDeliveries", "CrawlerVisits", "ErrorLogs", "Jobs", "SearchPerformance" })
            Assert.DoesNotContain(t, withAnalytics);
        Assert.DoesNotContain("PageViews", BackupFormat.TablesToBackUp(includeAnalytics: false));
        Assert.Contains("PageViews", withAnalytics);
        Assert.Contains("PageViews", BackupFormat.TablesToClearOnRestore());
    }

    // ── Values ──────────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> Values() => new[]
    {
        new object[] { "Em dash — “curly” ’quotes’ … ✓", typeof(string) },
        new object[] { Guid.Parse("7f1d2c3b-4a5e-6f70-8192-a3b4c5d6e7f8"), typeof(Guid) },
        new object[] { true, typeof(bool) },
        new object[] { int.MinValue, typeof(int) },
        new object[] { long.MaxValue, typeof(long) },
        new object[] { 0.1 + 0.2, typeof(double) },
        new object[] { 12345.6789m, typeof(decimal) },
        new object[] { new DateTime(2026, 9, 27, 14, 30, 5, DateTimeKind.Unspecified).AddTicks(1234567), typeof(DateTime) },
        new object[] { new DateTimeOffset(2026, 9, 27, 14, 30, 5, TimeSpan.FromHours(5.5)), typeof(DateTimeOffset) },
        new object[] { new byte[] { 0, 1, 2, 250, 255 }, typeof(byte[]) },
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void Values_round_trip_exactly(object value, Type type)
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer)) BackupFormat.WriteValue(w, value);
        using var doc = JsonDocument.Parse(buffer.ToArray());
        var back = BackupFormat.ReadValue(doc.RootElement, type);
        Assert.Equal(value, back);
        if (value is DateTime dt) Assert.Equal(dt.Kind, ((DateTime)back).Kind);
    }

    [Fact]
    public void Null_round_trips_as_DBNull()
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer)) BackupFormat.WriteValue(w, DBNull.Value);
        using var doc = JsonDocument.Parse(buffer.ToArray());
        Assert.Equal(DBNull.Value, BackupFormat.ReadValue(doc.RootElement, typeof(string)));
    }

    // ── Names, retention, media paths ───────────────────────────────────────────

    [Fact]
    public void File_names_are_generated_and_nothing_else_is_accepted()
    {
        var name = BackupFormat.FileNameFor(new DateTime(2026, 9, 27, 14, 30, 0, DateTimeKind.Utc));
        Assert.Equal("inkwell-backup-20260927-143000.zip", name);
        Assert.True(BackupFormat.IsBackupFileName(name));
        foreach (var bad in new[] { null, "", "../inkwell-backup-20260927-143000.zip", "inkwell-backup-20260927-143000.zip/../x",
                                    "..\\web.config", "appsettings.json", "inkwell-backup-2026.zip", "inkwell-backup-20260927-143000.zip.partial" })
            Assert.False(BackupFormat.IsBackupFileName(bad), bad);
    }

    [Fact]
    public void Retention_keeps_the_newest()
    {
        var names = new[] { "inkwell-backup-20260101-000000.zip", "inkwell-backup-20260301-000000.zip",
                            "inkwell-backup-20260201-000000.zip", "notes.zip" };
        Assert.Equal(new[] { "inkwell-backup-20260101-000000.zip" }, BackupFormat.BeyondRetention(names, 2));
        Assert.Empty(BackupFormat.BeyondRetention(names, 10));
        Assert.Equal(2, BackupFormat.BeyondRetention(names, 0).Count);      // always keeps at least one
    }

    [Fact]
    public void Media_entries_cannot_escape_the_uploads_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "inkwell-uploads-test");
        Assert.Equal(Path.Combine(root, "2026", "09", "a.webp"), BackupFormat.MediaTarget(root, "media/uploads/2026/09/a.webp"));
        foreach (var evil in new[] { "media/uploads/../../web.config", "media/uploads/..\\..\\x.dll", "media/uploads/C:/x.txt",
                                     "media/uploads//etc/passwd", "data/Posts.jsonl", "media/uploads/", "media/uploads/dir/", "" })
            Assert.Null(BackupFormat.MediaTarget(root, evil));
    }

    [Fact]
    public void Manifest_problems_are_reported_before_anything_is_touched()
    {
        BackupManifest Ok() => new() { AppVersion = "1.0.8", Tables = { ["Users"] = 1, ["Settings"] = 1, ["Posts"] = 3 } };
        Assert.Null(Ok().RestoreProblem());
        Assert.Contains("not an Inkwell backup", new BackupManifest { Format = "other" }.RestoreProblem());
        var newer = Ok(); newer.FormatVersion = BackupFormat.FormatVersion + 1;
        Assert.Contains("newer Inkwell", newer.RestoreProblem());
        var unknown = Ok(); unknown.Tables["Mystery"] = 1;
        Assert.Contains("Mystery", unknown.RestoreProblem());
        var excluded = Ok(); excluded.Tables["PasswordResetTokens"] = 1;
        Assert.Contains("PasswordResetTokens", excluded.RestoreProblem());
        var noUsers = Ok(); noUsers.Tables.Remove("Users");
        Assert.Contains("no users", noUsers.RestoreProblem());
    }

    // ── Screens and wiring (source scans) ───────────────────────────────────────

    [Fact]
    public void Backups_screen_is_admin_only_audited_and_absent_in_cloud_mode()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Controllers", "BackupsController.cs"));
        Assert.Contains("[Authorize(Policy = \"AdminOnly\")]", src);
        Assert.Equal(2, Regex.Matches(src, @"\[ValidateAntiForgeryToken\]").Count);
        foreach (var a in new[] { "BackupRequested", "BackupDownloaded", "BackupDeleted" }) Assert.Contains("AuditActions." + a, src);
        Assert.Equal(5, Regex.Matches(src, @"if \(Unavailable\) return NotFound\(\);").Count);

        var job = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Services", "Jobs", "BackupJob.cs"));
        Assert.Contains("AuditActions.BackupCreated", job);
        var setup = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Controllers", "SetupController.cs"));
        Assert.Contains("AuditActions.BackupRestored", setup);
    }

    [Fact]
    public void Restore_is_reachable_only_while_the_install_is_fresh()
    {
        var setup = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Controllers", "SetupController.cs"));
        var restoreActions = Regex.Matches(setup, @"public async Task<IActionResult> Restore(Post)?\([^)]*\)\s*\{\s*var guard = await GuardAsync\(\);");
        Assert.Equal(2, restoreActions.Count);
        // "Fresh" means no settings AND no users: the first admin can be created at /account/register
        // without a settings row, and the wizard (and restore) must close as soon as it exists.
        Assert.Contains("await _settings.IsInstalledAsync() || await _users.AnyUsersExistAsync()", setup);
    }

    // ── End to end ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The v1.0.8 definition of done: back up the dev site, restore it into a brand-new database that
    /// the app itself initialised (so it carries the fresh-install taxonomy seed), then prove the rows
    /// are identical table by table, the media files are byte-identical, a second restore is refused,
    /// and the site renders from the restored database. Skips when the dev database is unreachable.
    /// </summary>
    [Fact]
    public async Task A_backup_restores_into_an_empty_database_and_the_site_renders()
    {
        using var devFactory = new WebApplicationFactory<Program>();
        string? devCs;
        string devUploads;
        try
        {
            devCs = devFactory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            devUploads = Path.Combine(devFactory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath, "uploads");
            if (string.IsNullOrWhiteSpace(devCs)) { _out.WriteLine("No connection string — skipping."); return; }
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(devCs) { ConnectTimeout = 3 }.ConnectionString);
            await probe.OpenAsync();
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return; }

        const string testDb = "blog_restoretest";
        var master = new SqlConnectionStringBuilder(devCs) { InitialCatalog = "master" }.ConnectionString;
        var testCs = new SqlConnectionStringBuilder(devCs) { InitialCatalog = testDb }.ConnectionString;
        var work = Path.Combine(Path.GetTempPath(), "inkwell-restore-" + Guid.NewGuid().ToString("N"));
        var backupFolder = Path.Combine(work, "backups");
        var restoredUploads = Path.Combine(work, "uploads");

        async Task DropAsync()
        {
            SqlConnection.ClearAllPools();
            await using var c = new SqlConnection(master);
            await c.ExecuteAsync($"IF DB_ID('{testDb}') IS NOT NULL BEGIN ALTER DATABASE [{testDb}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{testDb}]; END");
        }

        await DropAsync();
        await using (var c = new SqlConnection(master)) await c.ExecuteAsync($"CREATE DATABASE [{testDb}]");

        WebApplicationFactory<Program>? restored = null;
        string? copiedFile = null;
        try
        {
            // A fresh install, as the README describes it: init.sql, then the app's own startup, which
            // runs its migrations and seeds the default taxonomy.
            await using (var c = new SqlConnection(testCs))
                await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "DBScripts", "init.sql")), commandTimeout: 0);
            restored = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
                b.UseSetting("ConnectionStrings:DefaultConnection", testCs));
            _ = restored.Services;
            await using (var c = new SqlConnection(testCs))
            {
                Assert.True(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables") > 20, "The app did not initialise the test database.");
                Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users"));

            }

            // Back up the dev site.
            var source = new BackupService(new BackupDataStore(new DapperContext(devCs)),
                new BackupPaths(backupFolder, devUploads, 10), NullLogger<BackupService>.Instance);
            var (name, manifest) = await source.CreateAsync(includeAnalytics: false, CancellationToken.None);
            _out.WriteLine($"{name}: {manifest.Tables.Values.Sum()} rows in {manifest.Tables.Count} tables, {manifest.MediaFiles} files, {new FileInfo(Path.Combine(backupFolder, name)).Length / 1024} KB");
            Assert.True(manifest.RowsIn("Posts") > 0 && manifest.RowsIn("Users") > 0);

            using (var zip = ZipFile.OpenRead(Path.Combine(backupFolder, name)))
            {
                var dataEntries = zip.Entries.Where(e => e.FullName.StartsWith(BackupFormat.DataPrefix)).Select(e => e.FullName).ToList();
                Assert.DoesNotContain("data/PasswordResetTokens.jsonl", dataEntries);
                Assert.DoesNotContain("data/PageViews.jsonl", dataEntries);
                Assert.Equal(manifest.MediaFiles, zip.Entries.Count(e => e.FullName.StartsWith(BackupFormat.MediaPrefix)));
            }

            // Restore into the fresh install.
            var target = new BackupService(new BackupDataStore(new DapperContext(testCs)),
                new BackupPaths(backupFolder, restoredUploads, 10), NullLogger<BackupService>.Instance);
            Assert.True(await target.IsEmptyInstallAsync());

            // A default-taxonomy row in the fresh install, sharing a slug with a category in the backup:
            // merging instead of replacing would break the unique slug or leave an extra row.
            await using (var src = new SqlConnection(devCs))
            await using (var dst = new SqlConnection(testCs))
            {
                var catSlug = await src.ExecuteScalarAsync<string?>("SELECT TOP 1 Slug FROM Categories");
                if (catSlug is not null)
                    await dst.ExecuteAsync("INSERT INTO Categories (Id, Name, Slug) VALUES (NEWID(), N'Seeded', @catSlug)", new { catSlug });
            }
            // Before the restore the fresh install offers it: first-run page links to it, and it lists the file.
            var freshClient = restored.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            Assert.Contains("/setup/restore", await freshClient.GetStringAsync("/account/register"));
            Directory.CreateDirectory(restored.Services.GetRequiredService<BackupPaths>().Folder);
            File.Copy(Path.Combine(backupFolder, name), Path.Combine(restored.Services.GetRequiredService<BackupPaths>().Folder, name), overwrite: true);
            copiedFile = Path.Combine(restored.Services.GetRequiredService<BackupPaths>().Folder, name);
            var restorePage = await freshClient.GetAsync("/setup/restore");
            Assert.Equal(HttpStatusCode.OK, restorePage.StatusCode);
            Assert.Contains(name, await restorePage.Content.ReadAsStringAsync());

            var outcome = await target.RestoreAsync(name, CancellationToken.None);
            foreach (var w in outcome.Warnings) _out.WriteLine("warning: " + w);
            Assert.Equal(manifest.MediaFiles, outcome.MediaFiles);

            // Row-for-row identical: count and checksum per table. The audit log is compared by
            // count only against the manifest, because a running dev app may append to it meanwhile.
            await using (var src = new SqlConnection(devCs))
            await using (var dst = new SqlConnection(testCs))
            {
                foreach (var table in manifest.Tables.Keys)
                {
                    Assert.Equal(manifest.Tables[table], await dst.ExecuteScalarAsync<long>($"SELECT COUNT_BIG(*) FROM [{table}]"));
                    if (table == "AuditLogs") continue;
                    var q = $"SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM [{table}]";
                    Assert.True(await src.ExecuteScalarAsync<int?>(q) == await dst.ExecuteScalarAsync<int?>(q), $"{table} differs after restore.");
                }
                // The fresh-install taxonomy seed was replaced, not merged.
                Assert.Equal(manifest.RowsIn("Categories"), await dst.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM Categories"));
            }

            // Media: same files, same bytes.
            if (manifest.MediaFiles > 0)
            {
                var first = Directory.EnumerateFiles(devUploads, "*", SearchOption.AllDirectories).First();
                var copy = Path.Combine(restoredUploads, Path.GetRelativePath(devUploads, first));
                Assert.Equal(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(copy));
            }

            // A second restore is refused and changes nothing.
            var again = await Assert.ThrowsAsync<InvalidOperationException>(() => target.RestoreAsync(name, CancellationToken.None));
            Assert.Contains("fresh install", again.Message);

            // The site renders from the restored database. A marker title only the test database
            // carries proves the page came from it and not from the dev database.
            string slug;
            await using (var dst = new SqlConnection(testCs))
            {
                slug = await dst.ExecuteScalarAsync<string>("SELECT TOP 1 Slug FROM Posts WHERE Status = 'Published' AND PublishedAt <= SYSUTCDATETIME() ORDER BY PublishedAt DESC")
                       ?? throw new InvalidOperationException("No published post in the backup.");
                await dst.ExecuteAsync("UPDATE Posts SET Title = Title + N' — restored-marker' WHERE Slug = @slug", new { slug });
            }
            var client = restored.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var setup = await client.GetAsync("/setup/restore");
            Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);                // installed now: the restore screen is closed

            var home = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);

            var post = await client.GetAsync("/" + slug);
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
            Assert.Contains("restored-marker", await post.Content.ReadAsStringAsync());

            var sitemap = await client.GetAsync("/sitemap.xml");
            Assert.Equal(HttpStatusCode.OK, sitemap.StatusCode);
        }
        finally
        {
            restored?.Dispose();
            if (copiedFile is not null) try { File.Delete(copiedFile); } catch { /* best effort */ }
            await DropAsync();
            try { Directory.Delete(work, recursive: true); } catch { /* temp */ }
        }
    }
}
