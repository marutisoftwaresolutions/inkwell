using System.IO.Compression;
using Blog.Core.Domain;
using Blog.Core.Interfaces;

namespace Blog.Web.Services.Import;

/// <summary>
/// Downloads an image by URL (SSRF-guarded), re-encodes it to WebP via the shared image pipeline,
/// stores it under <c>wwwroot/uploads/imported/yyyy-MM</c>, and creates a <see cref="Media"/> row.
/// Best-effort: returns null (never throws) when the URL is blocked, non-image, too large, or fails.
/// </summary>
public class ImageImportService
{
    private readonly HttpClient _http;
    private readonly IImageProcessingService _images;
    private readonly IMediaRepository _media;
    private readonly ILogger<ImageImportService> _logger;

    public ImageImportService(HttpClient http, IImageProcessingService images, IMediaRepository media, ILogger<ImageImportService> logger)
    {
        _http = http;
        _images = images;
        _media = media;
        _logger = logger;
    }

    public async Task<(Guid MediaId, string Url)?> ImportFromUrlAsync(string url, Guid ownerId, bool convertToWebp)
    {
        if (!UrlGuard.TryValidatePublicHttp(url, out var uri) || uri is null)
        {
            _logger.LogWarning("Import image skipped (URL not allowed): {Url}", url);
            return null;
        }

        try
        {
            using var resp = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return null;

            var mime = resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;

            // ReadAsByteArrayAsync honours HttpClient.MaxResponseContentBufferSize (20 MB) → throws if exceeded.
            var bytes = await resp.Content.ReadAsByteArrayAsync();

            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "image";

            using var ms = new MemoryStream(bytes);
            var subdir = $"imported/{DateTime.UtcNow:yyyy-MM}";
            var saved = await _images.SaveImageAsync(ms, fileName, mime, subdir, convertToWebp);

            var media = new Media
            {
                FileName = saved.FileName,
                OriginalFileName = fileName,
                FilePath = saved.RelativeUrl,
                Url = saved.RelativeUrl,
                ContentType = saved.ContentType,
                FileSize = saved.FileSize,
                Width = saved.Width,
                Height = saved.Height,
                UploadedBy = ownerId
            };
            var id = await _media.CreateAsync(media);
            return (id, saved.RelativeUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Import image failed for {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Imports an image straight from an entry inside the uploaded export <c>.zip</c> (Ghost's
    /// <c>content/images/**</c>), converting to WebP. Lets Ghost images be migrated even when the old
    /// site is offline. Returns null when the entry isn't in the archive.
    /// </summary>
    public async Task<(Guid MediaId, string Url)?> ImportFromZipEntryAsync(string zipPath, string imagePathKey, Guid ownerId, bool convertToWebp)
    {
        try
        {
            if (!File.Exists(zipPath)) return null;
            using var zip = ZipFile.OpenRead(zipPath);
            var entryName = MatchZipEntryName(zip.Entries.Select(e => e.FullName), imagePathKey);
            if (entryName == null) return null;
            var entry = zip.GetEntry(entryName) ?? zip.Entries.First(e => e.FullName == entryName);

            var fileName = Path.GetFileName(entry.FullName);
            var mime = MimeFromExtension(Path.GetExtension(fileName));
            if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;

            using var es = entry.Open();
            using var ms = new MemoryStream();
            await es.CopyToAsync(ms);
            ms.Position = 0;

            var subdir = $"imported/{DateTime.UtcNow:yyyy-MM}";
            var saved = await _images.SaveImageAsync(ms, fileName, mime, subdir, convertToWebp);
            var media = new Media
            {
                FileName = saved.FileName, OriginalFileName = fileName, FilePath = saved.RelativeUrl,
                Url = saved.RelativeUrl, ContentType = saved.ContentType, FileSize = saved.FileSize,
                Width = saved.Width, Height = saved.Height, UploadedBy = ownerId
            };
            var id = await _media.CreateAsync(media);
            return (id, saved.RelativeUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Import image from zip entry failed for {Key}", imagePathKey);
            return null;
        }
    }

    /// <summary>Extracts the <c>content/images/…</c> path from a Ghost image URL, or null if absent.</summary>
    public static string? ZipImageKey(string url)
    {
        var idx = url.IndexOf("content/images/", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? url[idx..] : null;
    }

    /// <summary>Finds the zip entry whose (normalised) path ends with the image key — tolerant of a
    /// wrapping folder in the archive (e.g. <c>myblog/content/images/…</c>).</summary>
    public static string? MatchZipEntryName(IEnumerable<string> entryFullNames, string imagePathKey)
    {
        var key = imagePathKey.Replace('\\', '/').TrimStart('/');
        return entryFullNames.FirstOrDefault(n =>
        {
            var norm = n.Replace('\\', '/');
            return norm.Equals(key, StringComparison.OrdinalIgnoreCase)
                || norm.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string MimeFromExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".bmp" => "image/bmp",
        ".tiff" or ".tif" => "image/tiff",
        _ => "application/octet-stream"
    };
}
