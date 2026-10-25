using Blog.Core.Domain;
using Blog.Core.Interfaces;
using System.Security.Claims;
using Blog.Core.Services.Export;
using Blog.Core.Services.Export.Push;
using Blog.Web.Models;
using Blog.Web.Services;
using Blog.Web.Services.Export;
using Blog.Web.Services.Export.Push;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Export (ADR-0020): download the site's content as a WordPress (WXR) file or a Ghost import
/// ZIP. The fidelity matrix for the current selection is shown before anything runs. An export is a
/// copy of the whole site's content, so the screen is Admin-only, every run is audited, and it is not
/// offered in cloud (multi-tenant) mode.
/// </summary>
[Authorize(Policy = "AdminOnly")]
[Route("admin/export")]
public class ExportController : Controller
{
    private readonly ExportService _export;
    private readonly ExportPushService _push;
    private readonly AuditService _audit;
    private readonly ITenantContext _tenant;
    private readonly ErrorLogService _errors;
    private readonly ILogger<ExportController> _log;

    public ExportController(ExportService export, ExportPushService push, AuditService audit, ITenantContext tenant, ErrorLogService errors, ILogger<ExportController> log)
    {
        _export = export;
        _push = push;
        _audit = audit;
        _tenant = tenant;
        _errors = errors;
        _log = log;
    }

    private string RequestOrigin => $"{Request.Scheme}://{Request.Host}";

    [HttpGet("")]
    public async Task<IActionResult> Index(ExportTarget target = ExportTarget.WordPress, bool? includeDrafts = null,
        bool? includePages = null, bool? includeComments = null, bool? includeEmails = null, string? siteAddress = null, bool submitted = false)
    {
        if (_tenant.IsCloudMode) return NotFound();
        // An unticked box sends nothing; once the form has been submitted, absent means off.
        var options = new ExportOptions
        {
            IncludeDrafts = includeDrafts ?? false,
            IncludePages = includePages ?? !submitted,
            IncludeComments = includeComments ?? !submitted,
            IncludeEmails = includeEmails ?? false,
        };
        var counts = await _export.CountAsync(options);
        ViewData["Title"] = "Export";
        var recent = await _push.ListRecentAsync(5);
        var recentCounts = new Dictionary<Guid, ExportPushCounts>();
        foreach (var p in recent) recentCounts[p.Id] = await _push.CountAsync(p.Id);
        return View(new ExportViewModel(target, options, counts, ExportFidelity.For(target, counts, options),
            string.IsNullOrWhiteSpace(siteAddress) ? RequestOrigin : siteAddress.Trim())
        {
            PushFidelity = ExportFidelity.ForPush(target, counts, options),
            RecentPushes = recent,
            RecentCounts = recentCounts,
            ActivePush = recent.FirstOrDefault(p => p.IsActive),
            AllowPrivateTargets = _push.AllowPrivateTargets,
        });
    }

    [HttpPost("download")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Download(ExportTarget target, bool includeDrafts, bool includePages, bool includeComments,
        bool includeEmails, string? siteAddress)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var options = new ExportOptions
        {
            IncludeDrafts = includeDrafts, IncludePages = includePages,
            IncludeComments = includeComments, IncludeEmails = includeEmails,
        };
        var back = new { target, includeDrafts, includePages, includeComments, includeEmails, siteAddress };

        var origin = RequestOrigin;
        if (!string.IsNullOrWhiteSpace(siteAddress))
        {
            if (!Uri.TryCreate(siteAddress.Trim(), UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http") || u.PathAndQuery != "/")
            {
                return Fail(400, "Enter the site address as a full web address with no path, for example https://www.example.com.", back, "siteAddress");
            }
            origin = u.GetLeftPart(UriPartial.Authority);
        }

        var summary = $"{target}; drafts {(includeDrafts ? "included" : "excluded")}; pages {(includePages ? "included" : "excluded")}; " +
                      $"comments {(includeComments && target == ExportTarget.WordPress ? "included" : "excluded")}; emails {(includeEmails ? "included" : "excluded")}";
        await _audit.LogAsync(AuditActions.ExportStarted, "Export", null, $"{target} export", newValues: summary);
        try
        {
            var file = await _export.CreateAsync(target, options, origin, HttpContext.RequestAborted);
            var r = file.Result;
            await _audit.LogAsync(AuditActions.ExportCompleted, "Export", null, file.DownloadName,
                newValues: $"{summary}; {r.Posts} posts, {r.Pages} pages, {r.Comments} comments, {r.Attachments} images" +
                           (file.MissingImages > 0 ? $", {file.MissingImages} images not found" : "") +
                           (r.Truncated > 0 ? $", {r.Truncated} values shortened" : ""));
            return File(file.Content, file.ContentType, file.DownloadName);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();   // the admin closed the page; nothing to tell them
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "{Target} export failed.", target);
            await _errors.RecordAsync(HttpContext, 500, ex);
            await _audit.LogAsync(AuditActions.ExportFailed, "Export", null, $"{target} export", newValues: summary);
            return Fail(500, "The export could not be completed. Nothing was downloaded. The Error Monitor has the details.", back, null);
        }
    }

    // ── Direct push (ADR-0029) ───────────────────────────────────────────────

    /// <summary>
    /// Checks the target site and credential, records the push and hands it to the push job. The credential is
    /// never echoed back, stored or audited: on an error the form returns with every other field filled in.
    /// </summary>
    [HttpPost("push")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Push(ExportTarget target, bool includeDrafts, bool includePages, string? pushSite,
        string? pushUser, string? pushSecret, PushConflictPolicy conflict, string? siteAddress)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var back = new { target, includeDrafts, includePages, submitted = true, siteAddress };

        var origin = RequestOrigin;
        if (!string.IsNullOrWhiteSpace(siteAddress))
        {
            if (!Uri.TryCreate(siteAddress.Trim(), UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http") || u.PathAndQuery != "/")
                return PushFailed("Enter this site's address as a full web address with no path, for example https://www.example.com.", "pushSource", back, pushSite, pushUser, conflict);
            origin = u.GetLeftPart(UriPartial.Authority);
        }

        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
        var result = await _push.StartAsync(target, pushSite, pushUser, pushSecret, conflict,
            new ExportOptions { IncludeDrafts = includeDrafts, IncludePages = includePages }, origin, userId, User.Identity?.Name, HttpContext.RequestAborted);
        if (result.Push is null)
            return PushFailed(result.Error!, result.Field, back, pushSite, pushUser, conflict);

        TempData["Success"] = $"Push started: {Items(result.Push.TotalCount)} to {result.Push.SiteUrl}. It runs in the background; you can leave this page.";
        return RedirectToAction(nameof(PushStatus), new { id = result.Push.Id });
    }

    private static string Items(int n) => n == 1 ? "1 item" : $"{n} items";

    private IActionResult PushFailed(string message, string? field, object back, string? site, string? user, PushConflictPolicy conflict)
    {
        TempData["PushError"] = message;
        TempData["PushField"] = field;
        TempData["PushSite"] = site;
        TempData["PushUser"] = user;
        TempData["PushConflict"] = conflict.ToString();
        return Redirect(Url.Action(nameof(Index), back) + "#push");
    }

    [HttpGet("push/{id:guid}")]
    public async Task<IActionResult> PushStatus(Guid id, string? show, string? q, int page = 1)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var push = await _push.GetAsync(id);
        if (push is null) return NotFound();
        var filter = show switch
        {
            "failed" => PushItemStatus.Failed,
            "skipped" => PushItemStatus.Skipped,
            "created" => PushItemStatus.Created,
            "updated" => PushItemStatus.Updated,
            "pending" => PushItemStatus.Pending,
            _ => (PushItemStatus?)null,
        };
        // SQL paging: a push has a row per post and page, so a large site would otherwise load them all per view.
        const int pageSize = 25;
        var total = await _push.CountItemsAsync(id, filter, q);
        var meta = PagingMeta.FromCounts(q, page, total, pageSize, 0);
        var rows = await _push.SearchItemsAsync(id, filter, q, meta.Page, pageSize);
        ListPaging.Stash(this, meta with { Shown = rows.Count });
        ViewData["Title"] = "Push";
        return View("Push", new PushStatusViewModel(push, await _push.CountAsync(id), rows, show));
    }

    /// <summary>The progress panel, polled by the status page while the push runs.</summary>
    [HttpGet("push/{id:guid}/progress")]
    public async Task<IActionResult> PushProgress(Guid id)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var push = await _push.GetAsync(id);
        if (push is null) return NotFound();
        // Polling runs only while the push runs: once it has stopped, reload the page so its buttons and rows match.
        if (push.Status != Blog.Core.Services.Export.Push.PushStatus.Running && Request.Headers.ContainsKey("HX-Request")) Response.Headers["HX-Refresh"] = "true";
        return PartialView("_PushProgress", new PushStatusViewModel(push, await _push.CountAsync(id), Array.Empty<ExportPushItem>(), null));
    }

    [HttpPost("push/{id:guid}/resume")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResumePush(Guid id, string? pushUser, string? pushSecret)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var result = await _push.ResumeAsync(id, pushUser, pushSecret, HttpContext.RequestAborted);
        if (result.Push is null)
        {
            TempData["PushError"] = result.Error;
            TempData["PushField"] = result.Field;
            TempData["PushUser"] = pushUser;
        }
        else TempData["Success"] = "Push resumed. It continues from the first item not yet sent.";
        return RedirectToAction(nameof(PushStatus), new { id });
    }

    [HttpPost("push/{id:guid}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelPush(Guid id)
    {
        if (_tenant.IsCloudMode) return NotFound();
        if (await _push.CancelAsync(id)) TempData["Success"] = "Push cancelled. Items already sent stay on the other site.";
        else TempData["Error"] = "This push has already finished, so there is nothing to cancel.";
        return RedirectToAction(nameof(PushStatus), new { id });
    }

    /// <summary>
    /// The page downloads through fetch() (header <c>X-Requested-With: fetch</c>), which would follow a
    /// redirect and lose the message; it gets the message as plain text instead, and the field it concerns.
    /// Without JavaScript the form posts normally and the message comes back through the redirect.
    /// </summary>
    private IActionResult Fail(int status, string message, object back, string? field)
    {
        if (Request.Headers.XRequestedWith == "fetch")
        {
            if (field is not null) Response.Headers["X-Field"] = field;
            return new ContentResult { StatusCode = status, Content = message, ContentType = "text/plain; charset=utf-8" };
        }
        TempData["Error"] = message;
        return RedirectToAction(nameof(Index), back);
    }
}

public sealed record ExportViewModel(ExportTarget Target, ExportOptions Options, ExportCounts Counts,
    IReadOnlyList<FidelityRow> Fidelity, string SiteAddress)
{
    public IReadOnlyList<FidelityRow> PushFidelity { get; init; } = Array.Empty<FidelityRow>();
    public IReadOnlyList<ExportPush> RecentPushes { get; init; } = Array.Empty<ExportPush>();
    public IReadOnlyDictionary<Guid, ExportPushCounts> RecentCounts { get; init; } = new Dictionary<Guid, ExportPushCounts>();
    public ExportPush? ActivePush { get; init; }
    public bool AllowPrivateTargets { get; init; }
}

public sealed record PushStatusViewModel(ExportPush Push, ExportPushCounts Counts, IReadOnlyList<ExportPushItem> Items, string? Show);
