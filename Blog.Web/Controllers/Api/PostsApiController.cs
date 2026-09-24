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

    public PostsApiController(IPostRepository posts, PostService postService)
    {
        _posts = posts;
        _postService = postService;
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
        await _posts.IncrementViewCountAsync(post.Id);
        return Ok(new { data = post });
    }

    [HttpPost]
    public async Task<IActionResult> CreatePost([FromBody] CreatePostRequest req)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var userEmail = User.FindFirstValue(ClaimTypes.Email)!;

        var post = new Post
        {
            Title = req.Title, Slug = req.Slug ?? "", Html = req.Content,
            MetaDescription = req.Summary ?? "", Plaintext = req.Content, AuthorId = userId, Status = PostStatus.Draft
        };

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

        existing.Title = req.Title ?? existing.Title;
        existing.Html = req.Content ?? existing.Html;
        existing.Plaintext = req.Content ?? existing.Plaintext;
        existing.MetaDescription = req.Summary ?? existing.MetaDescription;
        existing.Slug = req.Slug ?? existing.Slug;

        var error = await _postService.UpdatePostAsync(existing, req.CategoryIds ?? new(), req.Tags ?? new(), userId, userEmail);
        if (error != null) return BadRequest(ApiError(400, error));

        return Ok(new { data = "Updated." });
    }

    [HttpPatch("{id}/publish")]
    public async Task<IActionResult> Publish(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var userEmail = User.FindFirstValue(ClaimTypes.Email)!;
        var error = await _postService.PublishPostAsync(id, userId, userEmail);
        if (error != null) return NotFound(ApiError(404, error));
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

    private static object ApiError(int code, string error) =>
        new { statusCode = code, error, traceId = System.Diagnostics.Activity.Current?.Id };
}

public record CreatePostRequest(string Title, string? Slug, string Content, string? Summary,
    List<Guid>? CategoryIds, List<string>? Tags);
public record UpdatePostRequest(string? Title, string? Slug, string? Content, string? Summary,
    List<Guid>? CategoryIds, List<string>? Tags);
