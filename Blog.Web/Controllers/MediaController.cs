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
        AuditService audit, IImageProcessingService imageProcessing)
    {
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
            // content importer uses. Non-image types and SVG/WebP pass through unchanged.
            var safeFolder = NormalizeFolder(folder, "images");
            var relativeMonthDir = DateTime.UtcNow.ToString("yyyy-MM");
            var subdir = $"{safeFolder}/{relativeMonthDir}";

            var saved = await _imageProcessing.SaveImageAsync(file.OpenReadStream(), file.FileName, mime, subdir, convertToWebp: true);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Media upload failed");
            // The reason reaches the operator; the stack trace stays in the log.
            return StatusCode(500, new { error = "The upload failed on the server: " + ex.Message });
        }
    }

    [HttpPost("delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, [FromForm] string? folder, [FromForm] string? q, [FromForm] int page = 1)
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

        // Back to the same folder tab, search and page the user deleted from.
        var folderKey = NormalizeFolder(folder);
        return RedirectToAction("Index", new
        {
            folder = folderKey.Length > 0 ? folderKey : null,
            q = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            page = page > 1 ? page : (int?)null
        });
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
                contentType = m.ContentType
            }),
            total = total,
            page = page
        });
    }
}
