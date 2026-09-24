using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Content Health. Turns the freshness and AEO fields that already exist on every post
/// (<c>LastVerifiedAt</c>, <c>NextReviewAt</c>, Key Facts, FAQ, meta description) into a worklist,
/// so maintenance is something the operator can see and schedule rather than rediscover by audit.
/// Read-only: it points at the editor, it never edits.
/// </summary>
[Authorize(Policy = "EditorOrAbove")]
[Route("admin/content-health")]
public class ContentHealthController : Controller
{
    private readonly IContentHealthRepository _health;
    private readonly ITenantContext _tenant;
    private readonly Blog.Web.Services.SearchConsole.SearchPerformanceService _search;

    public ContentHealthController(IContentHealthRepository health, ITenantContext tenant,
        Blog.Web.Services.SearchConsole.SearchPerformanceService search)
    {
        _health = health;
        _tenant = tenant;
        _search = search;
    }

    /// <param name="filter">
    /// attention (default) · overdue · duesoon · unverified · nokeyfacts · nofaq · meta · staleyear · weakaeo ·
    /// striking · losing · lowctr · all
    /// </param>
    /// <param name="q">Search across post title and slug.</param>
    /// <param name="page">1-based page within the filtered, searched list.</param>
    [HttpGet("")]
    public async Task<IActionResult> Index(string filter = "attention", string? q = null, int page = 1)
    {
        // Self-hosted keeps everything under one owner; cloud scopes to the signed-in tenant.
        var ownerId = _tenant.IsCloudMode && _tenant.IsResolved ? _tenant.UserId : (Guid?)null;
        var all = await _health.GetPublishedAsync(ownerId);

        // Search Console, when connected: attach each post's 28-day summary by slug. Read-only and
        // fail-open — an unreachable table leaves the maintenance view exactly as it was.
        var (searchState, pages) = await _search.GetPageSummariesAsync(await _search.ResolveOwnerAsync());
        if (searchState.Ready)
            foreach (var item in all)
                item.Search = SearchPerformanceAnalyzer.ForSlug(pages, item.Slug);

        var filtered = filter switch
        {
            "striking"   => all.Where(i => i.StrikingDistance).OrderBy(i => i.Search!.Position),
            "losing"     => all.Where(i => i.LosingImpressions).OrderByDescending(i => i.Search!.PrevImpressions - i.Search!.Impressions),
            "lowctr"     => all.Where(i => i.LowCtrForPosition).OrderByDescending(i => i.Search!.Impressions),
            "overdue"    => all.Where(i => i.ReviewOverdue),
            "duesoon"    => all.Where(i => i.ReviewDueSoon),
            "unverified" => all.Where(i => i.NeverVerified || i.NoReviewDate),
            "nokeyfacts" => all.Where(i => !i.HasKeyFacts),
            "nofaq"      => all.Where(i => !i.HasFaq),
            "meta"       => all.Where(i => i.MetaMissing || i.MetaTooLong),
            "staleyear"  => all.Where(i => i.StaleYearStamp),
        "weakaeo"    => all.Where(i => i.AeoWeak).OrderBy(i => i.AeoScore),
            "all"        => all,
            _            => all.Where(i => i.NeedsAttention)
        };

        var summary = ContentHealthSummary.From(all);
        summary.SearchConnected = searchState.Ready;
        summary.SearchAsOf      = searchState.AsOf;

        ViewBag.Summary = summary;
        ViewBag.Items   = ListPaging.Apply(this, filtered, q, page, i => new[] { i.Title, i.Slug });
        ViewBag.Filter  = filter;
        ViewData["ListSearchPlaceholder"] = "Search by post title or slug…";
        ViewData["ListSearchKeep"] = new Dictionary<string, string?> { ["filter"] = filter };
        ViewBag.SearchConfigured = searchState.Configured;

        ViewData["Title"] = "Content Health";
        return View();
    }
}
