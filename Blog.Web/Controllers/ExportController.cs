using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services.Export;
using Blog.Web.Services;
using Blog.Web.Services.Export;
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
    private readonly AuditService _audit;
    private readonly ITenantContext _tenant;
    private readonly ErrorLogService _errors;
    private readonly ILogger<ExportController> _log;

    public ExportController(ExportService export, AuditService audit, ITenantContext tenant, ErrorLogService errors, ILogger<ExportController> log)
    {
        _export = export;
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
        return View(new ExportViewModel(target, options, counts, ExportFidelity.For(target, counts, options),
            string.IsNullOrWhiteSpace(siteAddress) ? RequestOrigin : siteAddress.Trim()));
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
    IReadOnlyList<FidelityRow> Fidelity, string SiteAddress);
