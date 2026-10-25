using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Models;
using Blog.Web.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

namespace Blog.Web.Controllers;

[Authorize(Policy = "AdminOnly")]
[Route("admin/analytics")]
public class AnalyticsController : Controller
{
    private readonly IPageViewRepository _pageViews;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenant;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AnalyticsController> _logger;

    /// <summary>How long one report stays cached. Views arrive in batches anyway, so a few minutes of
    /// staleness costs nothing, and switching tabs or exporting does not re-read the table.</summary>
    private static readonly TimeSpan ReportCacheFor = TimeSpan.FromMinutes(5);

    public AnalyticsController(IPageViewRepository pageViews, IUserRepository users, ITenantContext tenant,
        IMemoryCache cache, ILogger<AnalyticsController> logger)
    {
        _pageViews = pageViews;
        _users = users;
        _tenant = tenant;
        _cache = cache;
        _logger = logger;
    }

    private Task<Guid> GetOwnerIdAsync()
    {
        // Cloud mode: use tenant user ID.
        // Self-hosted: PageViewMiddleware records views with Guid.Empty — match that.
        if (_tenant.IsCloudMode && _tenant.IsResolved) return Task.FromResult(_tenant.UserId);
        return Task.FromResult(Guid.Empty);
    }

    private (DateTime From, DateTime To, string Period) ParseRange(string? period, string? from, string? to)
    {
        var now = DateTime.UtcNow.Date;
        return period switch
        {
            "today" => (now, now.AddDays(1), "today"),
            "7d"    => (now.AddDays(-6), now.AddDays(1), "7d"),
            "90d"   => (now.AddDays(-89), now.AddDays(1), "90d"),
            "custom" when DateTime.TryParse(from, out var f) && DateTime.TryParse(to, out var t)
                    => (f.Date, t.Date.AddDays(1), "custom"),
            _       => (now.AddDays(-29), now.AddDays(1), "30d")
        };
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? period, string? from, string? to)
    {
        var ownerId = await GetOwnerIdAsync();
        var (dateFrom, dateTo, p) = ParseRange(period, from, to);

        var report = await TryLoadReportAsync(ownerId, dateFrom, dateTo, 20);
        if (report is null)
            TempData["Error"] = "Analytics could not be loaded in time. The figures below are empty; try again in a minute or pick a shorter period.";
        var (total, daily, sources, topPages, referrers, campaigns, countries, regions) = Shape(report ?? new AnalyticsReport());

        var model = BuildViewModel(dateFrom, dateTo, p, total, daily, sources, topPages, referrers, campaigns, countries, regions);
        ViewData["Title"] = "Analytics";
        return View(model);
    }

    [HttpGet("export/excel")]
    public async Task<IActionResult> ExportExcel(string? period, string? from, string? to)
    {
        var ownerId = await GetOwnerIdAsync();
        var (dateFrom, dateTo, p) = ParseRange(period, from, to);
        var (total, daily, sources, topPages, referrers, campaigns, countries, regions) = Shape(await LoadReportAsync(ownerId, dateFrom, dateTo, 20));

        using var wb = new XLWorkbook();

        // Sheet 1: Summary
        var wsSummary = wb.Worksheets.Add("Summary");
        wsSummary.Cell(1, 1).Value = "Period";
        wsSummary.Cell(1, 2).Value = $"{dateFrom:yyyy-MM-dd} to {dateTo.AddDays(-1):yyyy-MM-dd}";
        wsSummary.Cell(2, 1).Value = "Total Views";  wsSummary.Cell(2, 2).Value = total;
        wsSummary.Cell(3, 1).Value = "Unique Pages"; wsSummary.Cell(3, 2).Value = topPages.Count;
        wsSummary.Column(1).Width = 20; wsSummary.Column(2).Width = 30;

        // Sheet 2: Daily Views
        var wsDaily = wb.Worksheets.Add("Daily Views");
        wsDaily.Cell(1, 1).Value = "Date"; wsDaily.Cell(1, 2).Value = "Views";
        wsDaily.Row(1).Style.Font.Bold = true;
        for (int i = 0; i < daily.Count; i++)
        {
            wsDaily.Cell(i + 2, 1).Value = daily[i].Date;
            wsDaily.Cell(i + 2, 2).Value = daily[i].Count;
        }
        wsDaily.Columns().AdjustToContents();

        // Sheet 3: Traffic Sources
        var wsSources = wb.Worksheets.Add("Traffic Sources");
        wsSources.Cell(1, 1).Value = "Source"; wsSources.Cell(1, 2).Value = "Medium"; wsSources.Cell(1, 3).Value = "Views";
        wsSources.Row(1).Style.Font.Bold = true;
        for (int i = 0; i < sources.Count; i++)
        {
            wsSources.Cell(i + 2, 1).Value = sources[i].Source;
            wsSources.Cell(i + 2, 2).Value = sources[i].Medium;
            wsSources.Cell(i + 2, 3).Value = sources[i].Count;
        }
        wsSources.Columns().AdjustToContents();

        // Sheet 4: Top Pages
        var wsPages = wb.Worksheets.Add("Top Pages");
        wsPages.Cell(1, 1).Value = "Page"; wsPages.Cell(1, 2).Value = "Views";
        wsPages.Row(1).Style.Font.Bold = true;
        for (int i = 0; i < topPages.Count; i++)
        {
            wsPages.Cell(i + 2, 1).Value = topPages[i].Path;
            wsPages.Cell(i + 2, 2).Value = topPages[i].Views;
        }
        wsPages.Columns().AdjustToContents();

        // Sheet 5: Referrers
        var wsRef = wb.Worksheets.Add("Referrers");
        wsRef.Cell(1, 1).Value = "Referrer"; wsRef.Cell(1, 2).Value = "Visits";
        wsRef.Row(1).Style.Font.Bold = true;
        for (int i = 0; i < referrers.Count; i++)
        {
            wsRef.Cell(i + 2, 1).Value = referrers[i].Referrer;
            wsRef.Cell(i + 2, 2).Value = referrers[i].Count;
        }
        wsRef.Columns().AdjustToContents();

        // Sheet 6: Campaigns
        if (campaigns.Count > 0)
        {
            var wsCamp = wb.Worksheets.Add("Campaigns");
            wsCamp.Cell(1, 1).Value = "Campaign"; wsCamp.Cell(1, 2).Value = "Visits";
            wsCamp.Row(1).Style.Font.Bold = true;
            for (int i = 0; i < campaigns.Count; i++)
            {
                wsCamp.Cell(i + 2, 1).Value = campaigns[i].Campaign;
                wsCamp.Cell(i + 2, 2).Value = campaigns[i].Count;
            }
            wsCamp.Columns().AdjustToContents();
        }

        // Sheet 7: Countries
        if (countries.Count > 0)
        {
            var wsCountry = wb.Worksheets.Add("Countries");
            wsCountry.Cell(1, 1).Value = "Country"; wsCountry.Cell(1, 2).Value = "Visits";
            wsCountry.Row(1).Style.Font.Bold = true;
            for (int i = 0; i < countries.Count; i++)
            {
                wsCountry.Cell(i + 2, 1).Value = countries[i].Country;
                wsCountry.Cell(i + 2, 2).Value = countries[i].Count;
            }
            wsCountry.Columns().AdjustToContents();
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        var fileName = $"analytics-{dateFrom:yyyy-MM-dd}-to-{dateTo.AddDays(-1):yyyy-MM-dd}.xlsx";
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv(string? period, string? from, string? to, string? sheet = "pages")
    {
        var ownerId = await GetOwnerIdAsync();
        var (dateFrom, dateTo, _) = ParseRange(period, from, to);
        var report = await LoadReportAsync(ownerId, dateFrom, dateTo, 100);
        var sb = new StringBuilder();

        if (sheet == "sources")
        {
            sb.AppendLine("Source,Medium,Views");
            foreach (var s in report.Sources) sb.AppendLine($"\"{s.Source}\",\"{s.Medium}\",{s.Count}");
        }
        else if (sheet == "daily")
        {
            sb.AppendLine("Date,Views");
            foreach (var d in report.Daily) sb.AppendLine($"{d.Date:yyyy-MM-dd},{d.Count}");
        }
        else if (sheet == "referrers")
        {
            sb.AppendLine("Referrer,Visits");
            foreach (var r in report.Referrers) sb.AppendLine($"\"{r.Referrer}\",{r.Count}");
        }
        else if (sheet == "countries")
        {
            sb.AppendLine("Country,Visits");
            foreach (var c in report.Countries) sb.AppendLine($"\"{c.Country}\",{c.Count}");
        }
        else // pages (default)
        {
            sb.AppendLine("Page,Views");
            foreach (var pg in report.TopPages) sb.AppendLine($"\"{pg.Path}\",{pg.Views}");
        }

        var fileName = $"analytics-{sheet}-{dateFrom:yyyy-MM-dd}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>One stored-procedure call per owner and window, cached briefly. A failure is never cached.</summary>
    private Task<AnalyticsReport> LoadReportAsync(Guid ownerId, DateTime from, DateTime to, int top) =>
        _cache.GetOrCreateAsync($"analytics:{ownerId:N}:{from:yyyyMMdd}:{to:yyyyMMdd}:{top}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ReportCacheFor;
            return _pageViews.GetReportAsync(ownerId, from, to, top);
        })!;

    /// <summary>
    /// The page is optional (architecture rule: fail open): a slow or unreachable database shows an empty
    /// report with a message instead of a server error. Exports keep throwing — an empty file would mislead.
    /// </summary>
    private async Task<AnalyticsReport?> TryLoadReportAsync(Guid ownerId, DateTime from, DateTime to, int top)
    {
        try { return await LoadReportAsync(ownerId, from, to, top); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Analytics report failed for {From:yyyy-MM-dd}..{To:yyyy-MM-dd}.", from, to);
            return null;
        }
    }

    private static (int total,
        List<DailyViewCount> daily,
        List<SourceCount> sources,
        List<PageViewCount> topPages,
        List<ReferrerCount> referrers,
        List<CampaignCount> campaigns,
        List<CountryCount> countries,
        List<RegionCount> regions) Shape(AnalyticsReport r)
    {
        var daily     = r.Daily.Select(d => new DailyViewCount(d.Date.ToString("yyyy-MM-dd"), d.Count)).ToList();
        var sources   = r.Sources.Select(s => new SourceCount(
            VisitSourceClassifier.GetDisplayLabel(s.Source, s.Medium), s.Source, s.Medium, s.Count)).ToList();
        var pages     = r.TopPages.Select(p => new PageViewCount(p.Path, p.Views)).ToList();
        var referrers = r.Referrers.Select(x => new ReferrerCount(x.Referrer, x.Count)).ToList();
        var campaigns = r.Campaigns.Select(c => new CampaignCount(c.Campaign, c.Count)).ToList();
        var countries = r.Countries.Select(c => new CountryCount(c.Country, c.Count)).ToList();
        var regions   = r.Regions.Select(x => new RegionCount(x.Region, x.Count)).ToList();

        return (r.Total, daily, sources, pages, referrers, campaigns, countries, regions);
    }

    private static AnalyticsDashboardViewModel BuildViewModel(
        DateTime dateFrom, DateTime dateTo, string period,
        int total,
        List<DailyViewCount> daily,
        List<SourceCount> sources,
        List<PageViewCount> topPages,
        List<ReferrerCount> referrers,
        List<CampaignCount> campaigns,
        List<CountryCount> countries,
        List<RegionCount> regions)
    {
        var topSource = sources.FirstOrDefault()?.Label ?? "—";
        return new AnalyticsDashboardViewModel
        {
            From            = dateFrom,
            To              = dateTo.AddDays(-1),
            Period          = period,
            TotalViews      = total,
            UniquePages     = topPages.Count,
            TopSource       = topSource,
            TotalReferrers  = referrers.Count,
            DailyCounts     = daily,
            SourceBreakdown = sources,
            TopPages        = topPages,
            TopReferrers    = referrers,
            Campaigns       = campaigns,
            TopCountries    = countries,
            TopRegions      = regions
        };
    }
}
