using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Redirects. Manages the redirect table and turns the 404 log into a worklist.
///
/// Redirect rules have existed since v1.0.4 — with 301, 302 and 410 support — but nothing could
/// read, edit or remove them: rules were written by hand in SQL, and the ones the importer and
/// slug-rename logic create accumulated invisibly. This is the surface for both.
/// </summary>
[Authorize(Policy = "AdminOnly")]
[Route("admin/redirects")]
public class RedirectsController : Controller
{
    private readonly IRedirectRepository _redirects;
    private readonly AuditService _audit;

    public RedirectsController(IRedirectRepository redirects, AuditService audit)
    {
        _redirects = redirects;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? tab, string? q, int page = 1)
    {
        var rules = await _redirects.GetAllAsync();

        var groups = await _redirects.GetUnhandledNotFoundsAsync();
        var slugs  = await _redirects.GetPublishedSlugsAsync();
        var triaged = NotFoundTriage.Triage(groups, slugs);

        ViewBag.Tab       = tab == "notfound" ? "notfound" : "rules";
        ViewBag.Rules     = Blog.Web.Models.ListPaging.Apply(this, rules, q, page, r => new[] { r.From, r.To });
        ViewData["ListSearchPlaceholder"] = "Search rules by from or to path";
        ViewData["ListSearchKeep"] = new Dictionary<string, string?> { ["tab"] = (string)ViewBag.Tab };
        ViewBag.NotFounds = triaged;

        // Actionable = something links here, or it is close enough to a real slug to be a rename.
        // The rest is almost entirely scanner traffic and is shown separately, collapsed.
        ViewBag.Actionable = triaged.Count(c => c.Kind != NotFoundKind.Unmatched);
        ViewBag.NearMisses = triaged.Count(c => c.Kind == NotFoundKind.NearMiss);
        ViewBag.GoneCount  = rules.Count(r => r.IsGone);

        ViewData["Title"] = "Redirects";
        return View();
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string from, string? to, int statusCode, string? tab)
    {
        from = (from ?? "").Trim();
        to = (to ?? "").Trim();

        var existing = await _redirects.GetAllAsync();
        var error = NotFoundTriage.Validate(from, to, statusCode, existing);
        if (error is not null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Index), new { tab });
        }

        var previous = existing.FirstOrDefault(r =>
            string.Equals(r.From, from, StringComparison.OrdinalIgnoreCase));

        // A 410 says the URL is intentionally gone; it has no destination to store.
        var destination = statusCode == RedirectStatus.Gone ? "" : to;

        await _redirects.UpsertAsync(from, destination, statusCode);

        await _audit.LogAsync(
            previous is null ? AuditActions.RedirectCreated : AuditActions.RedirectUpdated,
            "Redirect", from, from,
            oldValues: previous is null ? null : JsonSerializer.Serialize(new { previous.To, previous.StatusCode }),
            newValues: JsonSerializer.Serialize(new { To = destination, StatusCode = statusCode }));

        TempData["Success"] = statusCode == RedirectStatus.Gone
            ? $"{from} is now retired with 410 Gone."
            : $"{from} now redirects to {destination} ({statusCode}).";

        return RedirectToAction(nameof(Index), new { tab });
    }

    [HttpPost("delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string from, string? tab)
    {
        from = (from ?? "").Trim();

        var rule = await _redirects.GetRuleAsync(from);
        var removed = await _redirects.DeleteAsync(from);

        if (removed)
        {
            await _audit.LogAsync(AuditActions.RedirectDeleted, "Redirect", from, from,
                oldValues: rule is null ? null : JsonSerializer.Serialize(new { rule.To, rule.StatusCode }));

            TempData["Success"] = $"Rule for {from} removed. The URL will now serve whatever it otherwise would.";
        }
        else
        {
            TempData["Error"] = $"No rule found for {from}.";
        }

        return RedirectToAction(nameof(Index), new { tab });
    }
}
