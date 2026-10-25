using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Blog.Web.Controllers.Api;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class MiscApiController : ControllerBase
{
    private readonly ICategoryRepository _categories;
    private readonly ITagRepository _tags;
    private readonly ICommentRepository _comments;
    private readonly IMediaRepository _media;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenantContext;
    private readonly Blog.Web.Services.AuditService _audit;
    private const string Bearer = JwtBearerDefaults.AuthenticationScheme;

    public MiscApiController(ICategoryRepository categories, ITagRepository tags,
        ICommentRepository comments, IMediaRepository media, IUserRepository users,
        ITenantContext tenantContext, Blog.Web.Services.AuditService audit)
    {
        _audit = audit;
        _categories = categories;
        _tags = tags;
        _comments = comments;
        _media = media;
        _users = users;
        _tenantContext = tenantContext;
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var userId = _tenantContext.IsResolved ? _tenantContext.UserId : Guid.Empty;
        return Ok(new { data = await _categories.GetAllAsync(userId) });
    }

    [HttpGet("tags")]
    public async Task<IActionResult> GetTags()
    {
        var userId = _tenantContext.IsResolved ? _tenantContext.UserId : Guid.Empty;
        return Ok(new { data = await _tags.GetAllAsync(userId) });
    }

    [HttpGet("comments")]
    [Authorize(AuthenticationSchemes = Bearer)]
    public async Task<IActionResult> GetComments([FromQuery] Guid? postId, [FromQuery] int page = 1)
    {
        var result = await _comments.GetCommentsAsync(new CommentFilter
        {
            PostId = postId, Status = CommentStatus.Approved, Page = page, PageSize = 20
        });
        // The shape readers see on the page: never the commenter's email or IP address.
        var data = result.Items.Select(c => new { c.Id, c.PostId, c.PostSlug, c.ParentId, c.AuthorName, c.AuthorUrl, c.Content, c.CreatedAt });
        return Ok(new { data, pagination = new { result.Page, result.PageSize, result.TotalItems, result.TotalPages } });
    }

    // There is no comment-posting endpoint (ADR-0033): readers comment through the post page, which runs the spam
    // gate, the comments-enabled check and the firewall reporting. The anonymous POST that skipped all three was removed.

    [HttpPatch("comments/{id}/approve")]
    [Authorize(AuthenticationSchemes = Bearer, Policy = "CanManageComments")]
    public async Task<IActionResult> ApproveComment(Guid id)
    {
        var comment = await _comments.GetByIdAsync(id);
        if (comment == null) return NotFound(new { statusCode = 404, error = "Comment not found." });
        await _comments.UpdateStatusAsync(id, CommentStatus.Approved);
        await _audit.LogAsync(AuditActions.CommentApproved, "Comment", id.ToString(), comment.AuthorName, newValues: "Approved through the API");
        return Ok(new { data = "Approved." });
    }

    [HttpDelete("comments/{id}")]
    [Authorize(AuthenticationSchemes = Bearer, Policy = "CanManageComments")]
    public async Task<IActionResult> DeleteComment(Guid id)
    {
        var comment = await _comments.GetByIdAsync(id);
        if (comment == null) return NotFound(new { statusCode = 404, error = "Comment not found." });
        await _comments.DeleteAsync(id);
        await _audit.LogAsync(AuditActions.CommentDeleted, "Comment", id.ToString(), comment.AuthorName, newValues: "Deleted through the API");
        return Ok(new { data = "Deleted." });
    }

    // There is no media upload endpoint: the one here only redirected to the Desk's upload, which needs a
    // signed-in browser and an anti-forgery token, so it could never work from the API (ADR-0033).

    [HttpGet("users/me")]
    [Authorize(AuthenticationSchemes = Bearer)]
    public async Task<IActionResult> GetMe()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _users.GetByIdAsync(userId);
        if (user == null) return NotFound();
        return Ok(new { data = new { user.Id, user.Email, user.DisplayName, user.Role, user.Bio, user.AvatarUrl } });
    }

    [HttpPut("users/me")]
    [Authorize(AuthenticationSchemes = Bearer)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateMeRequest req)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _users.GetByIdAsync(userId);
        if (user == null) return NotFound();

        user.DisplayName = req.DisplayName ?? user.DisplayName;
        user.Bio = req.Bio ?? user.Bio;
        user.Website = req.Website ?? user.Website;
        await _users.UpdateAsync(user);
        await _audit.LogAsync(AuditActions.UserProfileUpdated, "User", userId.ToString(), user.DisplayName, newValues: "Profile updated through the API");
        return Ok(new { data = "Updated." });
    }
}

public record UpdateMeRequest(string? DisplayName, string? Bio, string? Website);
