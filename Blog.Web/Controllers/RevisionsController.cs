using System.Security.Claims;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Revision history for posts and pages: the sidebar list, a side-by-side compare against the
/// current content, and restore. Restore writes the current content as a revision first, so the
/// step is itself reversible, and never touches slug or publish state.
/// </summary>
[Authorize]
[Route("admin/revisions")]
public class RevisionsController : Controller
{
    private readonly IRevisionRepository _revisions;
    private readonly IPostRepository _posts;
    private readonly IPageRepository _pages;
    private readonly RevisionService _revisionService;
    private readonly AuditService _audit;
    private readonly IAuthorizationService _authz;
    private readonly ITenantContext _tenant;

    public RevisionsController(IRevisionRepository revisions, IPostRepository posts, IPageRepository pages,
        RevisionService revisionService, AuditService audit, IAuthorizationService authz, ITenantContext tenant)
    {
        _tenant = tenant;
        _revisions = revisions;
        _posts = posts;
        _pages = pages;
        _revisionService = revisionService;
        _audit = audit;
        _authz = authz;
    }

    [HttpGet("{type}/{entityId:guid}")]
    public async Task<IActionResult> List(string type, Guid entityId)
    {
        var entityType = Normalise(type);
        if (entityType is null) return NotFound();
        if (!await MayEditAsync(entityType, entityId)) return Forbid();

        ViewBag.EntityType = entityType;
        ViewBag.EntityId = entityId;
        ViewBag.Revisions = await _revisions.ListAsync(entityType, entityId);
        return PartialView("_RevisionList");
    }

    [HttpGet("{type}/{entityId:guid}/{revisionId:guid}")]
    public async Task<IActionResult> Compare(string type, Guid entityId, Guid revisionId)
    {
        var entityType = Normalise(type);
        if (entityType is null) return NotFound();
        if (!await MayEditAsync(entityType, entityId)) return Forbid();

        var revision = await _revisions.GetByIdAsync(revisionId);
        if (revision is null || revision.EntityId != entityId || revision.EntityType != entityType) return NotFound();

        var current = await CurrentAsync(entityType, entityId);
        if (current is null) return NotFound();

        ViewBag.Revision = revision;
        ViewBag.Current = current.Value;
        ViewBag.EntityType = entityType;
        ViewBag.EntityId = entityId;
        ViewBag.BodyDiff = TextDiff.Lines(revision.Html, current.Value.Html);
        ViewBag.EditUrl = entityType == RevisionSnapshot.PostType ? $"/admin/posts/edit/{entityId}" : $"/admin/pages/edit/{entityId}";
        ViewData["Title"] = $"Revision #{revision.Number}";
        return View();
    }

    [HttpPost("{type}/{entityId:guid}/{revisionId:guid}/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(string type, Guid entityId, Guid revisionId)
    {
        var entityType = Normalise(type);
        if (entityType is null) return NotFound();
        if (!await MayEditAsync(entityType, entityId)) return Forbid();

        var revision = await _revisions.GetByIdAsync(revisionId);
        if (revision is null || revision.EntityId != entityId || revision.EntityType != entityType) return NotFound();

        if (entityType == RevisionSnapshot.PostType)
        {
            var post = await _posts.GetByIdAsync(entityId, null);
            if (post is null) return NotFound();

            // The content being replaced becomes a revision first, so a restore is itself undoable.
            await _revisionService.RecordAsync(post, "Before restore", User);
            RevisionSnapshot.ApplyToPost(revision, post);
            await _posts.UpdateAsync(post);
            await _revisionService.RecordAsync(post, $"Restored from #{revision.Number}", User);
            await _audit.LogAsync(AuditActions.PostRevisionRestored, "Post", entityId.ToString(), $"{post.Title} ← revision #{revision.Number}");
            TempData["Success"] = $"Restored revision #{revision.Number}. Slug and publish state were left as they were.";
            return Redirect($"/admin/posts/edit/{entityId}");
        }
        else
        {
            var page = await _pages.GetBySlugAsync((await _revisions.GetLatestAsync(entityType, entityId))?.Slug ?? revision.Slug)
                       ?? await FindPageAsync(entityId);
            if (page is null || page.Id != entityId) page = await FindPageAsync(entityId);
            if (page is null) return NotFound();

            await _revisionService.RecordAsync(page, "Before restore", User);
            RevisionSnapshot.ApplyToPage(revision, page);
            await _pages.UpdateAsync(page);
            await _revisionService.RecordAsync(page, $"Restored from #{revision.Number}", User);
            await _audit.LogAsync(AuditActions.PageRevisionRestored, "Page", entityId.ToString(), $"{page.Title} ← revision #{revision.Number}");
            TempData["Success"] = $"Restored revision #{revision.Number}. Slug and publish state were left as they were.";
            return Redirect($"/admin/pages/edit/{entityId}");
        }
    }

    private static string? Normalise(string type) =>
        type.Equals("post", StringComparison.OrdinalIgnoreCase) ? RevisionSnapshot.PostType
        : type.Equals("page", StringComparison.OrdinalIgnoreCase) ? RevisionSnapshot.PageType
        : null;

    /// <summary>
    /// Same rule the editors apply: own post or Editor/Admin; pages need the pages policy. The entity is
    /// always loaded, so a guessed id never lists or restores anything, and in cloud mode the entity must
    /// belong to the resolved tenant (revisions carry no owner of their own — the post or page is the
    /// tenant boundary).
    /// </summary>
    private async Task<bool> MayEditAsync(string entityType, Guid entityId)
    {
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid);
        if (entityType == RevisionSnapshot.PostType)
        {
            var post = await _posts.GetByIdAsync(entityId, null);
            if (post is null) return false;
            if (_tenant.IsCloudMode && post.AuthorId != _tenant.UserId && post.AuthorId != uid) return false;
            if (User.IsInRole("Admin") || User.IsInRole("Editor")) return true;
            return post.AuthorId == uid;
        }
        var page = await FindPageAsync(entityId);
        if (page is null) return false;
        if (_tenant.IsCloudMode && page.AuthorId != _tenant.UserId && page.AuthorId != uid) return false;
        return (await _authz.AuthorizeAsync(User, "CanManagePages")).Succeeded;
    }

    private async Task<(string Title, string Html)?> CurrentAsync(string entityType, Guid entityId)
    {
        if (entityType == RevisionSnapshot.PostType)
        {
            var post = await _posts.GetByIdAsync(entityId, null);
            return post is null ? null : (post.Title ?? string.Empty, post.Html ?? string.Empty);
        }
        var page = await FindPageAsync(entityId);
        return page is null ? null : (page.Title ?? string.Empty, page.Content ?? string.Empty);
    }

    // IPageRepository.GetByIdAsync is author-scoped; pages are found through the caller's id first,
    // then through the global slug lookup via the newest revision, so an editor can restore an
    // author's page.
    private async Task<Page?> FindPageAsync(Guid entityId)
    {
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
        {
            var own = await _pages.GetByIdAsync(entityId, uid);
            if (own is not null) return own;
        }
        var latest = await _revisions.GetLatestAsync(RevisionSnapshot.PageType, entityId);
        if (latest is null) return null;
        var bySlug = await _pages.GetBySlugAsync(latest.Slug);
        return bySlug?.Id == entityId ? bySlug : null;
    }
}
