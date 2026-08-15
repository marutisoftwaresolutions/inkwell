using System.IO.Compression;
using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services.Import;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>Pure unit tests for the Ghost zip-image path helpers.</summary>
public class ZipImageHelperTests
{
    [Fact]
    public void ZipImageKey_extracts_content_images_path()
    {
        Assert.Equal("content/images/2023/02/hero.jpg",
            ImageImportService.ZipImageKey("https://x.com/content/images/2023/02/hero.jpg"));
        Assert.Null(ImageImportService.ZipImageKey("https://x.com/assets/pic.jpg"));
    }

    [Fact]
    public void MatchZipEntryName_matches_even_with_wrapping_folder()
    {
        var entries = new[] { "myblog/content/images/2023/02/hero.jpg", "myblog/blog.json" };
        Assert.Equal("myblog/content/images/2023/02/hero.jpg",
            ImageImportService.MatchZipEntryName(entries, "content/images/2023/02/hero.jpg"));
        Assert.Null(ImageImportService.MatchZipEntryName(entries, "content/images/nope.jpg"));
    }
}

/// <summary>Integration tests (real DB) for Phase 4: Ghost zip-image import and import undo/rollback.</summary>
public class ImportPhase4IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    // 1×1 transparent PNG.
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M8AAAMBAQDJ/pLvAAAAAElFTkSuQmCC");

    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public ImportPhase4IntegrationTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    private bool ProbeDb()
    {
        try
        {
            var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs)) return false;
            using var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            conn.Open();
            return true;
        }
        catch { return false; }
    }

    [Fact]
    public async Task Ghost_zip_image_is_imported_from_the_archive()
    {
        if (!_dbAvailable) { _out.WriteLine("[skip] no DB"); return; }

        var zipPath = Path.Combine(Path.GetTempPath(), $"ghost-{Guid.NewGuid():N}.zip");
        using (var zs = new FileStream(zipPath, FileMode.Create))
        using (var zip = new ZipArchive(zs, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("myblog/content/images/2023/02/hero.png");
            using var es = e.Open();
            es.Write(PngBytes, 0, PngBytes.Length);
        }

        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<IUserRepository>();
        var jobs = sp.GetRequiredService<IImportJobRepository>();
        var processor = sp.GetRequiredService<ImportProcessor>();
        var media = sp.GetRequiredService<IMediaRepository>();
        var env = sp.GetRequiredService<IWebHostEnvironment>();

        var admin = await users.GetFirstAdminAsync();
        if (admin is null) { _out.WriteLine("[skip] no admin"); return; }

        var opts = new ImportOptions { ImportImages = true, ConvertToWebP = true, DefaultAuthorId = admin.Id };
        var job = new ImportJob
        {
            OwnerId = admin.Id, Source = ImportSource.Ghost, FileName = "ghost.zip", FilePath = zipPath,
            Status = ImportJobStatus.Analyzed, OptionsJson = JsonSerializer.Serialize(opts)
        };
        job.Id = await jobs.CreateJobAsync(job);
        await jobs.AddItemsAsync(new[]
        {
            new ImportItem
            {
                JobId = job.Id, ItemType = ImportItemType.Image, Ordinal = 3,
                SourceId = "https://ghost.example.com/content/images/2023/02/hero.png",
                Title = "hero.png", Status = ImportItemStatus.Pending,
                DataJson = JsonSerializer.Serialize(new ParsedImage { Url = "https://ghost.example.com/content/images/2023/02/hero.png" })
            }
        });

        Guid? mediaId = null; string? webPath = null;
        try
        {
            var result = await processor.ProcessBatchAsync(job, 10);
            Assert.Equal(1, result.Imported);
            Assert.Equal(0, result.Failed);

            var imgItem = (await jobs.GetItemsAsync(job.Id, ImportItemStatus.Imported)).Single();
            mediaId = imgItem.TargetId;
            Assert.NotNull(mediaId);
            var m = await media.GetByIdAsync(mediaId!.Value);
            Assert.NotNull(m);
            // Ingested from the zip archive into the imported-media tree (URL download would have been blocked).
            Assert.Contains("/uploads/imported/", m!.Url);
            webPath = Path.Combine(env.WebRootPath, m.Url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(webPath), "image file should exist on disk after zip ingestion");
        }
        finally
        {
            if (mediaId.HasValue) await media.DeleteAsync(mediaId.Value, admin.Id);
            await jobs.DeleteJobAsync(job.Id, admin.Id);
            try { if (webPath != null && File.Exists(webPath)) File.Delete(webPath); } catch { }
            try { File.Delete(zipPath); } catch { }
        }
    }

    [Fact]
    public async Task Undo_removes_imported_post()
    {
        if (!_dbAvailable) { _out.WriteLine("[skip] no DB"); return; }

        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<IUserRepository>();
        var jobs = sp.GetRequiredService<IImportJobRepository>();
        var processor = sp.GetRequiredService<ImportProcessor>();
        var posts = sp.GetRequiredService<IPostRepository>();

        var admin = await users.GetFirstAdminAsync();
        if (admin is null) { _out.WriteLine("[skip] no admin"); return; }

        var slug = $"itest-undo-{Guid.NewGuid():N}"[..20];
        var opts = new ImportOptions { ImportPosts = true, ImportImages = false, SlugConflict = "Suffix", DefaultAuthorId = admin.Id };
        var job = new ImportJob
        {
            OwnerId = admin.Id, Source = ImportSource.WordPress, FileName = "u.xml",
            Status = ImportJobStatus.Analyzed, OptionsJson = JsonSerializer.Serialize(opts)
        };
        job.Id = await jobs.CreateJobAsync(job);
        await jobs.AddItemsAsync(new[]
        {
            new ImportItem
            {
                JobId = job.Id, ItemType = ImportItemType.Post, Ordinal = 4, SourceId = "u1",
                Title = "Undo Me", Status = ImportItemStatus.Pending,
                DataJson = JsonSerializer.Serialize(new ParsedPost { Title = "Undo Me", Slug = slug, Html = "<p>x</p>", Status = "publish", PublishedAt = DateTime.UtcNow })
            }
        });

        try
        {
            await processor.ProcessBatchAsync(job, 10);
            Assert.NotNull(await posts.GetBySlugAsync(slug));

            var removed = await processor.UndoAsync(job);
            Assert.Equal(1, removed);
            Assert.Null(await posts.GetBySlugAsync(slug)); // gone after undo
        }
        finally
        {
            var leftover = await posts.GetBySlugAsync(slug);
            if (leftover != null) await posts.DeleteAsync(leftover.Id);
            await jobs.DeleteJobAsync(job.Id, admin.Id);
        }
    }
}
