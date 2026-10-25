using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Blog.Web.Controllers;

[Authorize(Policy = "CanEditPosts")]
[Route("admin/[controller]")]
public class PostsController : Controller
{
    private readonly IPostRepository _posts;
    private readonly ICategoryRepository _categories;
    private readonly ITagRepository _tags;
    private readonly IMediaRepository _media;
    private readonly PostService _postService;
    private readonly AuditService _audit;
    private readonly IndexNowService _indexNow;
    private readonly ILinkSuggestionRepository _links;
    private readonly ITenantContext _tenantContext;
    private readonly Blog.Web.Services.SearchConsole.SearchPerformanceService _search;
    private readonly RevisionService _revisions;
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly IWebHostEnvironment _env;

    private readonly Blog.Web.Services.Webhooks.WebhookPublisher _webhooks;

    /// <summary>May publish (and so approve or return posts in review).</summary>
    private bool CanPublish => User.HasClaim("Permission", "posts.publish");

    public PostsController(IPostRepository posts, ICategoryRepository categories,
        ITagRepository tags, IMediaRepository media, PostService postService, AuditService audit,
        IndexNowService indexNow, ILinkSuggestionRepository links, ITenantContext tenantContext,
        Blog.Web.Services.SearchConsole.SearchPerformanceService search, RevisionService revisions,
        ISettingRepository settings, IUserRepository users, IWebHostEnvironment env,
        Blog.Web.Services.Webhooks.WebhookPublisher webhooks)
    {
        _webhooks = webhooks;
        _env = env;
        _settings = settings;
        _users = users;
        _search = search;
        _revisions = revisions;
        _posts = posts;
        _categories = categories;
        _tags = tags;
        _media = media;
        _postService = postService;
        _audit = audit;
        _indexNow = indexNow;
        _links = links;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// The zone an operator types schedule times in (Admin → Settings → Display). Stored values are
    /// UTC; the editor shows and reads the display zone so "15:00" means 15:00 on the operator's clock.
    /// Falls back to UTC when settings cannot be resolved, which is also the pre-1.0.6 behaviour.
    /// </summary>
    private async Task<string?> DisplayTimeZoneAsync()
    {
        try
        {
            Guid ownerId;
            if (_tenantContext.IsCloudMode) ownerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            else ownerId = (await _users.GetFirstAdminAsync())?.Id ?? Guid.Empty;
            var s = await _settings.GetSettingsAsync(ownerId);
            return string.IsNullOrWhiteSpace(s.DisplayTimeZoneId) ? null : s.DisplayTimeZoneId;
        }
        catch { return null; }
    }

    private async Task NormaliseScheduleAsync(Post post)
    {
        if (post.ScheduledAt.HasValue)
            post.ScheduledAt = TimeZoneHelper.FromDisplay(post.ScheduledAt.Value, await DisplayTimeZoneAsync());
    }

    // Ping IndexNow with a freshly published/updated post's public URL (best-effort, non-throwing).
    private async Task PingIndexNowAsync(Guid postId)
    {
        var saved = await _posts.GetByIdAsync(postId, null);
        if (saved is { Status: PostStatus.Published } && !string.IsNullOrEmpty(saved.Slug))
            await _indexNow.SubmitAsync($"{Request.Scheme}://{Request.Host}/{saved.Slug}");
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? search, string? status, int page = 1)
    {
        // The shared list search posts ?q=; older links and the command palette may still send ?search=.
        search = string.IsNullOrWhiteSpace(q) ? search : q;
        if (page < 1) page = 1;
        PostStatus? statusFilter = status switch
        {
            "published" => PostStatus.Published,
            "draft" => PostStatus.Draft,
            "scheduled" => PostStatus.Scheduled,
            "review" => PostStatus.InReview,
            "changes" => PostStatus.NeedsChanges,
            _ => null
        };

        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        // Review filters: editors see everyone's queue; an author sees only their own posts there (and their notes).
        var workflowFilter = statusFilter is PostStatus.InReview or PostStatus.NeedsChanges;
        var result = await _posts.GetPostsAsync(new PostFilter
        {
            Search = search, Status = statusFilter, AuthorId = workflowFilter && !CanPublish ? currentUserId : null, Page = page, PageSize = 20
        });
        ViewBag.CanPublish = CanPublish;

        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewData["ListSearchKeep"] = new Dictionary<string, string?> { ["status"] = string.IsNullOrEmpty(status) ? null : status };
        Blog.Web.Models.ListPaging.Stash(this, Blog.Web.Models.PagingMeta.FromCounts(search, page, result.TotalItems, 20, result.Items.Count));
        ViewBag.CurrentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        ViewBag.IsAdmin = User.IsInRole("Admin");
        ViewBag.IsEditor = User.IsInRole("Editor");
        return View(result);
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(string? mode = null)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        ViewBag.Categories = await _categories.GetAllAsync(userId);
        ViewBag.Tags = await _tags.GetAllAsync(userId);
        CustomFieldsEditor.Show(ViewData, await CustomFieldDefinitionsAsync(), new Dictionary<string, string>());
        return View(new Post { EditorMode = EditorModes.Normalize(mode) });
    }

    private Task<IReadOnlyList<CustomFieldDefinition>> CustomFieldDefinitionsAsync() =>
        HttpContext.RequestServices.GetRequiredService<CustomFieldsEditor>().DefinitionsAsync(User, pages: false);

    /// <summary>
    /// Validates the posted custom fields onto <paramref name="post"/> (ADR-0025) and fills the panel's
    /// ViewData either way, so a re-rendered editor shows what was typed. False when a value needs fixing.
    /// </summary>
    private async Task<bool> ApplyCustomFieldsAsync(Post post, IDictionary<string, string?>? posted, string? existingJson)
    {
        var defs = await CustomFieldDefinitionsAsync();
        var result = CustomFields.Clean(defs, posted, existingJson);
        post.CustomFieldsJson = result.Json;
        CustomFieldsEditor.Show(ViewData, defs, result.Values, result.Errors);
        if (result.Errors.Count == 0) return true;
        ModelState.AddModelError("", "Some custom fields need attention. Correct them under Custom fields and save again.");
        return false;
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Post post, List<Guid> categoryIds, string tagNames, string submitAction, Dictionary<string, string?>? cf)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        var userEmail = User.FindFirstValue(ClaimTypes.Email)!;
        post.AuthorId = userId;
        await NormaliseScheduleAsync(post);

        // The server decides the status from the button and the permission — never from a posted Status.
        var decision = EditorialWorkflow.DecideOnSave(null, submitAction, CanPublish, post.ScheduledAt, DateTime.UtcNow);
        post.Status = decision.Status;
        post.ReviewNote = null;

        ApplyStructuredDataGate(post);
        ApplyEditorMode(post, post.EditorMode);

        if (!await ApplyCustomFieldsAsync(post, cf, null))
        {
            ViewBag.Categories = await _categories.GetAllAsync(userId);
            ViewBag.Tags = await _tags.GetAllAsync(userId);
            return View(post);
        }

        var tags = (tagNames ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                                   .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

        var (id, error) = await _postService.CreatePostAsync(post, categoryIds, tags, userId, userEmail);
        if (error != null)
        {
            ModelState.AddModelError("", error);
            ViewBag.Categories = await _categories.GetAllAsync(userId);
            ViewBag.Tags = await _tags.GetAllAsync(userId);
            return View(post);
        }

        post.Id = id;
        if (decision.SubmittedForReview)
            await _posts.SetWorkflowAsync(id, PostStatus.InReview, null, DateTime.UtcNow, null);

        var auditAction = decision.SubmittedForReview ? AuditActions.PostSubmittedForReview
            : post.Status == PostStatus.Scheduled ? AuditActions.PostScheduled
            : post.Status == PostStatus.Published ? AuditActions.PostPublished
            : AuditActions.PostCreated;
        await _audit.LogAsync(auditAction, "Post", id.ToString(), post.Title);
        await _revisions.RecordAsync(post, "Created", User);
        await _webhooks.PostSavedAsync(HttpContext, null, post);
        await PingIndexNowAsync(id);

        TempData["Success"] = decision.Notice
            ?? (post.Status == PostStatus.Scheduled ? "Post scheduled."
                : post.Status == PostStatus.Published ? "Post published." : "Draft saved.");

        return RedirectToAction("Index");
    }

    /// <summary>An Editor or Admin approves a post in review: it is published (or scheduled, if its time is ahead).</summary>
    [HttpPost("{id:guid}/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id)
    {
        var post = await _posts.GetByIdAsync(id, null);
        if (post is null) return NotFound();
        if (!CanPublish) return Forbid();
        if (!EditorialWorkflow.CanReview(post.Status, CanPublish))
        {
            TempData["Error"] = "This post is not waiting for review, so there is nothing to approve.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var before = new Post { Status = post.Status, ScheduledAt = post.ScheduledAt };
        var now = DateTime.UtcNow;
        var status = post.ScheduledAt > now ? PostStatus.Scheduled : PostStatus.Published;
        await _posts.SetWorkflowAsync(id, status, null, null, status == PostStatus.Published ? now : null);
        post.Status = status;
        post.PublishedAt ??= status == PostStatus.Published ? now : null;

        await _audit.LogAsync(AuditActions.PostApproved, "Post", id.ToString(), post.Title);
        await _audit.LogAsync(status == PostStatus.Scheduled ? AuditActions.PostScheduled : AuditActions.PostPublished, "Post", id.ToString(), post.Title);
        await _revisions.RecordAsync(post, "Approved", User);
        await _webhooks.PostSavedAsync(HttpContext, before, post);
        await PingIndexNowAsync(id);

        TempData["Success"] = status == PostStatus.Scheduled
            ? $"Approved. “{post.Title}” is scheduled for its publish time."
            : $"Approved and published: “{post.Title}”.";
        return RedirectToAction(nameof(Index), new { status = "review" });
    }

    /// <summary>An Editor or Admin returns a post in review to its author, with a note saying what to change.</summary>
    [HttpPost("{id:guid}/request-changes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestChanges(Guid id, string? note)
    {
        var post = await _posts.GetByIdAsync(id, null);
        if (post is null) return NotFound();
        if (!CanPublish) return Forbid();
        if (!EditorialWorkflow.CanReview(post.Status, CanPublish))
        {
            TempData["Error"] = "This post is not waiting for review, so it cannot be sent back.";
            return RedirectToAction(nameof(Edit), new { id });
        }
        if (EditorialWorkflow.ValidateNote(note) is { } noteError)
        {
            TempData["Error"] = noteError;
            return RedirectToAction(nameof(Edit), new { id });
        }

        var trimmed = note!.Trim();
        await _posts.SetWorkflowAsync(id, PostStatus.NeedsChanges, trimmed, post.ReviewRequestedAt, null);
        await _audit.LogAsync(AuditActions.PostChangesRequested, "Post", id.ToString(),
            $"{post.Title} — {(trimmed.Length > 120 ? trimmed[..120] + "…" : trimmed)}");

        TempData["Success"] = $"Sent back to {(string.IsNullOrWhiteSpace(post.AuthorName) ? "the author" : post.AuthorName)} with your note.";
        return RedirectToAction(nameof(Index), new { status = "review" });
    }

    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var post = await _posts.GetByIdAsync(id, null);
        if (post == null) return NotFound();

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        bool isPrivileged = User.IsInRole("Admin") || User.IsInRole("Editor");
        if (post.AuthorId != userId && !isPrivileged)
        {
            return Forbid();
        }

        ViewBag.Categories = await _categories.GetAllAsync(userId);
        ViewBag.Tags = await _tags.GetAllAsync(userId);
        ViewBag.SelectedCategoryIds = post.Categories.Select(c => c.Id).ToList();
        ViewBag.SelectedTagNames = string.Join(", ", post.Tags.Select(t => t.Name));
        var displayZone = await DisplayTimeZoneAsync();
        ViewBag.DisplayTimeZone = displayZone;   // the review panel compares the schedule with "now" in the same zone
        if (post.ScheduledAt.HasValue) // the editor shows and reads the display zone; storage is UTC
            post.ScheduledAt = TimeZoneHelper.ToDisplay(post.ScheduledAt.Value, displayZone);
        CustomFieldsEditor.Show(ViewData, await CustomFieldDefinitionsAsync(), CustomFields.Parse(post.CustomFieldsJson));
        return View(post);
    }

    [HttpPost("edit/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Post post, List<Guid> categoryIds, string tagNames, string submitAction, Dictionary<string, string?>? cf)
    {
        var existingPost = await _posts.GetByIdAsync(id, null);
        if (existingPost == null) return NotFound();

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        bool isPrivileged = User.IsInRole("Admin") || User.IsInRole("Editor");
        if (existingPost.AuthorId != userId && !isPrivileged)
        {
            return Forbid();
        }

        var userEmail = User.FindFirstValue(ClaimTypes.Email)!;
        post.Id = id;
        await NormaliseScheduleAsync(post);

        // The server decides the status from the button and the permission — never from a posted Status
        // (a crafted Status=Published used to publish for a user without the publish permission).
        var decision = EditorialWorkflow.DecideOnSave(existingPost.Status, submitAction, CanPublish, post.ScheduledAt, DateTime.UtcNow);
        post.Status = decision.Status;

        ApplyStructuredDataGate(post);
        ApplyEditorMode(post, existingPost.EditorMode);

        if (!await ApplyCustomFieldsAsync(post, cf, existingPost.CustomFieldsJson))
        {
            ViewBag.Categories = await _categories.GetAllAsync(userId);
            ViewBag.Tags = await _tags.GetAllAsync(userId);
            return View(post);
        }

        var tags = (tagNames ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                                   .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

        var error = await _postService.UpdatePostAsync(post, categoryIds, tags, userId, userEmail);
        if (error != null)
        {
            ModelState.AddModelError("", error);
            ViewBag.Categories = await _categories.GetAllAsync(userId);
            ViewBag.Tags = await _tags.GetAllAsync(userId);
            return View(post);
        }
        // The generated social card carries the title; drop the cached PNG so the next request rebuilds it.
        Blog.Web.Services.OgImageCache.Invalidate(_env, post.Slug);
        if (!string.Equals(existingPost.Slug, post.Slug, StringComparison.OrdinalIgnoreCase)) Blog.Web.Services.OgImageCache.Invalidate(_env, existingPost.Slug);

        // Workflow fields: a (re)submission stamps the request time and clears the old note; leaving the
        // workflow (published, scheduled, draft) clears both; a returned post keeps its note while edited.
        if (decision.SubmittedForReview)
            await _posts.SetWorkflowAsync(id, PostStatus.InReview, null, DateTime.UtcNow, null);
        else if (!EditorialWorkflow.IsInWorkflow(post.Status) && (existingPost.ReviewNote is not null || existingPost.ReviewRequestedAt is not null))
            await _posts.SetWorkflowAsync(id, post.Status, null, null, null);

        var approvedFromReview = existingPost.Status == PostStatus.InReview && EditorialWorkflow.IsLive(post.Status, post.ScheduledAt, DateTime.MaxValue);
        var editAuditAction = decision.SubmittedForReview ? AuditActions.PostSubmittedForReview
            : post.Status == PostStatus.Scheduled ? AuditActions.PostScheduled
            : post.Status == PostStatus.Published ? AuditActions.PostPublished
            : AuditActions.PostUpdated;
        await _audit.LogAsync(editAuditAction, "Post", id.ToString(), post.Title);
        if (approvedFromReview) await _audit.LogAsync(AuditActions.PostApproved, "Post", id.ToString(), post.Title);
        await _webhooks.PostSavedAsync(HttpContext, existingPost, post);
        await _revisions.RecordAsync(post,
            post.Status == PostStatus.Published ? "Published" : post.Status == PostStatus.Scheduled ? "Scheduled"
            : post.Status == PostStatus.InReview ? "Submitted for review" : "Saved", User);
        await PingIndexNowAsync(id);

        TempData["Success"] = decision.Notice
            ?? (post.Status == PostStatus.Scheduled ? "Post scheduled."
                : post.Status == PostStatus.Published && submitAction == EditorialWorkflow.Actions.Publish ? "Post published." : "Saved.");

        // An editor who published from review goes back to the queue for the next one.
        return approvedFromReview ? RedirectToAction("Index", new { status = "review" }) : RedirectToAction("Index");
    }

    [HttpPost("delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string? q, string? status, int page = 1)
    {
        var existingPost = await _posts.GetByIdAsync(id, null);
        if (existingPost == null) return NotFound();

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        bool isPrivileged = User.IsInRole("Admin") || User.IsInRole("Editor");

        if (existingPost.AuthorId != userId && !isPrivileged)
        {
            return Forbid();
        }

        await _posts.DeleteAsync(id, isPrivileged ? null : userId);
        Blog.Web.Services.OgImageCache.Invalidate(_env, existingPost.Slug);
        await _audit.LogAsync(AuditActions.PostDeleted, "Post", id.ToString(), existingPost.Title);

        TempData["Success"] = "Post permanently deleted.";
        // Back to the same filtered page, not page 1 of everything (UI/UX rule 10).
        return RedirectToAction("Index", new { q, status, page });
    }

    // Markdown authoring (ADR-0022) ────────────────────────────────────────────────

    /// <summary>The editor's preview: the same renderer the save uses, so the preview is what readers get.</summary>
    [HttpPost("markdown-preview")]
    [ValidateAntiForgeryToken]
    public IActionResult MarkdownPreview([FromForm] string? markdown) =>
        Content(MarkdownRenderer.ToHtml(markdown), "text/html; charset=utf-8");

    /// <summary>
    /// Switches a saved post between the visual editor and Markdown. Visual → Markdown is lossless: the HTML
    /// becomes the Markdown source unchanged (Markdown passes HTML through). Markdown → visual keeps the
    /// rendered HTML and drops the Markdown source; the confirmation says so.
    /// </summary>
    [HttpPost("{id:guid}/editor-mode")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchEditorMode(Guid id, string mode)
    {
        var post = await _posts.GetByIdAsync(id, null);
        if (post == null) return NotFound();
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (post.AuthorId != userId && !(User.IsInRole("Admin") || User.IsInRole("Editor"))) return Forbid();

        var target = EditorModes.Normalize(mode);
        if (target == EditorModes.Normalize(post.EditorMode)) return RedirectToAction(nameof(Edit), new { id });
        if (target == EditorModes.Markdown)
        {
            // Markdown passes HTML through, but a blank line inside an HTML block can end it early and turn
            // indented lines into code. Check first: the switch never changes how a post looks.
            var html = post.Html ?? string.Empty;
            if (!MarkdownRenderer.RendersUnchanged(html))
            {
                TempData["Error"] = "This post can't be switched to Markdown without changing how it looks: some of its HTML has blank lines inside a block. Remove those blank lines in the visual editor, save, then try again.";
                return RedirectToAction(nameof(Edit), new { id });
            }
            post.Markdown = html;
        }
        ApplyEditorMode(post, target);
        await _posts.UpdateAsync(post);
        await _audit.LogAsync(AuditActions.PostUpdated, "Post", id.ToString(), post.Title,
            newValues: target == EditorModes.Markdown ? "Editor: switched to Markdown" : "Editor: switched to the visual editor");
        await _revisions.RecordAsync(post, target == EditorModes.Markdown ? "Switched to Markdown" : "Switched to the visual editor", User);
        TempData["Success"] = target == EditorModes.Markdown ? "This post is now written in Markdown." : "This post now uses the visual editor.";
        return RedirectToAction(nameof(Edit), new { id });
    }


    /// <summary>
    /// Link suggestions for the post being edited, in both directions, plus how it stands against
    /// the two-in / two-out minimum. Loaded into the editor sidebar on demand — the corpus scan is
    /// not worth doing on every page load.
    /// </summary>
    [HttpGet("link-suggestions/{id:guid}")]
    public async Task<IActionResult> LinkSuggestions(Guid id)
    {
        var post = await _posts.GetByIdAsync(id, null);
        if (post is null) return NotFound();

        var ownerId = _tenantContext.IsCloudMode && _tenantContext.IsResolved ? _tenantContext.UserId : (Guid?)null;
        var candidates = await _links.GetCandidatesAsync(ownerId);

        var current = candidates.FirstOrDefault(c => c.Id == id)
            ?? new LinkCandidate(post.Id, post.Slug ?? "", post.Title ?? "", post.Html ?? "", [], []);
        var others = candidates.Where(c => c.Id != id).ToList();

        ViewBag.Status   = LinkSuggester.Status(current, others);
        ViewBag.Outbound = LinkSuggester.SuggestOutbound(current, others);
        ViewBag.Inbound  = LinkSuggester.SuggestInbound(current, others);

        return PartialView("_LinkSuggestions");
    }

    /// <summary>
    /// Bulk actions from the Posts list: publish, move to draft, delete, or add a tag to every
    /// selected post. Each post is checked and audited individually — the same ownership and
    /// permission rules as the single-post actions — and one refused post never stops the rest.
    /// Status changes go through the repository directly; a status-only change writes no revision
    /// (the revision service already skips identical content).
    /// </summary>
    [HttpPost("bulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bulk(string action, List<Guid> ids, string? tag, string? q, string? status, int page = 1)
    {
        // Every exit returns to the same filtered page (UI/UX rule 10).
        var back = new { q, status, page };
        ids = ids?.Distinct().ToList() ?? new List<Guid>();
        if (ids.Count == 0) { TempData["Error"] = "Select at least one post."; return RedirectToAction("Index", back); }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isPrivileged = User.IsInRole("Admin") || User.IsInRole("Editor");
        var canPublish = User.HasClaim("Permission", "posts.publish");
        action = (action ?? string.Empty).Trim().ToLowerInvariant();
        tag = (tag ?? string.Empty).Trim();

        if (action == "tag" && tag.Length == 0) { TempData["Error"] = "Enter the tag to add."; return RedirectToAction("Index", back); }
        if (action == "publish" && !canPublish) { TempData["Error"] = "You do not have permission to publish posts."; return RedirectToAction("Index", back); }
        if (action is not ("publish" or "draft" or "delete" or "tag")) { TempData["Error"] = "Unknown bulk action."; return RedirectToAction("Index", back); }

        int done = 0, refused = 0;
        var publishedUrls = new List<string>();
        Tag? tagEntity = action == "tag" ? await _tags.GetOrCreateAsync(tag, userId) : null;

        foreach (var id in ids)
        {
            var post = await _posts.GetByIdAsync(id, null);
            if (post is null) { refused++; continue; }
            if (post.AuthorId != userId && !isPrivileged) { refused++; continue; }
            var before = new Post { Status = post.Status, ScheduledAt = post.ScheduledAt };

            switch (action)
            {
                case "publish":
                    if (post.Status == PostStatus.Published) { done++; break; }
                    post.Status = PostStatus.Published;
                    post.PublishedAt ??= DateTime.UtcNow;
                    post.ScheduledAt = null;
                    await _posts.UpdateAsync(post);
                    await _audit.LogAsync(AuditActions.PostPublished, "Post", id.ToString(), post.Title + " (bulk)");
                    if (EditorialWorkflow.IsInWorkflow(before.Status)) await _posts.SetWorkflowAsync(id, PostStatus.Published, null, null, null);
                    await _webhooks.PostSavedAsync(HttpContext, before, post);
                    if (!string.IsNullOrEmpty(post.Slug)) publishedUrls.Add($"{Request.Scheme}://{Request.Host}/{post.Slug}");
                    done++;
                    break;
                case "draft":
                    if (post.Status == PostStatus.Draft) { done++; break; }
                    post.Status = PostStatus.Draft;
                    await _posts.UpdateAsync(post);
                    await _audit.LogAsync(AuditActions.PostUnpublished, "Post", id.ToString(), post.Title + " (bulk)");
                    if (EditorialWorkflow.IsInWorkflow(before.Status)) await _posts.SetWorkflowAsync(id, PostStatus.Draft, null, null, null);
                    await _webhooks.PostSavedAsync(HttpContext, before, post);
                    done++;
                    break;
                case "delete":
                    await _posts.DeleteAsync(id, null);
                    Blog.Web.Services.OgImageCache.Invalidate(_env, post.Slug);
                    await _audit.LogAsync(AuditActions.PostDeleted, "Post", id.ToString(), post.Title + " (bulk)");
                    done++;
                    break;
                case "tag":
                    var current = await _tags.GetForPostAsync(id);
                    var tagIds = current.Select(t => t.Id).ToList();
                    if (!tagIds.Contains(tagEntity!.Id)) tagIds.Add(tagEntity.Id);
                    await _posts.AssignTagsAsync(id, tagIds);
                    await _audit.LogAsync(AuditActions.PostUpdated, "Post", id.ToString(), $"{post.Title} — tag “{tagEntity.Name}” added (bulk)");
                    done++;
                    break;
            }
        }

        // One IndexNow submission for the whole batch instead of one request per post inside the loop.
        if (publishedUrls.Count > 0)
        {
            try { await _indexNow.SubmitAsync(publishedUrls); } catch { /* best-effort, never fails the action */ }
        }

        var verb = action switch { "publish" => "published", "draft" => "moved to draft", "delete" => "deleted", _ => $"tagged “{tagEntity?.Name}”" };
        TempData[refused == 0 ? "Success" : "Warning"] = $"{done} post{(done == 1 ? "" : "s")} {verb}." + (refused > 0 ? $" {refused} skipped: not yours to change." : "");
        return RedirectToAction("Index", back);
    }

    /// <summary>
    /// A signed preview link for the post being edited, valid for seven days, for reviewers without
    /// an account. Nothing is stored; the link stops working when it expires or the key ring rotates.
    /// </summary>
    [HttpGet("preview-link/{id:guid}")]
    public async Task<IActionResult> PreviewLink(Guid id, [FromServices] Blog.Web.Services.PreviewLinkService previewLinks)
    {
        var post = await _posts.GetByIdAsync(id, null);
        if (post is null) return NotFound();

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isPrivileged = User.IsInRole("Admin") || User.IsInRole("Editor");
        if (post.AuthorId != userId && !isPrivileged) return Forbid();

        var token = previewLinks.Issue(id);
        ViewBag.Url = $"{Request.Scheme}://{Request.Host}/preview/{Uri.EscapeDataString(token)}";
        ViewBag.Expires = DateTime.UtcNow + Blog.Web.Services.PreviewLinkService.Lifetime;
        return PartialView("_PreviewLink");
    }

    /// <summary>
    /// Search Console performance for the post being edited — its last 28 days against the 28
    /// before, and the queries that brought it impressions. Loaded on demand like the link
    /// suggestions; read-only and fail-open, so the editor never waits on or breaks for it.
    /// </summary>
    [HttpGet("search-performance/{id:guid}")]
    public async Task<IActionResult> SearchPerformance(Guid id)
    {
        var post = await _posts.GetByIdAsync(id, null);
        if (post is null) return NotFound();

        var ownerId = await _search.ResolveOwnerAsync();
        var (state, summary, queries) = await _search.GetForSlugAsync(ownerId, post.Slug ?? string.Empty);

        ViewBag.State   = state;
        ViewBag.Summary = summary;
        ViewBag.Queries = queries;
        ViewBag.IsPublished = post.Status == PostStatus.Published;
        return PartialView("_SearchPerformance");
    }

    /// <summary>
    /// Structured data that would not validate never reaches the live site. A post being published
    /// or scheduled is linted first; on any error it is saved as a draft instead, so the author
    /// keeps their work but invalid or self-serving schema does not ship. Warnings are surfaced and
    /// allowed through - they are judgement calls, not defects.
    /// </summary>
    /// <summary>
    /// Markdown authoring (ADR-0022): a Markdown post's HTML and searchable text are always rendered here from
    /// its Markdown, never taken from the form, so every page, feed and export reads the same HTML as before.
    /// The mode itself changes only through <see cref="SwitchEditorMode"/>; a save keeps the stored one.
    /// </summary>
    private static void ApplyEditorMode(Post post, string? mode)
    {
        post.EditorMode = EditorModes.Normalize(mode);
        if (post.IsMarkdown)
        {
            post.Markdown ??= string.Empty;
            post.Html = MarkdownRenderer.ToHtml(post.Markdown);
            post.Plaintext = MarkdownRenderer.ToPlaintext(post.Html);
        }
        else post.Markdown = null;
    }

    private void ApplyStructuredDataGate(Post post)
    {
        if (post.Status != PostStatus.Published && post.Status != PostStatus.Scheduled) return;

        var findings = StructuredDataLinter.Lint(post);
        if (findings.Count == 0) return;

        if (!StructuredDataLinter.CanPublish(findings))
        {
            var errors = findings.Where(f => f.Severity == LintSeverity.Error)
                                 .Select(f => $"{f.Where}: {f.Message}");
            TempData["Error"] = "Saved as a draft - the structured data has to be fixed before this can publish. "
                              + string.Join(" ", errors);
            post.Status = PostStatus.Draft;
            return;
        }

        var warnings = findings.Where(f => f.Severity == LintSeverity.Warning)
                               .Select(f => $"{f.Where}: {f.Message}")
                               .ToList();
        if (warnings.Count > 0)
            TempData["Warning"] = "Published with structured-data warnings. " + string.Join(" ", warnings);
    }
}
