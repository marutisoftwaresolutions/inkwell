using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

// Gated on CanManageCategories (categories.manage) rather than a new permission: a Series is a
// taxonomy-adjacent grouping, and reusing an existing permission keeps every current install
// working with zero RBAC re-seed and no forced re-login (backward compatible).
[Authorize(Policy = "CanManageCategories")]
[Route("admin/series")]
public class SeriesController : Controller
{
    private readonly ISeriesRepository _series;
    private readonly IPostRepository _posts;
    private readonly AuditService _audit;

    public SeriesController(ISeriesRepository series, IPostRepository posts, AuditService audit)
    {
        _series = series;
        _posts = posts;
        _audit = audit;
    }

    private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static string Slugify(string value) =>
        value.ToLower().Trim().Replace(" ", "-").Replace("_", "-").Replace(".", "");

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = CurrentUserId();
        var list = await _series.GetAllAsync(userId);
        return View(list);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string title, string slug, string? description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "Title is required.";
            return RedirectToAction("Index");
        }
        if (string.IsNullOrWhiteSpace(slug)) slug = Slugify(title);

        var userId = CurrentUserId();
        if (await _series.SlugExistsAsync(slug, userId))
        {
            TempData["Error"] = "Slug already exists.";
            return RedirectToAction("Index");
        }

        var series = new Series { Title = title, Slug = slug, Description = description, AuthorId = userId };
        var id = await _series.CreateAsync(series);
        await _audit.LogAsync(AuditActions.SeriesCreated, "Series", id.ToString(), title);
        TempData["Success"] = "Series created — now add posts and set their order.";
        return RedirectToAction("Edit", new { id });
    }

    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var userId = CurrentUserId();
        var series = await _series.GetByIdAsync(id, userId);
        if (series == null) return NotFound();

        ViewBag.SeriesPosts = await _series.GetPostsAsync(id, publishedOnly: false);
        // All the owner's posts, for the "add post" picker (the view filters out ones already added).
        var all = await _posts.GetPostsAsync(new PostFilter
        {
            AuthorId = userId == Guid.Empty ? null : userId,
            Page = 1,
            PageSize = 500
        });
        ViewBag.AllPosts = all.Items;
        return View(series);
    }

    [HttpPost("edit/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, string title, string slug, string? description)
    {
        var userId = CurrentUserId();
        var series = await _series.GetByIdAsync(id, userId);
        if (series == null) return NotFound();

        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "Title is required.";
            return RedirectToAction("Edit", new { id });
        }
        if (string.IsNullOrWhiteSpace(slug)) slug = Slugify(title);
        if (await _series.SlugExistsAsync(slug, userId, id))
        {
            TempData["Error"] = "Slug already exists.";
            return RedirectToAction("Edit", new { id });
        }

        series.Title = title;
        series.Slug = slug;
        series.Description = description;
        await _series.UpdateAsync(series);
        await _audit.LogAsync(AuditActions.SeriesUpdated, "Series", id.ToString(), title);
        TempData["Success"] = "Series updated.";
        return RedirectToAction("Edit", new { id });
    }

    // Replaces the series' membership with the posted, ordered list of post IDs.
    [HttpPost("{id}/posts")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePosts(Guid id, [FromForm(Name = "postIds")] List<Guid>? postIds)
    {
        var userId = CurrentUserId();
        var series = await _series.GetByIdAsync(id, userId);
        if (series == null) return NotFound();

        await _series.SetPostsAsync(id, postIds ?? new List<Guid>());
        await _audit.LogAsync(AuditActions.SeriesPostsUpdated, "Series", id.ToString(), series.Title);
        TempData["Success"] = "Series posts updated.";
        return RedirectToAction("Edit", new { id });
    }

    [HttpPost("delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = CurrentUserId();
        var series = await _series.GetByIdAsync(id, userId);
        await _series.DeleteAsync(id, userId);
        await _audit.LogAsync(AuditActions.SeriesDeleted, "Series", id.ToString(), series?.Title);
        TempData["Success"] = "Series deleted.";
        return RedirectToAction("Index");
    }
}
