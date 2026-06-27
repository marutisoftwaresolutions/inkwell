using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.IO;

namespace Blog.Web.Controllers;

[Authorize(Policy = "CanManageMedia")]
[Route("admin/[controller]")]
public class MediaController : Controller
{
    private readonly IMediaRepository _media;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<MediaController> _logger;
    private readonly AuditService _audit;
    private static readonly HashSet<string> AllowedMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg","image/png","image/gif","image/webp","image/svg+xml",
        "video/mp4","video/webm",
        "application/pdf",
        "application/msword","application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    public MediaController(IMediaRepository media, IWebHostEnvironment env, ILogger<MediaController> logger, AuditService audit)
    {
        _media = media;
        _env = env;
        _logger = logger;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var items = await _media.GetAllAsync(userId, page, 48);
        ViewBag.Total = await _media.GetTotalCountAsync(userId);
        ViewBag.Page = page;
        return View(items);
    }

    [HttpPost("upload")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 104857600, ValueLengthLimit = 104857600)]
    //[ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, [FromForm] string folder = "images")
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided." });

            if (file.Length > 10 * 1024 * 1024)
                return BadRequest(new { error = "File exceeds 10MB limit." });

            var mime = file.ContentType;
            if (!AllowedMimes.Contains(mime))
                return BadRequest(new { error = "File type not allowed." });

            // Save to the "uploads" folder inside the standard wwwroot
            // Note: Hot Reload is ignored for this folder via Blog.Web.csproj <Watch Remove="wwwroot\uploads\**" />
            var uploadsRoot = Path.Combine(_env.WebRootPath, "uploads");
            // Normalize slashes for Windows so Directory.CreateDirectory doesn't choke on mixed slashes
            // Ensure folder is safe
            var safeFolder = folder.ToLowerInvariant() switch {
                "og" => "og",
                "twitter" => "twitter",
                _ => "images"
            };

            var relativeMonthDir = DateTime.UtcNow.ToString("yyyy-MM"); 
            var uploadsDir = Path.Combine(uploadsRoot, safeFolder, relativeMonthDir);
            Directory.CreateDirectory(uploadsDir);

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var fileName = $"{Guid.NewGuid():N}";
            int? width = null, height = null;

            fileName += ext;
            var destPath = Path.Combine(uploadsDir, fileName);
            using (var stream = new FileStream(destPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var relativePath = $"/uploads/{safeFolder}/{relativeMonthDir}/{fileName}";
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
                FileSize = file.Length,
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
            _logger.LogError(ex, "UPLOAD ERROR");
            return StatusCode(500, new { error = ex.Message, details = ex.ToString() });
        }
    }

    [HttpPost("delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
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
        return RedirectToAction("Index");
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
