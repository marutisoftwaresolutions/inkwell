using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Calendar (v1.0.9; MARKET-RESEARCH G14): a month at a glance of what was published, what is scheduled and
/// which drafts carry a date. Read-only; every entry links to its editor, where dates are changed.
/// </summary>
[Authorize(Policy = "CanEditPosts")]
[Route("admin/calendar")]
public class CalendarController : Controller
{
    private readonly IPostRepository _posts;
    private readonly ITenantContext _tenant;
    private readonly Blog.Web.Services.SiteDates _dates;

    public CalendarController(IPostRepository posts, ITenantContext tenant, Blog.Web.Services.SiteDates dates)
    {
        _posts = posts;
        _tenant = tenant;
        _dates = dates;
    }

    /// <param name="month">yyyy-MM; the current month in the site's time zone when missing or not a month.</param>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? month)
    {
        var zone = _dates.Options.TimeZoneId;
        var (year, monthNumber) = ContentCalendar.ParseMonth(month, DateTime.UtcNow, zone);
        var (fromUtc, toUtc) = ContentCalendar.QueryRangeUtc(year, monthNumber);
        // Self-hosted shows the whole site; cloud mode only the signed-in tenant's posts (as Content Health does).
        var ownerId = _tenant.IsCloudMode && _tenant.IsResolved ? _tenant.UserId : (Guid?)null;
        var posts = await _posts.GetCalendarAsync(fromUtc, toUtc, ownerId);

        ViewData["Title"] = "Calendar";
        return View(ContentCalendar.Build(year, monthNumber, posts, zone, DateTime.UtcNow));
    }
}
