using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

[Authorize(Policy = "CanManageTags")]
[Route("admin/tags")]
public class TagsController : Controller
{
    private readonly ITagRepository _tags;
    private readonly AuditService _audit;

    public TagsController(ITagRepository tags, AuditService audit)
    {
        _tags = tags;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var tags = await _tags.GetAllAsync(userId);
        ViewData["ListSearchPlaceholder"] = "Search tags";
        return View(Blog.Web.Models.ListPaging.Apply(this, tags, q, page, t => new[] { t.Name, t.Slug }));
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Name is required.";
            return RedirectToAction("Index");
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = name.ToLower().Trim().Replace(" ", "-").Replace("_", "-").Replace(".", "");
        }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var existing = await _tags.GetBySlugAsync(slug, userId);
        if (existing != null)
        {
            TempData["Error"] = "Tag slug already exists.";
            return RedirectToAction("Index");
        }

        var tag = new Tag
        {
            Name = name,
            Slug = slug,
            AuthorId = userId
        };

        await _tags.CreateAsync(tag);
        await _audit.LogAsync(AuditActions.TagCreated, "Tag", null, name);
        TempData["Success"] = "Tag created.";
        return RedirectToAction("Index");
    }

    /// <summary>The description editor for one tag: shown on /tag/{slug} and used as its meta description.</summary>
    [HttpGet("describe/{id}")]
    public async Task<IActionResult> Describe(Guid id, string? q, int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var tag = await _tags.GetByIdAsync(id, userId);
        if (tag is null) return NotFound();
        return View("~/Views/Shared/ArchiveDescription.cshtml", new Blog.Web.Models.ArchiveDescriptionModel(
            "Tag", tag.Id, tag.Name, $"/tag/{tag.Slug}", tag.Description,
            $"/admin/tags/describe/{tag.Id}", "/admin/tags", q, page));
    }

    [HttpPost("describe/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Describe(Guid id, string? description, string? q, int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var tag = await _tags.GetByIdAsync(id, userId);
        if (tag is null) return NotFound();

        var error = Blog.Core.Services.ArchiveIntro.Validate(description);
        if (error is not null)
        {
            ViewData["FieldError"] = error;
            return View("~/Views/Shared/ArchiveDescription.cshtml", new Blog.Web.Models.ArchiveDescriptionModel(
                "Tag", tag.Id, tag.Name, $"/tag/{tag.Slug}", description,
                $"/admin/tags/describe/{tag.Id}", "/admin/tags", q, page));
        }

        var clean = Blog.Core.Services.ArchiveIntro.Normalize(description);
        if (clean != tag.Description)
        {
            await _tags.SetDescriptionAsync(id, userId, clean);
            await _audit.LogAsync(AuditActions.TagUpdated, "Tag", id.ToString(), tag.Name,
                oldValues: tag.Description, newValues: clean ?? "Description removed");
        }
        TempData["Success"] = clean is null ? "Tag description removed." : "Tag description saved.";
        return RedirectToAction("Index", new { q, page });
    }

    [HttpPost("delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _tags.DeleteAsync(id, userId);
        await _audit.LogAsync(AuditActions.TagDeleted, "Tag", id.ToString());
        TempData["Success"] = "Tag deleted.";
        return RedirectToAction("Index");
    }
}
