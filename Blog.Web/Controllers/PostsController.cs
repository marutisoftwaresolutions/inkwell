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

    public PostsController(IPostRepository posts, ICategoryRepository categories,
        ITagRepository tags, IMediaRepository media, PostService postService, AuditService audit)
    {
        _posts = posts;
        _categories = categories;
        _tags = tags;
        _media = media;
        _postService = postService;
        _audit = audit;
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

        if (submitAction == "publish") 
        {
            if (!User.HasClaim("Permission", "posts.publish"))
            {
                TempData["Error"] = "You do not have permission to publish posts. Saved as draft instead.";
                post.Status = PostStatus.Draft;
            }
            else
            {
                post.Status = post.ScheduledAt.HasValue && post.ScheduledAt.Value > DateTime.Now 
                    ? PostStatus.Scheduled 
                    : PostStatus.Published;
            }
        }
        else if (submitAction == "draft")
        {
            post.Status = PostStatus.Draft;
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

        var auditAction = post.Status == PostStatus.Scheduled ? AuditActions.PostScheduled
            : post.Status == PostStatus.Published ? AuditActions.PostPublished
            : AuditActions.PostCreated;
        await _audit.LogAsync(auditAction, "Post", id.ToString(), post.Title);

        if (post.Status == PostStatus.Scheduled)
            TempData["Success"] = "Post scheduled!";
        else
            TempData["Success"] = submitAction == "publish" && post.Status == PostStatus.Published ? "Post published! 🎉" : "Draft saved.";

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

        if (submitAction == "publish") 
        {
            if (!User.HasClaim("Permission", "posts.publish"))
            {
                TempData["Error"] = "You do not have permission to publish posts. Saved as draft instead.";
                post.Status = PostStatus.Draft;
            }
            else
            {
                post.Status = post.ScheduledAt.HasValue && post.ScheduledAt.Value > DateTime.Now 
                    ? PostStatus.Scheduled 
                    : PostStatus.Published;
            }
        }
        else if (submitAction == "draft")
        {
            post.Status = PostStatus.Draft;
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

        var editAuditAction = post.Status == PostStatus.Scheduled ? AuditActions.PostScheduled
            : post.Status == PostStatus.Published ? AuditActions.PostPublished
            : AuditActions.PostUpdated;
        await _audit.LogAsync(editAuditAction, "Post", id.ToString(), post.Title);

        if (post.Status == PostStatus.Scheduled)
            TempData["Success"] = "Post scheduled!";
        else
            TempData["Success"] = submitAction == "publish" && post.Status == PostStatus.Published ? "Post published! 🎉" : "Saved.";

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
}
