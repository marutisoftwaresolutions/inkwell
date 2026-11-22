using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Blog.Core.Services;
using Blog.Web.Services.Backups;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Operations basics: health endpoints that say only "ok" or "unavailable", a plain warning when the
/// server's .NET runtime is behind the level the release was tested on, and a check that reads a
/// backup end to end without restoring it.
/// </summary>
public class OperationsBasicsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public OperationsBasicsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    // ── Runtime baseline ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("10.0.14", false)]
    [InlineData("10.0.15", false)]
    [InlineData("10.0.6", true)]      // the April 2026 Data Protection flaw was fixed after this
    [InlineData("10.0.0", true)]
    [InlineData("11.0.0", false)]     // a line this release knows nothing about is not judged
    [InlineData("10.1.0", false)]
    public void An_older_patch_of_the_tested_runtime_line_is_reported(string running, bool behind)
    {
        var problem = RuntimeBaseline.Problem(Version.Parse(running));
        Assert.Equal(behind, problem is not null);
        if (behind)
        {
            Assert.Contains(".NET " + running, problem);
            Assert.Contains("Update the .NET runtime", problem);
        }
    }

    [Fact]
    public void The_tested_runtime_is_the_line_the_app_targets()
    {
        Assert.Equal(10, RuntimeBaseline.TestedOn.Major);
        Assert.Null(RuntimeBaseline.Problem(RuntimeBaseline.TestedOn));
    }

    // ── Health ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Liveness_answers_ok_without_touching_the_database_and_gives_nothing_away()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_follows_the_database()
    {
        var response = await _factory.CreateClient().GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();
        if (TestDatabase.Available(_factory))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("ok", body);
        }
        else
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("unavailable", body);   // no connection string, no exception text
        }
    }

    [Fact]
    public async Task Readiness_is_503_when_the_database_cannot_be_reached()
    {
        using var down = _factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:DefaultConnection",
            "Server=127.0.0.1,1;Database=nodb;User Id=x;Password=x;Connect Timeout=1;TrustServerCertificate=True;"));
        var response = await down.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("unavailable", await response.Content.ReadAsStringAsync());
    }

    // ── Backup check ─────────────────────────────────────────────────────────

    private static (BackupService Service, string Folder, string Name) NewBackup(Action<ZipArchive, BackupManifest> fill)
    {
        var folder = Path.Combine(Path.GetTempPath(), "inkwell-backup-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var name = BackupFormat.FileNameFor(new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc));
        var manifest = new BackupManifest { CreatedAtUtc = DateTime.UtcNow, AppVersion = "test" };
        using (var zip = ZipFile.Open(Path.Combine(folder, name), ZipArchiveMode.Create))
        {
            fill(zip, manifest);
            using var m = zip.CreateEntry(BackupFormat.ManifestEntry).Open();
            JsonSerializer.Serialize(m, manifest);
        }
        var service = new BackupService(null!, new BackupPaths(folder, folder, 3), NullLogger<BackupService>.Instance);
        return (service, folder, name);
    }

    private static void Write(ZipArchive zip, string entry, string text)
    {
        using var s = zip.CreateEntry(entry).Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        s.Write(bytes, 0, bytes.Length);
    }

    [Fact]
    public void A_whole_backup_passes_the_check()
    {
        var (service, folder, name) = NewBackup((zip, manifest) =>
        {
            Write(zip, BackupFormat.DataEntry("Posts"), "{\"Id\":1}\n{\"Id\":2}\n");
            manifest.Tables["Posts"] = 2;
            Write(zip, BackupFormat.DataEntry("Users"), "{\"Id\":1}\n"); manifest.Tables["Users"] = 1;
            Write(zip, BackupFormat.DataEntry("Settings"), "{\"Id\":1}\n"); manifest.Tables["Settings"] = 1;
            Write(zip, BackupFormat.MediaPrefix + "images/a.webp", "image-bytes");
            manifest.MediaFiles = 1;
        });
        try { Assert.Empty(service.Verify(name)); }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Missing_rows_a_missing_table_and_missing_media_are_each_reported()
    {
        var (service, folder, name) = NewBackup((zip, manifest) =>
        {
            Write(zip, BackupFormat.DataEntry("Posts"), "{\"Id\":1}\n");
            manifest.Tables["Posts"] = 2;        // one row short
            manifest.Tables["Pages"] = 3;        // no file at all
            manifest.MediaFiles = 4;             // none stored
        });
        try
        {
            var problems = service.Verify(name);
            Assert.Contains(problems, p => p.Contains("Posts holds 1 rows; the manifest says 2"));
            Assert.Contains(problems, p => p.Contains("The data for Pages is missing"));
            Assert.Contains(problems, p => p.Contains("0 media files; the manifest says 4"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void A_file_that_is_not_a_backup_or_is_gone_is_reported_not_thrown()
    {
        var folder = Path.Combine(Path.GetTempPath(), "inkwell-backup-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var service = new BackupService(null!, new BackupPaths(folder, folder, 3), NullLogger<BackupService>.Instance);
            var name = BackupFormat.FileNameFor(DateTime.UtcNow);
            Assert.Single(service.Verify(name));                          // not there
            Assert.Single(service.Verify("../../appsettings.json"));      // not a backup name: never opened
            File.WriteAllText(Path.Combine(folder, name), "this is not a zip");
            Assert.Contains(service.Verify(name), p => p.Contains("damaged"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void The_check_is_an_audited_post_behind_the_backups_screen()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        var controller = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Controllers", "BackupsController.cs"));
        var check = controller[controller.IndexOf("[HttpPost(\"check/{name}\")]", StringComparison.Ordinal)..];
        Assert.Contains("[ValidateAntiForgeryToken]", check[..200]);
        Assert.Contains("AuditActions.BackupChecked", check);
    }
}
