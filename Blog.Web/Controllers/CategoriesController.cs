using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

[Authorize(Policy = "CanManageCategories")]
[Route("admin/categories")]
public class CategoriesController : Controller
{
    private readonly ICategoryRepository _categories;
    private readonly AuditService _audit;

    public CategoriesController(ICategoryRepository categories, AuditService audit)
    {
        _categories = categories;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var categories = await _categories.GetAllAsync(userId);
        ViewData["ListSearchPlaceholder"] = "Search categories";
        // Parents before their children; the view indents by depth and offers every category as a parent.
        var ordered = Blog.Core.Services.CategoryTree.Order(categories);
        ViewBag.Depths = ordered.ToDictionary(n => n.Category.Id, n => n.Depth);
        ViewBag.AllCategories = ordered;
        return View(Blog.Web.Models.ListPaging.Apply(this, ordered.Select(n => n.Category).ToList(), q, page, c => new[] { c.Name, c.Slug }));
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string slug, Guid? parentId)
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
        if (await _categories.SlugExistsAsync(slug, userId))
        {
            TempData["Error"] = "Slug already exists.";
            return RedirectToAction("Index");
        }

        if (parentId is not null && !Blog.Core.Services.CategoryTree.CanPlaceUnder(await _categories.GetAllAsync(userId), Guid.Empty, parentId))
        {
            TempData["Error"] = $"Choose a parent from the list. Categories can be nested {Blog.Core.Services.CategoryTree.MaxDepth} levels deep.";
            return RedirectToAction("Index");
        }

        var category = new Category
        {
            Name = name,
            Slug = slug,
            AuthorId = userId,
            ParentId = parentId
        };

        await _categories.CreateAsync(category);
        await _audit.LogAsync(AuditActions.CategoryCreated, "Category", null, name);
        TempData["Success"] = "Category created.";
        return RedirectToAction("Index");
    }

    /// <summary>Places a category under another one, or back at the top level.</summary>
    [HttpPost("move/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(Guid id, Guid? parentId, string? q, int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var all = await _categories.GetAllAsync(userId);
        var category = all.FirstOrDefault(c => c.Id == id);
        if (category is null) return NotFound();

        if (!Blog.Core.Services.CategoryTree.CanPlaceUnder(all, id, parentId))
        {
            TempData["Error"] = $"“{category.Name}” cannot go there: a category cannot sit under itself or under one of its own subcategories, and categories nest {Blog.Core.Services.CategoryTree.MaxDepth} levels deep at most.";
            return RedirectToAction("Index", new { q, page });
        }
        if (category.ParentId != parentId)
        {
            await _categories.SetParentAsync(id, userId, parentId);
            var under = parentId is Guid p ? all.First(c => c.Id == p).Name : null;
            await _audit.LogAsync(AuditActions.CategoryMoved, "Category", id.ToString(), category.Name,
                newValues: under is null ? "Moved to the top level" : $"Moved under {under}");
            TempData["Success"] = under is null ? "Category moved to the top level." : $"Category moved under “{under}”.";
        }
        return RedirectToAction("Index", new { q, page });
    }

    /// <summary>The description editor for one category: shown on /category/{slug} and used as its meta description.</summary>
    [HttpGet("describe/{id}")]
    public async Task<IActionResult> Describe(Guid id, string? q, int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var category = await _categories.GetByIdAsync(id, userId);
        if (category is null) return NotFound();
        return View("~/Views/Shared/ArchiveDescription.cshtml", new Blog.Web.Models.ArchiveDescriptionModel(
            "Category", category.Id, category.Name, $"/category/{category.Slug}", category.Description,
            $"/admin/categories/describe/{category.Id}", "/admin/categories", q, page));
    }

    [HttpPost("describe/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Describe(Guid id, string? description, string? q, int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var category = await _categories.GetByIdAsync(id, userId);
        if (category is null) return NotFound();

        var error = Blog.Core.Services.ArchiveIntro.Validate(description);
        if (error is not null)
        {
            ViewData["FieldError"] = error;
            return View("~/Views/Shared/ArchiveDescription.cshtml", new Blog.Web.Models.ArchiveDescriptionModel(
                "Category", category.Id, category.Name, $"/category/{category.Slug}", description,
                $"/admin/categories/describe/{category.Id}", "/admin/categories", q, page));
        }

        var clean = Blog.Core.Services.ArchiveIntro.Normalize(description);
        if (clean != category.Description)
        {
            await _categories.SetDescriptionAsync(id, userId, clean);
            await _audit.LogAsync(AuditActions.CategoryUpdated, "Category", id.ToString(), category.Name,
                oldValues: category.Description, newValues: clean ?? "Description removed");
        }
        TempData["Success"] = clean is null ? "Category description removed." : "Category description saved.";
        return RedirectToAction("Index", new { q, page });
    }

    [HttpPost("delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _categories.DeleteAsync(id, userId);
        await _audit.LogAsync(AuditActions.CategoryDeleted, "Category", id.ToString());
        TempData["Success"] = "Category deleted.";
        return RedirectToAction("Index");
    }
}
