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
    private readonly Blog.Web.Services.Security.IpFirewallService _firewall;
    private readonly IUserRepository _users;

    public CommentsController(ICommentRepository comments, AuditService audit,
        Blog.Web.Services.Security.IpFirewallService firewall, IUserRepository users)
    {
        _comments = comments;
        _audit = audit;
        _firewall = firewall;
        _users = users;
    }

    private const int PageSize = 20;

    /// <summary>
    /// The moderation queue: Pending (default), Approved or All, searchable across author name,
    /// email, body and post title. Paged in SQL so the search never loads every comment; the tab,
    /// query and page live in the query string so a redirect after an action lands back here.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, string? q, int page = 1)
    {
        var tab = NormalizeStatus(status);
        CommentStatus? statusFilter = tab switch
        {
            "approved" => CommentStatus.Approved,
            "all" => null,
            _ => CommentStatus.Pending
        };

        var query = (q ?? string.Empty).Trim();
        if (page < 1) page = 1;
        var filter = new CommentFilter { Status = statusFilter, Page = page, PageSize = PageSize };

        PagedResult<Comment> result;
        if (query.Length == 0)
        {
            result = await _comments.GetCommentsAsync(filter);
        }
        else
        {
            var items = await _comments.SearchAsync(filter, query);
            var total = await _comments.CountSearchAsync(filter, query);
            result = new PagedResult<Comment> { Items = items, TotalItems = total, Page = page, PageSize = PageSize };
        }

        Blog.Web.Models.ListPaging.Stash(this,
            Blog.Web.Models.PagingMeta.FromCounts(query, page, result.TotalItems, PageSize, result.Items.Count));
        ViewData["ListSearchPlaceholder"] = "Search comments…";
        ViewData["ListSearchKeep"] = new Dictionary<string, string?> { ["status"] = tab };
        ViewBag.StatusFilter = tab;
        return View(result);
    }

    private static string NormalizeStatus(string? status) => (status ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "approved" => "approved",
        "all" => "all",
        _ => "pending"
    };

    private IActionResult RedirectBack()
    {
        var referer = Request.Headers.Referer.ToString();
        return string.IsNullOrEmpty(referer) ? RedirectToAction("Index") : Redirect(referer);
    }

    /// <summary>Back to the same tab, query and page the moderator was on (state survives navigation).</summary>
    private IActionResult RedirectToList(string? status, string? q, int page)
        => RedirectToAction("Index", new { status = NormalizeStatus(status), q = string.IsNullOrWhiteSpace(q) ? null : q.Trim(), page = page > 1 ? page : (int?)null });

    /// <summary>
    /// Bulk moderation from the queue: approve, move to pending, delete, or delete as spam every
    /// selected comment. Each comment is handled and audited individually, exactly like the single
    /// actions, and one missing comment never stops the rest. Spam reports the posting address to
    /// the IP firewall once per address, however many comments it left — the firewall counts one
    /// moderator judgement per address, not one per row.
    /// </summary>
    [HttpPost("bulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bulk(string action, List<Guid> ids, string? status, string? q, int page = 1)
    {
        ids = ids?.Distinct().ToList() ?? new List<Guid>();
        if (ids.Count == 0) { TempData["Error"] = "Select at least one comment."; return RedirectToList(status, q, page); }

        action = (action ?? string.Empty).Trim().ToLowerInvariant();
        if (action is not ("approve" or "pending" or "delete" or "spam"))
        {
            TempData["Error"] = "Unknown bulk action.";
            return RedirectToList(status, q, page);
        }

        int done = 0, skipped = 0;
        var reportedAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in ids)
        {
            var comment = await _comments.GetByIdAsync(id);
            if (comment is null) { skipped++; continue; }
            var label = $"{comment.AuthorName} on {comment.PostTitle} (bulk)";

            switch (action)
            {
                case "approve":
                    if (comment.Status != CommentStatus.Approved)
                    {
                        await _comments.UpdateStatusAsync(id, CommentStatus.Approved);
                        await _audit.LogAsync(AuditActions.CommentApproved, "Comment", id.ToString(), label);
                    }
                    done++;
                    break;

                case "pending":
                    if (comment.Status != CommentStatus.Pending)
                    {
                        await _comments.UpdateStatusAsync(id, CommentStatus.Pending);
                        await _audit.LogAsync(AuditActions.CommentRejected, "Comment", id.ToString(), label);
                    }
                    done++;
                    break;

                case "delete":
                    await _comments.DeleteAsync(id);
                    await _audit.LogAsync(AuditActions.CommentDeleted, "Comment", id.ToString(), label);
                    done++;
                    break;

                case "spam":
                    await _comments.DeleteAsync(id);
                    var ip = comment.AuthorIp?.Trim();
                    var reported = !string.IsNullOrWhiteSpace(ip) && reportedAddresses.Add(ip!);
                    if (reported)
                        await _firewall.RegisterCommentSpamAsync(ip!, "marked as spam by a moderator (bulk)", $"/{comment.PostSlug}/comment");
                    await _audit.LogAsync(AuditActions.CommentMarkedSpam, "Comment", id.ToString(),
                        label + (string.IsNullOrWhiteSpace(ip) ? "" : $" (from {ip})"));
                    done++;
                    break;
            }
        }

        var noun = done == 1 ? "comment" : "comments";
        var summary = action switch
        {
            "approve" => $"{done} {noun} approved.",
            "pending" => $"{done} {noun} moved to pending.",
            "delete" => $"{done} {noun} deleted.",
            _ => $"{done} {noun} deleted as spam." + (reportedAddresses.Count == 0 ? "" :
                    $" {reportedAddresses.Count} address{(reportedAddresses.Count == 1 ? "" : "es")} reported to the firewall.")
        };
        if (skipped > 0) summary += $" {skipped} skipped: already gone.";
        TempData[skipped == 0 ? "Success" : "Warning"] = summary;
        return RedirectToList(status, q, page);
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

    /// <summary>
    /// Delete as spam. Unlike a plain delete, the address that posted the comment is reported to the
    /// IP firewall with the same weight the automatic gate uses, so a moderator's judgement also
    /// hardens the front door. Nothing is stored; the comment is gone.
    /// </summary>
    [HttpPost("spam/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Spam(Guid id)
    {
        var comment = await _comments.GetByIdAsync(id);
        if (comment is null) return NotFound();

        await _comments.DeleteAsync(id);
        var reported = !string.IsNullOrWhiteSpace(comment.AuthorIp);
        if (reported)
            await _firewall.RegisterCommentSpamAsync(comment.AuthorIp!, "marked as spam by a moderator", $"/{comment.PostSlug}/comment");

        await _audit.LogAsync(AuditActions.CommentMarkedSpam, "Comment", id.ToString(),
            $"{comment.AuthorName} on {comment.PostTitle}" + (reported ? $" (from {comment.AuthorIp})" : ""));
        TempData["Success"] = reported
            ? "Comment deleted as spam and the address reported to the firewall."
            : "Comment deleted as spam.";
        return RedirectBack();
    }

    /// <summary>
    /// Reply from the Desk. The reply is a normal, already-approved comment by the signed-in user,
    /// threaded under the original, so it renders on the post exactly like any other reply.
    /// Replying to a pending comment approves it: a reply only makes sense on a visible thread.
    /// </summary>
    [HttpPost("reply/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(Guid id, string content)
    {
        var parent = await _comments.GetByIdAsync(id);
        if (parent is null) return NotFound();

        content = (content ?? string.Empty).Trim();
        if (content.Length == 0)
        {
            TempData["Error"] = "Write something before sending the reply.";
            return RedirectBack();
        }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var me = await _users.GetByIdAsync(userId);
        var reply = new Comment
        {
            PostId = parent.PostId,
            ParentId = parent.Id,
            MemberId = userId,
            AuthorName = me?.DisplayName ?? me?.Username ?? User.Identity?.Name ?? "Editor",
            AuthorEmail = me?.Email ?? User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            Content = content,
            Status = CommentStatus.Approved,
        };
        var replyId = await _comments.CreateAsync(reply);

        if (parent.Status != CommentStatus.Approved)
        {
            await _comments.UpdateStatusAsync(parent.Id, CommentStatus.Approved);
            await _audit.LogAsync(AuditActions.CommentApproved, "Comment", parent.Id.ToString(), parent.AuthorName);
        }
        await _audit.LogAsync(AuditActions.CommentReplied, "Comment", replyId.ToString(),
            $"reply to {parent.AuthorName} on {parent.PostTitle}");
        TempData["Success"] = "Reply posted.";
        return RedirectBack();
    }
}
