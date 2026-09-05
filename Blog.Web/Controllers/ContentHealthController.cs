using Blog.Core.Domain;
using Blog.Core.Interfaces;
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

    public ContentHealthController(IContentHealthRepository health, ITenantContext tenant)
    {
        _health = health;
        _tenant = tenant;
    }

    /// <param name="filter">
    /// attention (default) · overdue · duesoon · unverified · nokeyfacts · nofaq · meta · staleyear · weakaeo · all
    /// </param>
    [HttpGet("")]
    public async Task<IActionResult> Index(string filter = "attention")
    {
        // Self-hosted keeps everything under one owner; cloud scopes to the signed-in tenant.
        var ownerId = _tenant.IsCloudMode && _tenant.IsResolved ? _tenant.UserId : (Guid?)null;
        var all = await _health.GetPublishedAsync(ownerId);

        var filtered = filter switch
        {
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

        ViewBag.Summary = ContentHealthSummary.From(all);
        ViewBag.Items   = filtered.ToList();
        ViewBag.Filter  = filter;

        ViewData["Title"] = "Content Health";
        return View();
    }
}
