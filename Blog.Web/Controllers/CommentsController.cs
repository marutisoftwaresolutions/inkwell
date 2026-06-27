using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Blog.Web.Controllers;

[Authorize(Policy = "CanManageComments")]
[Route("admin/[controller]")]
public class CommentsController : Controller
{
    private readonly ICommentRepository _comments;
    private readonly AuditService _audit;

    public CommentsController(ICommentRepository comments, AuditService audit)
    {
        _comments = comments;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, int page = 1)
    {
        CommentStatus? statusFilter = status switch
        {
            "approved" => CommentStatus.Approved,
            _ => CommentStatus.Pending
        };

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var result = await _comments.GetCommentsAsync(new CommentFilter
        {
            Status = statusFilter, Page = page, PageSize = 20
        });
        ViewBag.StatusFilter = status ?? "pending";
        return View(result);
    }

    private IActionResult RedirectBack()
    {
        var referer = Request.Headers.Referer.ToString();
        return string.IsNullOrEmpty(referer) ? RedirectToAction("Index") : Redirect(referer);
    }

    [HttpPost("approve/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id)
    {
        await _comments.UpdateStatusAsync(id, CommentStatus.Approved);
        await _audit.LogAsync(AuditActions.CommentApproved, "Comment", id.ToString());
        return RedirectBack();
    }

    [HttpPost("pending/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pending(Guid id)
    {
        await _comments.UpdateStatusAsync(id, CommentStatus.Pending);
        await _audit.LogAsync(AuditActions.CommentRejected, "Comment", id.ToString());
        return RedirectBack();
    }

    [HttpPost("delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _comments.DeleteAsync(id);
        await _audit.LogAsync(AuditActions.CommentDeleted, "Comment", id.ToString());
        return RedirectBack();
    }
}
