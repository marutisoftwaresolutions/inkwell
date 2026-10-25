using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Infrastructure.Data; // For SlugHelper
using Blog.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Blog.Web.Controllers;

[Authorize(Policy = "CanManagePages")]
[Route("admin/[controller]")]
public class PagesController : Controller
{
    private readonly IPageRepository _pages;
    private readonly IMediaRepository _media;
    private readonly IUserRepository _users;
    private readonly AuditService _audit;
    private readonly RevisionService _revisions;
    private readonly IndexNowService _indexNow;

    private readonly Blog.Web.Services.Webhooks.WebhookPublisher _webhooks;

    public PagesController(IPageRepository pages, IMediaRepository media, IUserRepository users, AuditService audit,
        RevisionService revisions, IndexNowService indexNow, Blog.Web.Services.Webhooks.WebhookPublisher webhooks)
    {
        _webhooks = webhooks;
        _pages = pages;
        _media = media;
        _users = users;
        _audit = audit;
        _revisions = revisions;
        _indexNow = indexNow;
    }

    // Ping IndexNow with a freshly published/updated page's public URL (best-effort, non-throwing).
    // Mirrors PostsController.PingIndexNowAsync - Pages had no equivalent call until 2026-09-24.
    private async Task PingIndexNowAsync(Guid pageId, Guid userId)
    {
        var saved = await _pages.GetByIdAsync(pageId, userId);
        if (saved is { IsPublished: true } && !string.IsNullOrEmpty(saved.Slug))
            await _indexNow.SubmitAsync($"{Request.Scheme}://{Request.Host}/{saved.Slug}");
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var pages = await _pages.GetAllAsync(userId);
        ViewData["ListSearchPlaceholder"] = "Search pages by title or slug";
        return View(Blog.Web.Models.ListPaging.Apply(this, pages, q, page, p => new[] { p.Title, p.Slug }));
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(string? mode)
    {
        CustomFieldsEditor.Show(ViewData, await CustomFieldDefinitionsAsync(), new Dictionary<string, string>());
        return View(new Page { IsPublished = true, EditorMode = EditorModes.Normalize(mode) });
    }

    private Task<IReadOnlyList<CustomFieldDefinition>> CustomFieldDefinitionsAsync() =>
        HttpContext.RequestServices.GetRequiredService<CustomFieldsEditor>().DefinitionsAsync(User, pages: true);

    /// <summary>Validates the posted custom fields onto <paramref name="page"/> (ADR-0025); false when one needs fixing.</summary>
    private async Task<bool> ApplyCustomFieldsAsync(Page page, IDictionary<string, string?>? posted, string? existingJson)
    {
        var defs = await CustomFieldDefinitionsAsync();
        var result = CustomFields.Clean(defs, posted, existingJson);
        page.CustomFieldsJson = result.Json;
        CustomFieldsEditor.Show(ViewData, defs, result.Values, result.Errors);
        if (result.Errors.Count == 0) return true;
        TempData["Error"] = "Some custom fields need attention. Correct them under Custom fields and save again.";
        return false;
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Page page, string action, Dictionary<string, string?>? cf)
    {
        // Early returns (missing title, taken slug) redisplay what was typed in the custom fields.
        CustomFieldsEditor.Show(ViewData, await CustomFieldDefinitionsAsync(), (cf ?? new()).Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!));
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        // Title is required
        if (string.IsNullOrWhiteSpace(page.Title))
        {
            TempData["Error"] = "Title is required.";
            return View(page);
        }

        try
        {
            page.AuthorId = userId;
            if (action == "publish") page.IsPublished = true;
            else if (action == "draft") page.IsPublished = false;

            // Auto-generate slug if empty
            if (string.IsNullOrWhiteSpace(page.Slug))
                page.Slug = SlugHelper.Generate(page.Title);

            if (await _pages.SlugExistsAsync(page.Slug, userId))
            {
                TempData["Error"] = "A page with this URL slug already exists. Please use a different slug.";
                return View(page);
            }

            page.PublishedAt = page.IsPublished ? DateTime.UtcNow : null;
            ApplyEditorMode(page, page.EditorMode);
            if (!await ApplyCustomFieldsAsync(page, cf, null)) return View(page);
            
            var id = await _pages.CreateAsync(page);
            await _audit.LogAsync(page.IsPublished ? AuditActions.PageCreated : AuditActions.PageCreated,
                "Page", id.ToString(), page.Title);
            page.Id = id;
            await _webhooks.PageSavedAsync(HttpContext, page);
            if (page.IsPublished) await PingIndexNowAsync(id, userId);
            TempData["Success"] = page.IsPublished ? "Page published!" : "Draft saved.";
            return RedirectToAction("Edit", new { id });
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to create page: {ex.Message}";
            return View(page);
        }
    }

    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var page = await _pages.GetByIdAsync(id, userId);
        if (page == null) return NotFound();
        CustomFieldsEditor.Show(ViewData, await CustomFieldDefinitionsAsync(), CustomFields.Parse(page.CustomFieldsJson));
        return View(page);
    }

    [HttpPost("edit/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Page page, string action, Dictionary<string, string?>? cf)
    {
        // Early returns (missing title, taken slug) redisplay what was typed in the custom fields.
        CustomFieldsEditor.Show(ViewData, await CustomFieldDefinitionsAsync(), (cf ?? new()).Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!));
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        if (string.IsNullOrWhiteSpace(page.Title))
        {
            TempData["Error"] = "Title is required.";
            return View(page);
        }

        try
        {
            var existing = await _pages.GetByIdAsync(id, userId);
            if (existing == null) return NotFound();

            existing.Title = page.Title;
            existing.Slug = page.Slug;
            existing.Content = page.Content;
            existing.Markdown = page.Markdown;
            // A save keeps the stored mode; only SwitchEditorMode changes it.
            ApplyEditorMode(existing, existing.EditorMode);
            page.EditorMode = existing.EditorMode;
            existing.FeaturedImageId = page.FeaturedImageId;
            existing.ParentId = page.ParentId;
            existing.SortOrder = page.SortOrder;
            // The SEO and social fields are on the edit form too; they were only ever saved on create.
            existing.MetaTitle = page.MetaTitle;
            existing.MetaDescription = page.MetaDescription;
            existing.CanonicalUrl = page.CanonicalUrl;
            existing.OgTitle = page.OgTitle;
            existing.OgDescription = page.OgDescription;
            existing.OgImage = page.OgImage;
            existing.TwitterTitle = page.TwitterTitle;
            existing.TwitterDescription = page.TwitterDescription;
            existing.TwitterImage = page.TwitterImage;
            if (!await ApplyCustomFieldsAsync(existing, cf, existing.CustomFieldsJson)) { page.CustomFieldsJson = existing.CustomFieldsJson; return View(page); }

            if (action == "publish")
            {
                existing.IsPublished = true;
                if (existing.PublishedAt == null) existing.PublishedAt = DateTime.UtcNow;
            }
            else if (action == "draft")
            {
                existing.IsPublished = false;
            }

            if (await _pages.SlugExistsAsync(existing.Slug, userId, id))
            {
                TempData["Error"] = "A page with this URL slug already exists. Please use a different slug.";
                return View(page);
            }

            await _pages.UpdateAsync(existing);
            await _audit.LogAsync(AuditActions.PageUpdated, "Page", id.ToString(), existing.Title);
            await _revisions.RecordAsync(existing, action == "publish" ? "Published" : "Saved", User);
            await _webhooks.PageSavedAsync(HttpContext, existing);
            if (existing.IsPublished) await PingIndexNowAsync(id, userId);
            TempData["Success"] = "Page updated.";
            return RedirectToAction("Edit", new { id });
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to update page: {ex.Message}";
            return View(page);
        }
    }

    // Markdown authoring (ADR-0022, extended to pages in v1.0.9) ─────────────────────────────

    /// <summary>The editor's preview: the same renderer the save uses, so the preview is what readers get.</summary>
    [HttpPost("markdown-preview")]
    [ValidateAntiForgeryToken]
    public IActionResult MarkdownPreview([FromForm] string? markdown) =>
        Content(MarkdownRenderer.ToHtml(markdown), "text/html; charset=utf-8");

    /// <summary>
    /// Switches a saved page between the visual editor and Markdown, exactly as for posts: visual → Markdown
    /// keeps the HTML as the Markdown source (refused when that would change how the page looks); Markdown →
    /// visual keeps the rendered HTML and drops the source.
    /// </summary>
    [HttpPost("{id:guid}/editor-mode")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchEditorMode(Guid id, string mode)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var page = await _pages.GetByIdAsync(id, userId);
        if (page == null) return NotFound();

        var target = EditorModes.Normalize(mode);
        if (target == EditorModes.Normalize(page.EditorMode)) return RedirectToAction(nameof(Edit), new { id });
        if (target == EditorModes.Markdown)
        {
            var html = page.Content ?? string.Empty;
            if (!MarkdownRenderer.RendersUnchanged(html))
            {
                TempData["Error"] = "This page can't be switched to Markdown without changing how it looks: some of its HTML has blank lines inside a block. Remove those blank lines in the visual editor, save, then try again.";
                return RedirectToAction(nameof(Edit), new { id });
            }
            page.Markdown = html;
        }
        ApplyEditorMode(page, target);
        await _pages.UpdateAsync(page);
        await _audit.LogAsync(AuditActions.PageUpdated, "Page", id.ToString(), page.Title,
            newValues: target == EditorModes.Markdown ? "Editor: switched to Markdown" : "Editor: switched to the visual editor");
        await _revisions.RecordAsync(page, target == EditorModes.Markdown ? "Switched to Markdown" : "Switched to the visual editor", User);
        TempData["Success"] = target == EditorModes.Markdown ? "This page is now written in Markdown." : "This page now uses the visual editor.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    /// <summary>A Markdown page's HTML is always rendered here from its Markdown, never taken from the form.</summary>
    private static void ApplyEditorMode(Page page, string? mode)
    {
        page.EditorMode = EditorModes.Normalize(mode);
        if (page.IsMarkdown)
        {
            page.Markdown ??= string.Empty;
            page.Content = MarkdownRenderer.ToHtml(page.Markdown);
        }
        else page.Markdown = null;
    }

    [HttpPost("delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        bool isAdmin = User.IsInRole("Admin");

        await _pages.DeleteAsync(id, isAdmin ? null : userId);
        await _audit.LogAsync(AuditActions.PageDeleted, "Page", id.ToString());
        TempData["Success"] = "Page permanently deleted.";
        return RedirectToAction("Index");
    }
}


