using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

[Authorize(Policy = "AdminOnly")]
[Route("admin/subscribers")]
public class SubscribersController : Controller
{
    private readonly IMemberRepository _members;

    public SubscribersController(IMemberRepository members)
    {
        _members = members;
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
        TempData["Success"] = "Subscriber removed.";
        return RedirectToAction(nameof(Index), new { status, page });
    }
}
