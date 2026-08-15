using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace Blog.Web.Services;

/// <summary>Result of saving an image to the uploads tree.</summary>
public record SavedImage(
    string FileName,
    string RelativeUrl,
    string ContentType,
    long FileSize,
    int? Width,
    int? Height,
    bool ConvertedToWebp);

public interface IImageProcessingService
{
    /// <summary>
    /// Saves <paramref name="input"/> under <c>wwwroot/uploads/{subdir}</c>. When
    /// <paramref name="convertToWebp"/> is set and the source is a raster (JPEG/PNG/GIF) it is
    /// re-encoded to WebP (dimensions captured); SVG and existing WebP pass through, and any decode
    /// failure falls back to storing the original bytes. Returns the stored file's metadata.
    /// </summary>
    Task<SavedImage> SaveImageAsync(Stream input, string originalFileName, string mime, string subdir, bool convertToWebp);
}

public class ImageProcessingService : IImageProcessingService
{
    private static readonly HashSet<string> RasterMimes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/gif" };

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ImageProcessingService> _logger;

    public ImageProcessingService(IWebHostEnvironment env, ILogger<ImageProcessingService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public async Task<SavedImage> SaveImageAsync(Stream input, string originalFileName, string mime, string subdir, bool convertToWebp)
    {
        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", subdir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(uploadsDir);

        // Buffer once so the source can be both decoded and (on fallback) written verbatim.
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer);
        var bytes = buffer.ToArray();

        var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = ".bin";
        var baseName = Guid.NewGuid().ToString("N");
        int? width = null, height = null;
        var fileName = baseName + ext;
        var destPath = Path.Combine(uploadsDir, fileName);
        var outMime = mime;
        var converted = false;

        if (convertToWebp && RasterMimes.Contains(mime))
        {
            try
            {
                using var image = Image.Load(bytes);
                width = image.Width;
                height = image.Height;
                fileName = baseName + ".webp";
                destPath = Path.Combine(uploadsDir, fileName);
                await image.SaveAsWebpAsync(destPath, new WebpEncoder { Quality = 80 });
                outMime = "image/webp";
                converted = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WebP conversion failed for {File}; storing original.", originalFileName);
                fileName = baseName + ext;
                destPath = Path.Combine(uploadsDir, fileName);
                converted = false;
            }
        }

        if (!converted)
            await File.WriteAllBytesAsync(destPath, bytes);

        var relativeUrl = $"/uploads/{subdir.TrimEnd('/')}/{fileName}";
        return new SavedImage(fileName, relativeUrl, outMime, bytes.LongLength, width, height, converted);
    }
}
