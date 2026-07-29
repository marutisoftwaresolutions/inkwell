using Blog.Core.Interfaces;
using Blog.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

public class ErrorController : Controller
{
    private readonly ISettingRepository _settings;
    private readonly ITenantContext _tenantContext;
    private readonly ErrorLogService _errorLog;

    public ErrorController(ISettingRepository settings, ITenantContext tenantContext, ErrorLogService errorLog)
    {
        _settings = settings;
        _tenantContext = tenantContext;
        _errorLog = errorLog;
    }

    [Route("error/{statusCode}")]
    public async Task<IActionResult> HttpStatusCodeHandler(int statusCode)
    {
        // Log 4xx here (missing resources etc.). 5xx are logged with full exception detail by the
        // exception-logging middleware, so skip them here to avoid double-counting.
        if (statusCode >= 400 && statusCode < 500)
        {
            var reExec = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodeReExecuteFeature>();
            var originalPath = reExec?.OriginalPath ?? HttpContext.Request.Path.Value ?? "/";
            // Rebuild a context-ish record using the original (pre-reexecute) path.
            HttpContext.Request.Path = originalPath;
            await _errorLog.RecordAsync(HttpContext, statusCode);
        }

        var userId = _tenantContext.IsResolved ? _tenantContext.UserId : Guid.Empty;
        var userSettings = await _settings.GetSettingsAsync(userId);
        ViewBag.SiteName = userSettings.SiteName;
        ViewBag.Tagline = userSettings.SiteDescription;

        switch (statusCode)
        {
            case 404:
                ViewData["Title"] = "Page Not Found";
                return View("NotFound");
            default:
                ViewData["Title"] = "An Error Occurred";
                return View("GenericError");
        }
    }
}

