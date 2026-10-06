using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

[Authorize(Policy = "AdminOnly")]
[Route("admin/subscribers")]
public class SubscribersController : Controller
{
    private readonly IMemberRepository _members;
    private readonly AuditService _audit;

    public SubscribersController(IMemberRepository members, AuditService audit)
    {
        _members = members;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, int page = 1)
    {
        const int pageSize = 50;
        var (items, total) = await _members.GetPagedAsync(page, pageSize, status);

        ViewBag.Status = status;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.Total = total;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

        var confirmed = await _members.GetConfirmedCountAsync();
        ViewBag.ConfirmedCount = confirmed;

        ViewData["Title"] = "Subscribers";
        return View(items);
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string? status, int page = 1)
    {
        await _members.DeleteAsync(id);
        // By id only: the address is personal data and the audit trail is permanent.
        await _audit.LogAsync(AuditActions.SubscriberDeleted, "Subscriber", id.ToString(), "Subscriber");
        TempData["Success"] = "Subscriber removed.";
        return RedirectToAction(nameof(Index), new { status, page });
    }
}
