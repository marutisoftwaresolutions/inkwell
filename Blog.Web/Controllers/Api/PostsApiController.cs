using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Blog.Web.Controllers.Api;

[ApiController]
[Route("api/v1/posts")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces("application/json")]
public class PostsApiController : ControllerBase
{
    private readonly IPostRepository _posts;
    private readonly PostService _postService;
    private readonly Blog.Web.Services.Webhooks.WebhookPublisher _webhooks;

    public PostsApiController(IPostRepository posts, PostService postService, Blog.Web.Services.Webhooks.WebhookPublisher webhooks)
    {
        _posts = posts;
        _postService = postService;
        _webhooks = webhooks;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetPosts([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] Guid? categoryId = null, [FromQuery] Guid? tagId = null)
    {
        var result = await _posts.GetPostsAsync(new PostFilter
        {
            Status = PostStatus.Published, Page = page, PageSize = Math.Min(pageSize, 100),
            Search = search, CategoryId = categoryId, TagId = tagId
        });

        return Ok(new
        {
            data = result.Items.Select(p => new
            {
                p.Id, p.Title, p.Slug, Summary = p.MetaDescription, p.AuthorName,
                p.PublishedAt, p.ViewCount, FeaturedImageUrl = p.FeatureImage, p.CommentCount
            }),
            pagination = new { result.Page, result.PageSize, result.TotalItems, result.TotalPages }
        });
    }

    [HttpGet("{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPost(string slug)
    {
        var post = await _posts.GetBySlugAsync(slug);
        if (post == null || (post.Status != PostStatus.Published && !(post.Status == PostStatus.Scheduled && post.ScheduledAt <= DateTime.UtcNow)))
            return NotFound(ApiError(404, "Post not found."));
        return Ok(new { data = post });
    }

    [HttpPost]
    public async Task<IActionResult> CreatePost([FromBody] CreatePostRequest req)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var userEmail = User.FindFirstValue(ClaimTypes.Email)!;

        if (string.IsNullOrWhiteSpace(req.Content) && req.Markdown is null)
            return BadRequest(ApiError(400, "Send the body as \"content\" (HTML) or \"markdown\"."));

        var post = new Post
        {
            Title = req.Title, Slug = req.Slug ?? "", Html = req.Content ?? "",
            MetaDescription = req.Summary ?? "", Plaintext = req.Content ?? "", AuthorId = userId, Status = PostStatus.Draft
        };
        ApplyBody(post, req.Content, req.Markdown);

        var (id, error) = await _postService.CreatePostAsync(post, req.CategoryIds ?? new(), req.Tags ?? new(), userId, userEmail);
        if (error != null) return BadRequest(ApiError(400, error));

        return CreatedAtAction(nameof(GetPost), new { slug = post.Slug }, new { data = new { id, post.Slug } });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePost(Guid id, [FromBody] UpdatePostRequest req)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var userEmail = User.FindFirstValue(ClaimTypes.Email)!;

        var existing = await _posts.GetByIdAsync(id);
        if (existing == null) return NotFound(ApiError(404, "Post not found."));
        var before = new Post { Status = existing.Status, ScheduledAt = existing.ScheduledAt };

        existing.Title = req.Title ?? existing.Title;
        ApplyBody(existing, req.Content, req.Markdown);
        existing.MetaDescription = req.Summary ?? existing.MetaDescription;
        existing.Slug = req.Slug ?? existing.Slug;

        var error = await _postService.UpdatePostAsync(existing, req.CategoryIds ?? new(), req.Tags ?? new(), userId, userEmail);
        if (error != null) return BadRequest(ApiError(400, error));

        await _webhooks.PostSavedAsync(HttpContext, before, existing);
        return Ok(new { data = "Updated." });
    }

    [HttpPatch("{id}/publish")]
    public async Task<IActionResult> Publish(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var userEmail = User.FindFirstValue(ClaimTypes.Email)!;
        var beforePost = await _posts.GetByIdAsync(id);
        var error = await _postService.PublishPostAsync(id, userId, userEmail);
        if (error != null) return NotFound(ApiError(404, error));

        var afterPost = await _posts.GetByIdAsync(id);
        if (afterPost is not null)
            await _webhooks.PostSavedAsync(HttpContext, beforePost is null ? null : new Post { Status = beforePost.Status, ScheduledAt = beforePost.ScheduledAt }, afterPost);
        return Ok(new { data = "Published." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        bool isAdmin = User.IsInRole("Admin");

        await _posts.DeleteAsync(id, isAdmin ? (Guid?)null : userId);
        return Ok(new { data = "Deleted permanently." });
    }

    /// <summary>
    /// Markdown authoring (ADR-0022): "markdown" makes Markdown the post's source and renders its HTML; "content"
    /// (HTML) makes HTML the source again, so a later Desk save can never re-render over it from stale Markdown.
    /// Neither leaves the body as it was.
    /// </summary>
    private static void ApplyBody(Post post, string? html, string? markdown)
    {
        if (markdown is not null)
        {
            post.EditorMode = EditorModes.Markdown;
            post.Markdown = markdown;
            post.Html = MarkdownRenderer.ToHtml(markdown);
            post.Plaintext = MarkdownRenderer.ToPlaintext(post.Html);
        }
        else if (html is not null)
        {
            post.EditorMode = EditorModes.Rich;
            post.Markdown = null;
            post.Html = html;
            post.Plaintext = MarkdownRenderer.ToPlaintext(html);
        }
    }

    private static object ApiError(int code, string error) =>
        new { statusCode = code, error, traceId = System.Diagnostics.Activity.Current?.Id };
}

public record CreatePostRequest(string Title, string? Slug, string? Content, string? Summary,
    List<Guid>? CategoryIds, List<string>? Tags, string? Markdown = null);
public record UpdatePostRequest(string? Title, string? Slug, string? Content, string? Summary,
    List<Guid>? CategoryIds, List<string>? Tags, string? Markdown = null);
