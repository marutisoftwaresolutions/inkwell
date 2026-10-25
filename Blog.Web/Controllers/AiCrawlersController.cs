using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → AI Crawlers. Shows which AI answer engines and search crawlers are actually reading the
/// site, and which pages they fetch — the feedback loop that makes AEO work measurable rather than
/// assumed. Search Console reports generative-AI impressions without naming the engine; this names it.
/// </summary>
[Authorize(Policy = "EditorOrAbove")]
[Route("admin/ai-crawlers")]
public class AiCrawlersController : Controller
{
    private readonly ICrawlerVisitRepository _visits;
    private readonly ITenantContext _tenant;
    private readonly ISettingRepository _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AiCrawlersController> _logger;

    /// <summary>How long one report stays cached: crawler visits are written in batches, and re-reading
    /// millions of rows because someone switched from 30 to 7 days and back buys nothing.</summary>
    private static readonly TimeSpan ReportCacheFor = TimeSpan.FromMinutes(5);

    public AiCrawlersController(ICrawlerVisitRepository visits, ITenantContext tenant, ISettingRepository settings,
        IMemoryCache cache, ILogger<AiCrawlersController> logger)
    {
        _visits = visits;
        _tenant = tenant;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var ownerId = _tenant.IsCloudMode && _tenant.IsResolved ? _tenant.UserId : (Guid?)null;

        var report   = await TryLoadReportAsync(ownerId, days);
        var settings = await _settings.GetSettingsAsync(ownerId ?? Guid.Empty);
        if (report is null)
            TempData["Error"] = "Crawler activity could not be loaded in time. The figures below are empty; try again in a minute or pick a shorter window.";
        report ??= new CrawlerReport();

        // Every crawler we can name is listed, including those with no visits — an engine that has
        // never fetched the site is a finding, not an omission.
        var seen = report.Activity.ToDictionary(a => a.Crawler, StringComparer.OrdinalIgnoreCase);
        var full = CrawlerIdentifier.Known
            .Select(k => seen.TryGetValue(k.Name, out var a)
                ? Fill(a, k)
                : new CrawlerActivity { Crawler = k.Name, Operator = k.Operator, IsAi = k.IsAi, Purpose = k.Purpose })
            .OrderByDescending(a => a.Visits)
            .ThenBy(a => a.Crawler, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ViewBag.Days      = days;
        ViewBag.Ai        = full.Where(a => a.IsAi).ToList();
        ViewBag.Search    = full.Where(a => !a.IsAi).ToList();
        ViewBag.TopPages  = report.TopPages;
        ViewBag.Queried   = report.Queried;
        ViewBag.Daily     = FillGaps(report.Daily, days);
        // Headline figures leave out visits that failed the operator's network check: those are someone
        // else using the name, not the engine reading the site. The table still shows them.
        ViewBag.AiVisits  = full.Where(a => a.IsAi).Sum(a => a.Visits - a.FailedVisits);
        ViewBag.AiEngines = full.Count(a => a.IsAi && a.Visits - a.FailedVisits > 0);
        ViewBag.RetentionDays = Blog.Web.Services.Jobs.CrawlerVisitRetentionJob.ClampDays(
            settings.CrawlerVisitRetentionDays);

        ViewData["Title"] = "AI Crawlers";
        return View();
    }

    /// <summary>One stored-procedure call per owner and window, cached briefly. A failure is never cached.</summary>
    private async Task<CrawlerReport?> TryLoadReportAsync(Guid? ownerId, int days)
    {
        try
        {
            return await _cache.GetOrCreateAsync($"ai-crawlers:{ownerId?.ToString("N") ?? "all"}:{days}", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = ReportCacheFor;
                return _visits.GetReportAsync(ownerId, days);
            });
        }
        catch (Exception ex)
        {
            // Optional page (architecture rule: fail open) — a slow database shows a message, not a 500.
            _logger.LogWarning(ex, "AI Crawlers report failed for a {Days}-day window.", days);
            return null;
        }
    }

    /// <summary>
    /// Days with no crawler visit are absent from the query. Left as-is, the chart would close the
    /// gap and read as steady attention — a quiet week has to look quiet.
    /// </summary>
    private static IReadOnlyList<CrawlerDay> FillGaps(IReadOnlyList<CrawlerDay> rows, int days)
    {
        var byDay = rows
            .GroupBy(r => r.Day.Date)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Visits));
        var start = DateTime.UtcNow.Date.AddDays(-(days - 1));

        return Enumerable.Range(0, days)
            .Select(i => start.AddDays(i))
            .Select(d => new CrawlerDay { Day = d, Visits = byDay.TryGetValue(d, out var v) ? v : 0 })
            .ToList();
    }

    private static CrawlerActivity Fill(CrawlerActivity a, CrawlerIdentity k)
    {
        a.Purpose = k.Purpose;
        a.Operator = k.Operator;
        a.IsAi = k.IsAi;
        return a;
    }
}
