using System.Collections.Concurrent;
using Blog.Core.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using ImageInfo = Blog.Core.Services.ImageInfo;

namespace Blog.Web.Services;

/// <summary>
/// The disk half of responsive images (ADR-0032). <see cref="Describe"/> answers, from memory after the first look, an
/// upload's size and which narrower WebP copies exist next to it; <see cref="EnsureVariantsAsync"/> makes the missing
/// copies. Copies are made when an image is uploaded and, for everything already on disk, by <c>ImageVariantJob</c>.
/// Every failure is swallowed: an image without copies renders exactly as before.
/// </summary>
public sealed class ResponsiveImageStore
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private readonly string _webRoot;
    private readonly ILogger<ResponsiveImageStore> _log;
    private readonly ConcurrentDictionary<string, (ImageInfo? Info, DateTime Until)> _cache = new(StringComparer.OrdinalIgnoreCase);

    // Files the backfill has nothing more to do for (every copy made, or a file that could not be decoded), keyed by
    // path and last-write time, so a finished library is not re-read every run and a broken file is not retried for
    // ever; replacing the file makes it count again.
    private readonly ConcurrentDictionary<string, DateTime> _settled = new(StringComparer.OrdinalIgnoreCase);

    private DateTime? Stamp(string uploadPath) { try { return FileOf(uploadPath) is { } f && File.Exists(f) ? File.GetLastWriteTimeUtc(f) : null; } catch { return null; } }

    /// <summary>True when the backfill already finished (or gave up on) this version of the file.</summary>
    public bool IsSettled(string uploadPath) => _settled.TryGetValue(uploadPath, out var at) && Stamp(uploadPath) == at;

    public void MarkSettled(string uploadPath) { if (Stamp(uploadPath) is { } at) _settled[uploadPath] = at; }

    // Files whose pixels could not be decoded on the last try: the only failure the backfill gives up on. A locked file
    // or an I/O error is temporary and is tried again next run.
    private readonly ConcurrentDictionary<string, bool> _undecodable = new(StringComparer.OrdinalIgnoreCase);

    public bool CannotDecode(string uploadPath) => _undecodable.ContainsKey(uploadPath);

    public ResponsiveImageStore(IWebHostEnvironment env, ILogger<ResponsiveImageStore> log)
    {
        _webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        _log = log;
    }

    /// <summary>The file behind a site-relative upload path, or null when it would leave the uploads folder.</summary>
    public string? FileOf(string uploadPath)
    {
        var root = Path.GetFullPath(Path.Combine(_webRoot, "uploads")) + Path.DirectorySeparatorChar;
        var file = Path.GetFullPath(Path.Combine(_webRoot, uploadPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        return file.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? file : null;
    }

    public ImageInfo? Describe(string uploadPath)
    {
        if (!ResponsiveImages.IsSource(uploadPath)) return null;
        if (_cache.TryGetValue(uploadPath, out var hit) && hit.Until > DateTime.UtcNow) return hit.Info;
        ImageInfo? info = null;
        try
        {
            var file = FileOf(uploadPath);
            if (file is not null && File.Exists(file))
            {
                var meta = Image.Identify(file);   // reads the header only
                var variants = ResponsiveImages.WidthsFor(meta.Width)
                    .Where(w => FileOf(ResponsiveImages.VariantPath(uploadPath, w)) is { } v && File.Exists(v)).ToList();
                info = new ImageInfo(meta.Width, meta.Height, variants);
            }
        }
        catch (Exception ex) { _log.LogDebug(ex, "Image {Path} could not be described.", uploadPath); }
        _cache[uploadPath] = (info, DateTime.UtcNow + CacheFor);
        return info;
    }

    public void Forget(string uploadPath) => _cache.TryRemove(uploadPath, out _);

    /// <summary>The copies this image should have but does not. Empty for anything that is not a source image.</summary>
    public IReadOnlyList<int> MissingWidths(string uploadPath, bool fresh = false)
    {
        if (fresh) Forget(uploadPath);
        var info = Describe(uploadPath);
        return info is null ? Array.Empty<int>() : ResponsiveImages.WidthsFor(info.Width).Except(info.Variants).ToList();
    }

    /// <summary>Makes the missing copies of one upload. Returns how many were written; never throws.</summary>
    public async Task<int> EnsureVariantsAsync(string uploadPath, CancellationToken ct = default)
    {
        var missing = MissingWidths(uploadPath, fresh: true);
        if (missing.Count == 0) return 0;
        var made = 0;
        try
        {
            var file = FileOf(uploadPath)!;
            using var image = await Image.LoadAsync(file, ct);
            foreach (var width in missing.OrderByDescending(w => w))
            {
                var target = FileOf(ResponsiveImages.VariantPath(uploadPath, width));
                if (target is null) continue;
                using var copy = image.Clone(x => x.Resize(new ResizeOptions { Size = new Size(width, 0), Mode = ResizeMode.Max }));
                var temp = target + ".tmp";
                await copy.SaveAsWebpAsync(temp, new WebpEncoder { Quality = 80 }, ct);
                File.Move(temp, target, overwrite: true);   // a half-written copy is never served
                made++;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (ImageFormatException ex)
        {
            _undecodable[uploadPath] = true;
            _log.LogWarning(ex, "Responsive copies of {Path} could not be made: the image cannot be decoded.", uploadPath);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Responsive copies of {Path} could not be made (tried again next run).", uploadPath); }
        Forget(uploadPath);
        return made;
    }

    /// <summary>Every source image under the uploads folder, as site-relative paths. Social-card and avatar folders are skipped.</summary>
    public IEnumerable<string> SourceImages()
    {
        var uploads = Path.Combine(_webRoot, "uploads");
        if (!Directory.Exists(uploads)) yield break;
        foreach (var file in Directory.EnumerateFiles(uploads, "*.*", SearchOption.AllDirectories))
        {
            var rel = "/" + Path.GetRelativePath(_webRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            if (rel.StartsWith("/uploads/og/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("/uploads/twitter/", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("/uploads/avatars/", StringComparison.OrdinalIgnoreCase)) continue;
            if (ResponsiveImages.IsSource(rel)) yield return rel;
        }
    }
}
