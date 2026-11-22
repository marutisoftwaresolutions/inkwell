using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Pbm;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Qoi;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Formats.Webp;

namespace Blog.Web.Services;

/// <summary>
/// Which picture formats the image library may read and write. TIFF is left out on purpose: the pinned
/// ImageSharp 3.1.12 has published advisories in its TIFF reader and writer (GHSA-v76p-62qx-wwq2,
/// GHSA-wmxv-xphr-5c9g, GHSA-j9gm-c75j-xc9q, GHSA-jjfr-hcj7-qf5w) that are fixed only in 3.2.0, and
/// Inkwell never needed to decode a TIFF: uploads accept JPG, PNG, GIF and WebP, and an imported TIFF
/// is stored as the file it is. With no TIFF detector a TIFF is "not a picture the library knows", so
/// no code path can reach that reader, whatever the file is named.
/// </summary>
public static class ImageFormatPolicy
{
    private static readonly object Gate = new();
    private static bool _applied;

    /// <summary>
    /// Removes TIFF detection from the library's shared configuration, which every <c>Image.Load</c> and
    /// <c>Image.Identify</c> in the app uses. Safe to call more than once.
    /// </summary>
    public static void Apply()
    {
        lock (Gate)
        {
            if (_applied) return;
            var formats = Configuration.Default.ImageFormatsManager;
            formats.ClearImageFormatDetectors();
            formats.AddImageFormatDetector(new PngImageFormatDetector());
            formats.AddImageFormatDetector(new JpegImageFormatDetector());
            formats.AddImageFormatDetector(new GifImageFormatDetector());
            formats.AddImageFormatDetector(new WebpImageFormatDetector());
            formats.AddImageFormatDetector(new BmpImageFormatDetector());
            formats.AddImageFormatDetector(new PbmImageFormatDetector());
            formats.AddImageFormatDetector(new TgaImageFormatDetector());
            formats.AddImageFormatDetector(new QoiImageFormatDetector());
            _applied = true;
        }
    }

    /// <summary>
    /// A configuration that does not know TIFF at all, for the on-request image resizer: a <c>.tif</c> file
    /// is then served as the file it is, and <c>?format=tiff</c> is not an output it can produce.
    /// </summary>
    public static Configuration WithoutTiff() => new(
        new PngConfigurationModule(),
        new JpegConfigurationModule(),
        new GifConfigurationModule(),
        new WebpConfigurationModule(),
        new BmpConfigurationModule(),
        new PbmConfigurationModule(),
        new TgaConfigurationModule(),
        new QoiConfigurationModule());
}
