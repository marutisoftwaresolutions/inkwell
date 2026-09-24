using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    public AiCrawlersController(ICrawlerVisitRepository visits, ITenantContext tenant, ISettingRepository settings)
    {
        _visits = visits;
        _tenant = tenant;
        _settings = settings;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var ownerId = _tenant.IsCloudMode && _tenant.IsResolved ? _tenant.UserId : (Guid?)null;

        var activityTask  = _visits.GetActivityAsync(ownerId, days);
        var topPagesTask  = _visits.GetTopPagesAsync(ownerId, days);
        var dailyTask     = _visits.GetDailyAiVisitsAsync(ownerId, days);
        var settingsTask  = _settings.GetSettingsAsync(ownerId ?? Guid.Empty);
        await Task.WhenAll(activityTask, topPagesTask, dailyTask, settingsTask);

        // Every crawler we can name is listed, including those with no visits — an engine that has
        // never fetched the site is a finding, not an omission.
        var seen = activityTask.Result.ToDictionary(a => a.Crawler, StringComparer.OrdinalIgnoreCase);
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
        ViewBag.TopPages  = topPagesTask.Result;
        ViewBag.Daily     = FillGaps(dailyTask.Result, days);
        ViewBag.AiVisits  = full.Where(a => a.IsAi).Sum(a => a.Visits);
        ViewBag.AiEngines = full.Count(a => a.IsAi && a.Visits > 0);
        ViewBag.RetentionDays = Blog.Web.Services.Jobs.CrawlerVisitRetentionJob.ClampDays(
            settingsTask.Result.CrawlerVisitRetentionDays);

        ViewData["Title"] = "AI Crawlers";
        return View();
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
