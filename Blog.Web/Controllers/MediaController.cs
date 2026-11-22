using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace Blog.Web.Controllers;

[Authorize(Policy = "CanManageMedia")]
[Route("admin/[controller]")]
public class MediaController : Controller
{
    private readonly IMediaRepository _media;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<MediaController> _logger;
    private readonly AuditService _audit;
    private readonly IImageProcessingService _imageProcessing;
    private readonly ResponsiveImageStore _responsive;

    /// <summary>Files per library page. Also bounds the reference query behind the delete dialog.</summary>
    public const int PageSize = 48;

    /// <summary>Largest file the library accepts; the view quotes <see cref="MaxUploadMegabytes"/> so the copy cannot drift.</summary>
    public const int MaxUploadMegabytes = 10;
    public const long MaxUploadBytes = MaxUploadMegabytes * 1024L * 1024L;

    private static readonly string[] AllowedMimeList =
    {
        "image/jpeg","image/png","image/gif","image/webp","image/svg+xml",
        "video/mp4","video/webm",
        "application/pdf",
        "application/msword","application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };
    private static readonly HashSet<string> AllowedMimes = new(AllowedMimeList, StringComparer.OrdinalIgnoreCase);

    /// <summary>The file input's <c>accept</c> list — exactly the types <see cref="Upload"/> admits, nothing more.</summary>
    public static readonly string AcceptAttribute = string.Join(",", AllowedMimeList);

    /// <summary>Upload folders the library knows. The empty key is "every folder".</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Folders = new[]
    {
        ("", "All"), ("images", "General"), ("og", "Open Graph"), ("twitter", "Twitter Cards")
    };

    /// <summary>Whitelists a folder from the query string or an upload form: unknown values become <paramref name="fallback"/>.</summary>
    public static string NormalizeFolder(string? folder, string fallback = "")
        => (folder ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "images" => "images",
            "og" => "og",
            "twitter" => "twitter",
            _ => fallback
        };

    public MediaController(IMediaRepository media, IWebHostEnvironment env, ILogger<MediaController> logger,
        AuditService audit, IImageProcessingService imageProcessing, ResponsiveImageStore responsive)
    {
        _responsive = responsive;
        _media = media;
        _env = env;
        _logger = logger;
        _audit = audit;
        _imageProcessing = imageProcessing;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? folder, int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var query = (q ?? string.Empty).Trim();
        var folderKey = NormalizeFolder(folder);
        if (page < 1) page = 1;

        var items = await _media.SearchAsync(userId, query, page, PageSize, folderKey);
        var total = await _media.CountSearchAsync(userId, query, folderKey);
        // Who still embeds each file on this page — the delete dialog says so instead of guessing.
        var references = items.Count == 0
            ? new Dictionary<Guid, List<MediaReference>>()
            : await _media.FindReferencesAsync(items);

        ViewBag.Total = total;
        ViewBag.Page = page;
        ViewData["ListSearchPlaceholder"] = "Search files by name";
        // The folder tab lives in the query string so search, paging and the post-delete redirect keep it.
        ViewData["ListSearchKeep"] = new Dictionary<string, string?> { ["folder"] = folderKey.Length == 0 ? null : folderKey };
        ViewData["MediaFolder"] = folderKey;
        ViewData["MediaReferences"] = references;
        Blog.Web.Models.ListPaging.Stash(this, Blog.Web.Models.PagingMeta.FromCounts(query, page, total, PageSize, items.Count));
        return View(items);
    }

    /// <summary>How many files the unused report lists at most; it says so when it reaches the limit.</summary>
    public const int UnusedReportSize = 200;

    /// <summary>
    /// Files nothing on the site points at, so the library can be tidied. A file is listed only when it is in no
    /// post, page, site setting, theme setting or profile photo. It cannot know about links from other sites or
    /// from newsletters already sent, and says so. Read-only: deleting is the library's own delete, one file at a time.
    /// </summary>
    [HttpGet("unused")]
    public async Task<IActionResult> Unused()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        List<Media> items;
        try { items = await _media.GetUnusedAsync(userId, UnusedReportSize); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unused media report failed");
            TempData["Error"] = "The report took too long on this library. Nothing was changed.";
            return RedirectToAction(nameof(Index));
        }
        ViewBag.Limit = UnusedReportSize;
        return View(items);
    }

    [HttpPost("upload")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 104857600, ValueLengthLimit = 104857600)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, [FromForm] string folder = "images")
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file was received." });

            if (file.Length > MaxUploadBytes)
                return BadRequest(new { error = $"The file is larger than {MaxUploadMegabytes} MB." });

            var mime = file.ContentType;
            if (!AllowedMimes.Contains(mime))
                return BadRequest(new { error = "That file type is not accepted. Upload an image, MP4 or WebM video, PDF, Word or Excel file." });

            // Save under wwwroot/uploads/{folder}/{yyyy-MM}. Raster images are auto-converted to WebP
            // (with dimensions captured) by the shared IImageProcessingService — the same code path the
            // content importer uses. SVG is sanitised (SvgSanitizer); other non-raster types pass through, stored
            // under the extension that belongs to their declared type (UploadFileTypes).
            var safeFolder = NormalizeFolder(folder, "images");
            var relativeMonthDir = DateTime.UtcNow.ToString("yyyy-MM");
            var subdir = $"{safeFolder}/{relativeMonthDir}";

            var saved = await _imageProcessing.SaveImageAsync(file.OpenReadStream(), file.FileName, mime, subdir, convertToWebp: true);
            // Narrower copies for srcset (ADR-0032). Never fails the upload; the image-variants job retries anything missed.
            await _responsive.EnsureVariantsAsync(saved.RelativeUrl, CancellationToken.None);   // a dropped connection must not abort a stored upload
            var fileName = saved.FileName;
            mime = saved.ContentType;
            var storedSize = saved.FileSize;
            int? width = saved.Width, height = saved.Height;
            var relativePath = saved.RelativeUrl;
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                return Unauthorized(new { error = "User not identified." });

            var media = new Media
            {
                FileName = fileName,
                OriginalFileName = file.FileName,
                FilePath = relativePath,
                Url = relativePath,
                ContentType = mime,
                FileSize = storedSize,
                Width = width,
                Height = height,
                UploadedBy = userId
            };

            var id = await _media.CreateAsync(media);
            media.Id = id;

            await _audit.LogAsync(AuditActions.MediaUploaded, "Media", media.Id.ToString(), file.FileName);
            return Ok(new { id = media.Id, url = media.Url, fileName = media.FileName });
        }
        catch (ImageTooLargeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (NotAPictureException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidDataException)
        {
            return BadRequest(new { error = "That SVG could not be read. Export it as a plain SVG and try again, or upload a PNG." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Media upload failed");
            // The reason reaches the operator; the stack trace stays in the log.
            return StatusCode(500, new { error = "The upload failed on the server: " + ex.Message });
        }
    }

    [HttpPost("delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, [FromForm] string? folder, [FromForm] string? q, [FromForm] int page = 1, [FromForm] string? returnTo = null)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized();

        var item = await _media.GetByIdAsync(id, userId);
        if (item != null)
        {
            var filePath = item.FilePath;
            if (!string.IsNullOrEmpty(filePath) && filePath.StartsWith("/"))
            {
                // Remove the leading slash to make it a relative path for Path.Combine
                filePath = filePath.TrimStart('/');
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                var fullPath = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), filePath.Replace('/', Path.DirectorySeparatorChar));
                _logger.LogInformation("Attempting to delete file from disk: {FullPath}", fullPath);

                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                    // Its responsive copies go with it (ADR-0032).
                    foreach (var w in Blog.Core.Services.ResponsiveImages.VariantWidths)
                        if (_responsive.FileOf(Blog.Core.Services.ResponsiveImages.VariantPath("/" + filePath, w)) is { } copy && System.IO.File.Exists(copy))
                            try { System.IO.File.Delete(copy); }
                            catch (Exception ex) { _logger.LogWarning(ex, "Responsive copy {Copy} could not be deleted.", copy); }   // never blocks the delete
                    _responsive.Forget("/" + filePath);
                    _logger.LogInformation("Successfully deleted from disk: {FullPath}", fullPath);
                }
                else
                {
                    _logger.LogWarning("File not found on disk: {FullPath}", fullPath);
                }
            }

            await _media.DeleteAsync(id, userId);
            _logger.LogInformation("Deleted DB record for media ID: {Id}", id);
            await _audit.LogAsync(AuditActions.MediaDeleted, "Media", id.ToString(), item.FileName);
        }
        TempData["Success"] = "File deleted.";

        // Someone clearing the unused-files report stays on the report.
        if (returnTo == "unused") return RedirectToAction(nameof(Unused));

        // Back to the same folder tab, search and page the user deleted from.
        var folderKey = NormalizeFolder(folder);
        return RedirectToAction("Index", new
        {
            folder = folderKey.Length > 0 ? folderKey : null,
            q = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            page = page > 1 ? page : (int?)null
        });
    }

    public const int MaxAltLength = 300;
    public const int MaxCaptionLength = 500;

    /// <summary>Alt text and caption for one file. The editors copy them in when the file is placed.</summary>
    [HttpGet("details/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, string? folder, string? q, int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var item = await _media.GetByIdAsync(id, userId);
        if (item == null) return NotFound();   // images get alt text, caption, crop and replace; other files get replace
        ViewData["MediaFolder"] = NormalizeFolder(folder);
        ViewData["MediaQuery"] = q;
        ViewData["MediaPage"] = page;
        return View(item);
    }

    [HttpPost("details/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Details(Guid id, [FromForm] string? altText, [FromForm] string? caption,
        [FromForm] string? folder, [FromForm] string? q, [FromForm] int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var item = await _media.GetByIdAsync(id, userId);
        if (item == null) return NotFound();

        altText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();
        caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        if (altText?.Length > MaxAltLength)
            ModelState.AddModelError("AltText", $"Keep the alt text under {MaxAltLength} characters; describe what the image shows.");
        if (caption?.Length > MaxCaptionLength)
            ModelState.AddModelError("Caption", $"Keep the caption under {MaxCaptionLength} characters.");
        if (!ModelState.IsValid)
        {
            item.AltText = altText;
            item.Caption = caption;
            ViewData["MediaFolder"] = NormalizeFolder(folder);
            ViewData["MediaQuery"] = q;
            ViewData["MediaPage"] = page;
            return View(item);
        }

        var before = new { item.AltText, item.Caption };
        await _media.UpdateDetailsAsync(id, userId, altText, caption);
        await _audit.LogAsync(AuditActions.MediaUpdated, "Media", id.ToString(), item.FileName,
            oldValues: System.Text.Json.JsonSerializer.Serialize(before),
            newValues: System.Text.Json.JsonSerializer.Serialize(new { AltText = altText, Caption = caption }));
        TempData["Success"] = "Image details saved.";

        var folderKey = NormalizeFolder(folder);
        return RedirectToAction("Index", new
        {
            folder = folderKey.Length > 0 ? folderKey : null,
            q = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            page = page > 1 ? page : (int?)null
        });
    }

    // ── Crop and replace ───────────────────────────────────────────────────

    private static readonly HashSet<string> RasterExtensions = new(StringComparer.OrdinalIgnoreCase) { ".webp", ".jpg", ".jpeg", ".png", ".gif" };
    private static readonly HashSet<string> RasterUploadMimes = new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/gif", "image/webp" };

    private IActionResult BackToDetails(Guid id, string? folder, string? q, int page)
    {
        var folderKey = NormalizeFolder(folder);
        return RedirectToAction(nameof(Details), new
        {
            id,
            folder = folderKey.Length > 0 ? folderKey : null,
            q = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            page = page > 1 ? page : (int?)null
        });
    }

    /// <summary>The folder under uploads a file sits in ("images/2026-11"), so a cropped copy lands beside its original.</summary>
    private static string SubdirOf(string url)
    {
        const string prefix = "/uploads/";
        var slash = url.LastIndexOf('/');
        var dir = url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && slash > prefix.Length ? url[prefix.Length..slash] : "";
        return dir.Length > 0 && !dir.Contains("..", StringComparison.Ordinal) ? dir : "images/" + DateTime.UtcNow.ToString("yyyy-MM");
    }

    /// <summary>
    /// Cuts a rectangle out of an image and saves it as a NEW library file. The original and every post
    /// that uses it are untouched, so a crop can never change what readers already see.
    /// </summary>
    [HttpPost("details/{id:guid}/crop")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crop(Guid id, [FromForm] int x, [FromForm] int y, [FromForm] int width, [FromForm] int height,
        [FromForm] string? folder, [FromForm] string? q, [FromForm] int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var item = await _media.GetByIdAsync(id, userId);
        if (item == null) return NotFound();

        var source = _responsive.FileOf(item.Url);
        if (source is null || !System.IO.File.Exists(source) || !RasterExtensions.Contains(Path.GetExtension(source)))
        {
            TempData["Error"] = "This file cannot be cropped. Cropping works on JPEG, PNG, GIF and WebP pictures stored in the library.";
            return BackToDetails(id, folder, q, page);
        }

        try
        {
            ImageGuard.Check(source);
            using var image = await SixLabors.ImageSharp.Image.LoadAsync(source);
            if (Blog.Core.Services.ImageCrop.Clamp(image.Width, image.Height, x, y, width, height) is not { } rect)
            {
                TempData["Error"] = $"Choose an area inside the picture, at least {Blog.Core.Services.ImageCrop.MinSide} pixels on each side and smaller than the whole picture ({image.Width} × {image.Height}).";
                return BackToDetails(id, folder, q, page);
            }

            SixLabors.ImageSharp.Processing.ProcessingExtensions.Mutate(image, c =>
                SixLabors.ImageSharp.Processing.CropExtensions.Crop(c, new SixLabors.ImageSharp.Rectangle(rect.X, rect.Y, rect.Width, rect.Height)));

            // Through the same pipeline as an upload: stored as WebP, dimensions captured, responsive copies made.
            using var png = new MemoryStream();
            await SixLabors.ImageSharp.ImageExtensions.SaveAsPngAsync(image, png);
            png.Position = 0;
            var originalName = string.IsNullOrEmpty(item.OriginalFileName) ? item.FileName : item.OriginalFileName;
            var copyName = Path.GetFileNameWithoutExtension(originalName) + $" (cropped {rect.Width}x{rect.Height}).png";
            var saved = await _imageProcessing.SaveImageAsync(png, copyName, "image/png", SubdirOf(item.Url), convertToWebp: true);
            await _responsive.EnsureVariantsAsync(saved.RelativeUrl, CancellationToken.None);

            var copy = new Media
            {
                FileName = saved.FileName,
                OriginalFileName = Path.ChangeExtension(copyName, Path.GetExtension(saved.FileName)),
                FilePath = saved.RelativeUrl,
                Url = saved.RelativeUrl,
                ContentType = saved.ContentType,
                FileSize = saved.FileSize,
                Width = saved.Width ?? rect.Width,
                Height = saved.Height ?? rect.Height,
                UploadedBy = userId,
            };
            copy.Id = await _media.CreateAsync(copy);
            if (item.AltText is not null || item.Caption is not null)
                await _media.UpdateDetailsAsync(copy.Id, userId, item.AltText, item.Caption);

            await _audit.LogAsync(AuditActions.MediaUploaded, "Media", copy.Id.ToString(), copy.OriginalFileName,
                newValues: $"Cropped from {originalName}: {rect.Width} × {rect.Height} at {rect.X}, {rect.Y}");
            TempData["Success"] = "Cropped copy saved. The original is unchanged.";
            return BackToDetails(copy.Id, folder, q, 1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Crop failed for media {Id}", id);
            TempData["Error"] = ex is ImageTooLargeException ? ex.Message + " Nothing was cropped." : "The picture could not be read, so nothing was cropped.";
            return BackToDetails(id, folder, q, page);
        }
    }

    /// <summary>
    /// Puts a new file behind an existing library entry. The address stays the same, so every post and
    /// page that uses the image shows the new one. The new file must be the same kind (a picture for a
    /// picture, an SVG for an SVG) because the address carries the type. The old file is not kept.
    /// </summary>
    [HttpPost("details/{id:guid}/replace")]
    [RequestSizeLimit(MaxUploadBytes + 1024 * 1024)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Replace(Guid id, [FromForm] IFormFile? file, [FromForm] string? folder, [FromForm] string? q, [FromForm] int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var item = await _media.GetByIdAsync(id, userId);
        if (item == null) return NotFound();

        if (file == null || file.Length == 0) { TempData["Error"] = "Choose the new file first."; return BackToDetails(id, folder, q, page); }
        if (file.Length > MaxUploadBytes) { TempData["Error"] = $"The file is larger than {MaxUploadMegabytes} MB."; return BackToDetails(id, folder, q, page); }

        var target = _responsive.FileOf(item.Url);
        if (target is null || !System.IO.File.Exists(target))
        {
            TempData["Error"] = "The stored file could not be found, so it cannot be replaced. Upload the new file as a new image instead.";
            return BackToDetails(id, folder, q, page);
        }

        var ext = Path.GetExtension(target);
        var isSvgTarget = ext.Equals(".svg", StringComparison.OrdinalIgnoreCase);
        var temp = target + "." + Guid.NewGuid().ToString("N")[..8] + ".replacing";   // unique: two replaces of one file never share it
        var moved = false;
        try
        {
            int? width = null, height = null;
            if (isSvgTarget)
            {
                if (!Blog.Core.Services.SvgSanitizer.IsSvg(file.FileName, file.ContentType))
                {
                    TempData["Error"] = "This file is an SVG, so replace it with another SVG. To use a photo instead, upload it as a new image.";
                    return BackToDetails(id, folder, q, page);
                }
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer);
                var clean = Blog.Core.Services.SvgSanitizer.Sanitize(buffer.ToArray());
                if (clean is null)
                {
                    TempData["Error"] = "That SVG could not be read. Export it as a plain SVG and try again.";
                    return BackToDetails(id, folder, q, page);
                }
                await System.IO.File.WriteAllBytesAsync(temp, clean);
            }
            else if (RasterExtensions.Contains(ext) && RasterUploadMimes.Contains(file.ContentType))
            {
                // Decoded and re-encoded in the stored file's own format, so the address and its type stay true.
                using var incoming = new MemoryStream();
                await file.CopyToAsync(incoming);
                var incomingBytes = incoming.ToArray();
                ImageGuard.Check(incomingBytes);
                using var image = SixLabors.ImageSharp.Image.Load(incomingBytes);
                width = image.Width; height = image.Height;
                await using var output = System.IO.File.Create(temp);
                var formats = image.Configuration.ImageFormatsManager;
                if (!formats.TryFindFormatByFileExtension(ext.TrimStart('.'), out var format)) throw new InvalidDataException("No encoder for " + ext);
                await image.SaveAsync(output, formats.GetEncoder(format));
            }
            else if (RasterExtensions.Contains(ext))
            {
                TempData["Error"] = "Replace this picture with a JPEG, PNG, GIF or WebP file. To change the kind of file, upload it as a new one.";
                return BackToDetails(id, folder, q, page);
            }
            else if (AllowedMimes.Contains(file.ContentType)
                     && string.Equals(Blog.Core.Services.UploadFileTypes.SafeExtension(file.FileName, file.ContentType), ext, StringComparison.OrdinalIgnoreCase))
            {
                // A document or a video: the new file must be the same kind, judged by its declared type as on upload,
                // because the address ends in that type. Stored as sent.
                await using var output = System.IO.File.Create(temp);
                await file.CopyToAsync(output);
            }
            else
            {
                TempData["Error"] = $"This file is a {ext.TrimStart('.').ToUpperInvariant()} file, so replace it with another one of the same kind. To change the kind of file, upload it as a new one.";
                return BackToDetails(id, folder, q, page);
            }

            System.IO.File.Move(temp, target, overwrite: true);
            moved = true;

            // From here the file HAS changed, so it is audited first; the tidying after it may fail without undoing that.
            var before = new { item.FileSize, item.Width, item.Height };
            var size = new FileInfo(target).Length;
            await _audit.LogAsync(AuditActions.MediaUpdated, "Media", id.ToString(), item.FileName,
                oldValues: System.Text.Json.JsonSerializer.Serialize(before),
                newValues: System.Text.Json.JsonSerializer.Serialize(new { FileSize = size, Width = width, Height = height, ReplacedWith = Path.GetFileName(file.FileName) }));
            TempData["Success"] = "File replaced. Pages that use it now point at the new file; a browser that has the old one saved may need a refresh.";

            // The narrower copies were made from the old picture: remove them and make new ones.
            foreach (var w in Blog.Core.Services.ResponsiveImages.VariantWidths)
                if (_responsive.FileOf(Blog.Core.Services.ResponsiveImages.VariantPath(item.Url, w)) is { } copy && System.IO.File.Exists(copy))
                    try { System.IO.File.Delete(copy); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Responsive copy {Copy} could not be deleted.", copy); }
            _responsive.Forget(item.Url);
            await _responsive.EnsureVariantsAsync(item.Url, CancellationToken.None);
            await _media.UpdateFileAsync(id, userId, size, width, height);
        }
        catch (ImageTooLargeException ex)
        {
            TempData["Error"] = ex.Message + " Nothing was replaced.";
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            TempData["Error"] = "That file is not a picture this site can read. Nothing was replaced.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Replace failed for media {Id}", id);
            if (moved) TempData["Warning"] = "The file was replaced, but its details could not be refreshed. Open it again in a moment; if the size shown is still the old one, replace it once more.";
            else TempData["Error"] = "The new file could not be stored, so the old one is still in place.";
        }
        finally
        {
            try { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); } catch { /* a leftover temp file is harmless */ }
        }
        return BackToDetails(id, folder, q, page);
    }

    [HttpGet("api")]
    public async Task<IActionResult> GetMediaApi(int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var items = await _media.GetAllAsync(userId, page, 50); // Fetch a batch for the modal
        var total = await _media.GetTotalCountAsync(userId);

        return Json(new {
            items = items.Select(m => new {
                id = m.Id,
                url = m.Url,
                fileName = m.FileName,
                originalFileName = m.OriginalFileName,
                contentType = m.ContentType,
                altText = m.AltText,
                caption = m.Caption,
                width = m.Width,
                height = m.Height
            }),
            total = total,
            page = page
        });
    }
}
