using Blog.Core.Interfaces;
using Blog.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

public class ErrorController : Controller
{
    private readonly ISettingRepository _settings;
    private readonly ITenantContext _tenantContext;
    private readonly ErrorLogService _errorLog;
    private readonly IPostRepository _posts;

    public ErrorController(ISettingRepository settings, ITenantContext tenantContext, ErrorLogService errorLog, IPostRepository posts)
    {
        _settings = settings;
        _tenantContext = tenantContext;
        _errorLog = errorLog;
        _posts = posts;
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
                // A dead end strands the reader: echo what they asked for (so a typo is obvious),
                // offer search, and show what other readers open most. Fail-open — an empty list
                // just hides the section.
                ViewBag.RequestedPath = HttpContext.Request.Path.Value ?? "/";
                try
                {
                    var recent = await _posts.GetPostsAsync(new PostFilter
                    {
                        Status = Blog.Core.Domain.PostStatus.Published,
                        AuthorId = userId == Guid.Empty ? null : userId,
                        Page = 1, PageSize = 24
                    });
                    ViewBag.PopularPosts = recent.Items.OrderByDescending(p => p.ViewCount).Take(5).ToList();
                }
                catch { ViewBag.PopularPosts = new List<Blog.Core.Domain.Post>(); }
                return View("NotFound");
            case 410:
                // Deliberately retired URL (e.g. a legacy product route). Distinct from 404 so the
                // page can explain the removal and point at what replaced it.
                ViewData["Title"] = "Page No Longer Available";
                return View("Gone");
            default:
                ViewData["Title"] = "An Error Occurred";
                return View("GenericError");
        }
    }
}

