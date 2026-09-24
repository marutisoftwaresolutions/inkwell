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

    public PostsController(IPostRepository posts, ICategoryRepository categories,
        ITagRepository tags, IMediaRepository media, PostService postService, AuditService audit,
        IndexNowService indexNow, ILinkSuggestionRepository links, ITenantContext tenantContext,
        Blog.Web.Services.SearchConsole.SearchPerformanceService search, RevisionService revisions,
        ISettingRepository settings, IUserRepository users, IWebHostEnvironment env)
    {
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
    public async Task<IActionResult> Index(string? search, string? status, int page = 1)
    {
        PostStatus? statusFilter = status switch
        {
            "published" => PostStatus.Published,
            "draft" => PostStatus.Draft,
            "scheduled" => PostStatus.Scheduled,
            _ => null
        };

        var result = await _posts.GetPostsAsync(new PostFilter
        {
            Search = search, Status = statusFilter, AuthorId = null, Page = page, PageSize = 20
        });

        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.CurrentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        ViewBag.IsAdmin = User.IsInRole("Admin");
        ViewBag.IsEditor = User.IsInRole("Editor");
        return View(result);
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        ViewBag.Categories = await _categories.GetAllAsync(userId);
        ViewBag.Tags = await _tags.GetAllAsync(userId);
        return View(new Post());
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Post post, List<Guid> categoryIds, string tagNames, string submitAction)
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

        if (submitAction == "publish") 
        {
            if (!User.HasClaim("Permission", "posts.publish"))
            {
                TempData["Error"] = "You do not have permission to publish posts. Saved as draft instead.";
                post.Status = PostStatus.Draft;
            }
            else
            {
                post.Status = post.ScheduledAt.HasValue && post.ScheduledAt.Value > DateTime.UtcNow 
                    ? PostStatus.Scheduled 
                    : PostStatus.Published;
            }
        }
        else if (submitAction == "draft")
        {
            post.Status = PostStatus.Draft;
        }

        ApplyStructuredDataGate(post);

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

        var auditAction = post.Status == PostStatus.Scheduled ? AuditActions.PostScheduled
            : post.Status == PostStatus.Published ? AuditActions.PostPublished
            : AuditActions.PostCreated;
        await _audit.LogAsync(auditAction, "Post", id.ToString(), post.Title);
        post.Id = id;
        await _revisions.RecordAsync(post, "Created", User);
        await PingIndexNowAsync(id);

        if (post.Status == PostStatus.Scheduled)
            TempData["Success"] = "Post scheduled.";
        else
            TempData["Success"] = submitAction == "publish" && post.Status == PostStatus.Published ? "Post published." : "Draft saved.";

        return RedirectToAction("Index");
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
        if (post.ScheduledAt.HasValue) // the editor shows and reads the display zone; storage is UTC
            post.ScheduledAt = TimeZoneHelper.ToDisplay(post.ScheduledAt.Value, await DisplayTimeZoneAsync());
        return View(post);
    }

    [HttpPost("edit/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Post post, List<Guid> categoryIds, string tagNames, string submitAction)
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

        if (submitAction == "publish") 
        {
            if (!User.HasClaim("Permission", "posts.publish"))
            {
                TempData["Error"] = "You do not have permission to publish posts. Saved as draft instead.";
                post.Status = PostStatus.Draft;
            }
            else
            {
                post.Status = post.ScheduledAt.HasValue && post.ScheduledAt.Value > DateTime.UtcNow 
                    ? PostStatus.Scheduled 
                    : PostStatus.Published;
            }
        }
        else if (submitAction == "draft")
        {
            post.Status = PostStatus.Draft;
        }

        ApplyStructuredDataGate(post);

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

        var editAuditAction = post.Status == PostStatus.Scheduled ? AuditActions.PostScheduled
            : post.Status == PostStatus.Published ? AuditActions.PostPublished
            : AuditActions.PostUpdated;
        await _audit.LogAsync(editAuditAction, "Post", id.ToString(), post.Title);
        await _revisions.RecordAsync(post,
            post.Status == PostStatus.Published ? "Published" : post.Status == PostStatus.Scheduled ? "Scheduled" : "Saved", User);
        await PingIndexNowAsync(id);

        if (post.Status == PostStatus.Scheduled)
            TempData["Success"] = "Post scheduled.";
        else
            TempData["Success"] = submitAction == "publish" && post.Status == PostStatus.Published ? "Post published." : "Saved.";

        return RedirectToAction("Index");
    }

    [HttpPost("delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
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
        return RedirectToAction("Index");
    }

    // HTMX auto-save endpoint
    [HttpPost("autosave/{id}")]
    public async Task<IActionResult> AutoSave(Guid id, [FromForm] string content, [FromForm] string title)
    {
        var post = await _posts.GetByIdAsync(id);
        if (post == null) return NotFound();
        post.Title = title;
        post.Html = content;
        post.Plaintext = content; // autosave sets both temporarily or simplistic
        await _posts.UpdateAsync(post);
        return Content("Saved", "text/plain");
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
    public async Task<IActionResult> Bulk(string action, List<Guid> ids, string? tag)
    {
        ids = ids?.Distinct().ToList() ?? new List<Guid>();
        if (ids.Count == 0) { TempData["Error"] = "Select at least one post."; return RedirectToAction("Index"); }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isPrivileged = User.IsInRole("Admin") || User.IsInRole("Editor");
        var canPublish = User.HasClaim("Permission", "posts.publish");
        action = (action ?? string.Empty).Trim().ToLowerInvariant();
        tag = (tag ?? string.Empty).Trim();

        if (action == "tag" && tag.Length == 0) { TempData["Error"] = "Enter the tag to add."; return RedirectToAction("Index"); }
        if (action == "publish" && !canPublish) { TempData["Error"] = "You do not have permission to publish posts."; return RedirectToAction("Index"); }
        if (action is not ("publish" or "draft" or "delete" or "tag")) { TempData["Error"] = "Unknown bulk action."; return RedirectToAction("Index"); }

        int done = 0, refused = 0;
        var publishedUrls = new List<string>();
        Tag? tagEntity = action == "tag" ? await _tags.GetOrCreateAsync(tag, userId) : null;

        foreach (var id in ids)
        {
            var post = await _posts.GetByIdAsync(id, null);
            if (post is null) { refused++; continue; }
            if (post.AuthorId != userId && !isPrivileged) { refused++; continue; }

            switch (action)
            {
                case "publish":
                    if (post.Status == PostStatus.Published) { done++; break; }
                    post.Status = PostStatus.Published;
                    post.PublishedAt ??= DateTime.UtcNow;
                    post.ScheduledAt = null;
                    await _posts.UpdateAsync(post);
                    await _audit.LogAsync(AuditActions.PostPublished, "Post", id.ToString(), post.Title + " (bulk)");
                    if (!string.IsNullOrEmpty(post.Slug)) publishedUrls.Add($"{Request.Scheme}://{Request.Host}/{post.Slug}");
                    done++;
                    break;
                case "draft":
                    if (post.Status == PostStatus.Draft) { done++; break; }
                    post.Status = PostStatus.Draft;
                    await _posts.UpdateAsync(post);
                    await _audit.LogAsync(AuditActions.PostUnpublished, "Post", id.ToString(), post.Title + " (bulk)");
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
        return RedirectToAction("Index");
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
