using Blog.Core.Interfaces;
using Blog.Web.Models;
using Blog.Web.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Blog.Web.Controllers;

[Authorize(Policy = "AdminOnly")]
[Route("admin/analytics")]
public class AnalyticsController : Controller
{
    private readonly IPageViewRepository _pageViews;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenant;

    public AnalyticsController(IPageViewRepository pageViews, IUserRepository users, ITenantContext tenant)
    {
        _pageViews = pageViews;
        _users = users;
        _tenant = tenant;
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

        var (total, daily, sources, topPages, referrers, campaigns, countries, regions) = await LoadDataAsync(ownerId, dateFrom, dateTo);

        var model = BuildViewModel(dateFrom, dateTo, p, total, daily, sources, topPages, referrers, campaigns, countries, regions);
        ViewData["Title"] = "Analytics";
        return View(model);
    }

    [HttpGet("export/excel")]
    public async Task<IActionResult> ExportExcel(string? period, string? from, string? to)
    {
        var ownerId = await GetOwnerIdAsync();
        var (dateFrom, dateTo, p) = ParseRange(period, from, to);
        var (total, daily, sources, topPages, referrers, campaigns, countries, regions) = await LoadDataAsync(ownerId, dateFrom, dateTo);

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
        var sb = new StringBuilder();

        if (sheet == "sources")
        {
            sb.AppendLine("Source,Medium,Views");
            var sources = await _pageViews.GetSourceBreakdownAsync(ownerId, dateFrom, dateTo);
            foreach (var s in sources) sb.AppendLine($"\"{s.Source}\",\"{s.Medium}\",{s.Count}");
        }
        else if (sheet == "daily")
        {
            sb.AppendLine("Date,Views");
            var daily = await _pageViews.GetDailyAsync(ownerId, dateFrom, dateTo);
            foreach (var d in daily) sb.AppendLine($"{d.Date:yyyy-MM-dd},{d.Count}");
        }
        else if (sheet == "referrers")
        {
            sb.AppendLine("Referrer,Visits");
            var refs = await _pageViews.GetTopReferrersAsync(ownerId, dateFrom, dateTo, 100);
            foreach (var r in refs) sb.AppendLine($"\"{r.Referrer}\",{r.Count}");
        }
        else if (sheet == "countries")
        {
            sb.AppendLine("Country,Visits");
            var ctry = await _pageViews.GetCountryBreakdownAsync(ownerId, dateFrom, dateTo, 100);
            foreach (var c in ctry) sb.AppendLine($"\"{c.Country}\",{c.Count}");
        }
        else // pages (default)
        {
            sb.AppendLine("Page,Views");
            var pages = await _pageViews.GetTopPagesAsync(ownerId, dateFrom, dateTo, 100);
            foreach (var pg in pages) sb.AppendLine($"\"{pg.Path}\",{pg.Views}");
        }

        var fileName = $"analytics-{sheet}-{dateFrom:yyyy-MM-dd}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<(int total,
        List<DailyViewCount> daily,
        List<SourceCount> sources,
        List<PageViewCount> topPages,
        List<ReferrerCount> referrers,
        List<CampaignCount> campaigns,
        List<CountryCount> countries,
        List<RegionCount> regions)> LoadDataAsync(Guid ownerId, DateTime from, DateTime to)
    {
        var totalTask     = _pageViews.GetTotalAsync(ownerId, from, to);
        var dailyTask     = _pageViews.GetDailyAsync(ownerId, from, to);
        var sourcesTask   = _pageViews.GetSourceBreakdownAsync(ownerId, from, to);
        var pagesTask     = _pageViews.GetTopPagesAsync(ownerId, from, to, 20);
        var referrersTask = _pageViews.GetTopReferrersAsync(ownerId, from, to, 20);
        var campTask      = _pageViews.GetCampaignsAsync(ownerId, from, to);
        var countryTask   = _pageViews.GetCountryBreakdownAsync(ownerId, from, to, 20);
        var regionTask    = _pageViews.GetRegionBreakdownAsync(ownerId, from, to, 20);

        await Task.WhenAll(totalTask, dailyTask, sourcesTask, pagesTask, referrersTask, campTask, countryTask, regionTask);

        var daily     = dailyTask.Result.Select(d => new DailyViewCount(d.Date.ToString("yyyy-MM-dd"), d.Count)).ToList();
        var sources   = sourcesTask.Result.Select(s => new SourceCount(
            VisitSourceClassifier.GetDisplayLabel(s.Source, s.Medium), s.Source, s.Medium, s.Count)).ToList();
        var pages     = pagesTask.Result.Select(p => new PageViewCount(p.Path, p.Views)).ToList();
        var referrers = referrersTask.Result.Select(r => new ReferrerCount(r.Referrer, r.Count)).ToList();
        var campaigns = campTask.Result.Select(c => new CampaignCount(c.Campaign, c.Count)).ToList();
        var countries = countryTask.Result.Select(c => new CountryCount(c.Country, c.Count)).ToList();
        var regions   = regionTask.Result.Select(r => new RegionCount(r.Region, r.Count)).ToList();

        return (totalTask.Result, daily, sources, pages, referrers, campaigns, countries, regions);
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
