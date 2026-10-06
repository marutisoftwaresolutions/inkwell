using System.Security.Claims;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Blog.Web.Services.Backups;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Backups (ADR-0019). Queue a whole-site backup, download or delete one. A backup holds
/// every account's password hash and every subscriber's email address, so the screen is Admin-only,
/// every download is audited, and on a cloud (multi-tenant) install it does not exist at all: one
/// tenant's admin must never be able to download another tenant's data.
/// </summary>
[Authorize(Policy = "AdminOnly")]
[Route("admin/backups")]
public class BackupsController : Controller
{
    private readonly BackupService _backups;
    private readonly AuditService _audit;
    private readonly ITenantContext _tenant;

    public BackupsController(BackupService backups, AuditService audit, ITenantContext tenant)
    {
        _backups = backups;
        _audit = audit;
        _tenant = tenant;
    }

    private bool Unavailable => _tenant.IsCloudMode;

    [HttpGet("")]
    public IActionResult Index()
    {
        if (Unavailable) return NotFound();
        ViewData["Title"] = "Backups";
        return View(BuildModel());
    }

    /// <summary>The list and the queued/running state, polled by the page while a backup is on its way.</summary>
    [HttpGet("status")]
    public IActionResult Status(bool waiting = false)
    {
        if (Unavailable) return NotFound();
        var model = BuildModel();
        // Polled while waiting: the first answer after the backup lands says so, once.
        ViewData["JustFinished"] = waiting && !model.InProgress;
        return PartialView("_BackupList", model);
    }

    private BackupsViewModel BuildModel() => new(
        _backups.List(), _backups.PendingRequest(), _backups.IsRunning(), _backups.Paths.Keep);

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(bool includeAnalytics)
    {
        if (Unavailable) return NotFound();
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
        var userName = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.Email) ?? "admin";

        if (!_backups.Request(new BackupRequest(includeAnalytics, userId, userName, DateTime.UtcNow)))
        {
            TempData["Error"] = "A backup is already queued or running. It will appear in the list when it is done.";
            return RedirectToAction(nameof(Index));
        }

        await _audit.LogAsync(AuditActions.BackupRequested, "Backup", null, "Site backup",
            newValues: includeAnalytics ? "page views included" : null);
        TempData["Success"] = "Backup queued. It usually appears in the list within two minutes.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("download/{name}")]
    public async Task<IActionResult> Download(string name)
    {
        if (Unavailable) return NotFound();
        var file = _backups.Find(name);
        if (file is null) return NotFound();
        await _audit.LogAsync(AuditActions.BackupDownloaded, "Backup", name, name);
        return File(_backups.OpenRead(name), "application/zip", name);
    }

    [HttpPost("delete/{name}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string name)
    {
        if (Unavailable) return NotFound();
        if (_backups.Find(name) is null || !_backups.Delete(name))
        {
            TempData["Error"] = "That backup is no longer in the backups folder.";
            return RedirectToAction(nameof(Index));
        }
        await _audit.LogAsync(AuditActions.BackupDeleted, "Backup", name, name);
        TempData["Success"] = "Backup deleted.";
        return RedirectToAction(nameof(Index));
    }
}

public sealed record BackupsViewModel(IReadOnlyList<BackupFile> Files, BackupRequest? Pending, bool Running, int Keep)
{
    public bool InProgress => Pending is not null || Running;
}
