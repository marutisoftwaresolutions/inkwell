using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

[Authorize(Policy = "AdminOnly")]
[Route("admin/errors")]
public class ErrorsController : Controller
{
    private readonly IErrorLogRepository _errors;

    public ErrorsController(IErrorLogRepository errors) => _errors = errors;

    [HttpGet("")]
    public async Task<IActionResult> Index(int? status, string? q, int page = 1)
    {
        const int pageSize = 50;
        var (items, total) = await _errors.GetPagedAsync(page, pageSize, status, q);
        var stats = await _errors.GetStatsAsync();

        ViewBag.Items      = items;
        ViewBag.Total      = total;
        ViewBag.Page       = page;
        ViewBag.PageSize   = pageSize;
        ViewBag.TotalPages = (int)System.Math.Ceiling((double)total / pageSize);
        ViewBag.Stats      = stats;
        ViewBag.FilterStatus = status;
        ViewBag.FilterQuery  = q;

        ViewData["Title"] = "Error Monitor";
        return View();
    }
}
