using System.IO.Compression;
using System.Text;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Core.Services.Export;

namespace Blog.Web.Services.Export;

/// <summary>A finished export, ready to stream: the file deletes itself when the stream is closed.</summary>
public sealed record ExportFile(Stream Content, string DownloadName, string ContentType, ExportResult Result, int MissingImages);

/// <summary>
/// Produces WordPress (WXR) and Ghost (JSON in a ZIP with content/images) exports (ADR-0020). Each
/// export is written to a temporary file outside the web root (<c>Exports:Path</c>, default
/// <c>App_Data/exports</c>), streamed to the admin who asked for it, and deleted when the download
/// closes. Nothing is kept: files left behind by a crash are swept on the next export.
/// </summary>
public sealed class ExportService
{
    public static readonly TimeSpan SweepAge = TimeSpan.FromHours(1);

    private readonly IExportSource _source;
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly string _folder;
    private readonly string _uploadsRoot;
    private readonly ILogger<ExportService> _log;

    public ExportService(IExportSource source, ISettingRepository settings, IUserRepository users,
        IWebHostEnvironment env, IConfiguration config, ILogger<ExportService> log)
    {
        _source = source;
        _settings = settings;
        _users = users;
        _log = log;
        var folder = config["Exports:Path"];
        _folder = string.IsNullOrWhiteSpace(folder) ? Path.Combine(env.ContentRootPath, "App_Data", "exports") : folder;
        _uploadsRoot = Path.Combine(env.WebRootPath, "uploads");
    }

    public Task<ExportCounts> CountAsync(ExportOptions options) => _source.CountAsync(options);

    public async Task<ExportSite> SiteAsync(string baseUrl)
    {
        var owner = (await _users.GetFirstAdminAsync())?.Id ?? Guid.Empty;
        var s = await _settings.GetSettingsAsync(owner);
        return new ExportSite(string.IsNullOrWhiteSpace(s.SiteName) ? "Inkwell site" : s.SiteName,
            s.SiteDescription ?? "", baseUrl.TrimEnd('/'), string.IsNullOrWhiteSpace(s.SiteLanguage) ? "en" : s.SiteLanguage,
            TimeZoneHelper.Resolve(s.DisplayTimeZoneId));
    }

    public async Task<ExportFile> CreateAsync(ExportTarget target, ExportOptions options, string baseUrl, CancellationToken ct)
    {
        Directory.CreateDirectory(_folder);
        Sweep();

        var site = await SiteAsync(baseUrl);
        var catalog = await _source.LoadCatalogAsync(options);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
        var name = Slug(site.Title);
        var temp = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".tmp");
        var posts = _source.StreamPostsAsync(options, ct);
        var pages = options.IncludePages ? _source.StreamPagesAsync(options, ct) : null;

        try
        {
            ExportResult result;
            var missing = 0;
            if (target == ExportTarget.WordPress)
            {
                await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    result = await WxrExportWriter.WriteAsync(fs, site, catalog, posts, pages, options, ct);
                return new ExportFile(OpenAndDelete(temp), $"{name}-wordpress-{stamp}.xml", "application/xml", result, 0);
            }

            await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                // Ghost reads the JSON from the archive root and images from content/images/**.
                var json = zip.CreateEntry($"{name}-ghost-{stamp}.json", CompressionLevel.Optimal);
                await using (var js = json.Open())
                    result = await GhostExportWriter.WriteAsync(js, site, catalog, posts, pages, options, ct);

                var root = Path.GetFullPath(_uploadsRoot) + Path.DirectorySeparatorChar;
                foreach (var upload in result.UploadPaths)
                {
                    var file = Path.GetFullPath(Path.Combine(_uploadsRoot, upload["/uploads/".Length..].Replace('/', Path.DirectorySeparatorChar)));
                    if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) { missing++; continue; }
                    var entry = zip.CreateEntry(ExportHtml.GhostImagePath(upload).TrimStart('/'), CompressionLevel.NoCompression);
                    await using var src = File.OpenRead(file);
                    await using var dst = entry.Open();
                    await src.CopyToAsync(dst, ct);
                    result.Attachments++;
                }
            }
            return new ExportFile(OpenAndDelete(temp), $"{name}-ghost-{stamp}.zip", "application/zip", result, missing);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static Stream OpenAndDelete(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);

    /// <summary>Deletes exports older than <see cref="SweepAge"/> (left by a crash or an aborted download).</summary>
    public int Sweep()
    {
        if (!Directory.Exists(_folder)) return 0;
        var n = 0;
        foreach (var f in Directory.EnumerateFiles(_folder, "*.tmp"))
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > SweepAge && TryDelete(f)) n++;
        return n;
    }

    private bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (Exception ex) { _log.LogDebug(ex, "Export temp file {Path} not deleted yet.", path); return false; }
    }

    private static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.ToLowerInvariant())
            sb.Append(ch is >= 'a' and <= 'z' or >= '0' and <= '9' ? ch : '-');
        var slug = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "inkwell" : slug.Length > 40 ? slug[..40].Trim('-') : slug;
    }
}
