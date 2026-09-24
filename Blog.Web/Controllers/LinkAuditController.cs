using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Link Audit. Scans every published post and page for internal links that dead-end, point
/// at a retired URL, or take an avoidable redirect hop. Read-only: it reports, the author fixes.
/// </summary>
[Authorize(Policy = "EditorOrAbove")]
[Route("admin/link-audit")]
public class LinkAuditController : Controller
{
    private readonly ILinkAuditRepository _repo;
    private readonly ITenantContext _tenant;

    public LinkAuditController(ILinkAuditRepository repo, ITenantContext tenant)
    {
        _repo = repo;
        _tenant = tenant;
    }

    /// <param name="filter">severe (default) · broken · gone · redirect · chain · all</param>
    /// <param name="q">Search across post title, slug, link, anchor and issue text.</param>
    /// <param name="page">1-based page within the filtered, searched list.</param>
    [HttpGet("")]
    public async Task<IActionResult> Index(string filter = "severe", string? q = null, int page = 1)
    {
        var ownerId = _tenant.IsCloudMode && _tenant.IsResolved ? _tenant.UserId : (Guid?)null;
        var data = await _repo.LoadAsync(ownerId);
        var all = LinkAuditor.Analyze(data.Documents, data.PublishedSlugs, data.Redirects);

        var filtered = filter switch
        {
            "broken"   => all.Where(i => i.Kind == LinkIssueKind.Broken),
            "gone"     => all.Where(i => i.Kind == LinkIssueKind.Gone),
            "redirect" => all.Where(i => i.Kind == LinkIssueKind.Redirect),
            "chain"    => all.Where(i => i.Kind == LinkIssueKind.Chain),
            "all"      => all,
            _          => all.Where(i => i.IsSevere)
        };

        ViewBag.Issues     = ListPaging.Apply(this, filtered, q, page,
            i => new[] { i.SourceTitle, i.SourceSlug, i.Href, i.Anchor, i.Detail, i.Kind.ToString() });
        ViewBag.Filter     = filter;
        ViewData["ListSearchPlaceholder"] = "Search by post, slug or link…";
        ViewData["ListSearchKeep"] = new Dictionary<string, string?> { ["filter"] = filter };
        ViewBag.Scanned    = data.Documents.Count;
        ViewBag.Broken     = all.Count(i => i.Kind == LinkIssueKind.Broken);
        ViewBag.Gone       = all.Count(i => i.Kind == LinkIssueKind.Gone);
        ViewBag.Redirects  = all.Count(i => i.Kind == LinkIssueKind.Redirect);
        ViewBag.Chains     = all.Count(i => i.Kind == LinkIssueKind.Chain);

        ViewData["Title"] = "Link Audit";
        return View();
    }
}
