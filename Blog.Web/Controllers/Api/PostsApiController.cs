using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace Blog.Web.Controllers.Api;

[ApiController]
[Route("api/v1/posts")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]   // reads below are [AllowAnonymous]
[Produces("application/json")]
public class PostsApiController : ControllerBase
{
    private readonly IPostRepository _posts;
    private readonly PostService _postService;
    private readonly Blog.Web.Services.Webhooks.WebhookPublisher _webhooks;
    private readonly Blog.Web.Services.CustomFieldsEditor _fields;
    private readonly Blog.Web.Services.AuditService _audit;
    private readonly IWebHostEnvironment _env;
    private readonly Blog.Web.Services.RevisionService _revisions;
    private readonly Blog.Web.Services.IndexNowService _indexNow;

    // Writes use the Desk's own policies and ownership rules (ADR-0033): the token's caller carries the same
    // role and permission claims as a Desk sign-in, rebuilt from the database on every request.
    private const string Bearer = JwtBearerDefaults.AuthenticationScheme;
    private bool Privileged => User.IsInRole("Admin") || User.IsInRole("Editor");
    private bool CanPublish => User.HasClaim("Permission", "posts.publish");
    /// <summary>A published or scheduled post is an editor's decision: changing or deleting it needs the publish permission.</summary>
    private static bool IsLiveOrScheduled(Post p) => p.Status is PostStatus.Published or PostStatus.Scheduled;

    public PostsApiController(IPostRepository posts, PostService postService, Blog.Web.Services.Webhooks.WebhookPublisher webhooks,
        Blog.Web.Services.CustomFieldsEditor fields, Blog.Web.Services.AuditService audit, IWebHostEnvironment env,
        Blog.Web.Services.RevisionService revisions, Blog.Web.Services.IndexNowService indexNow)
    {
        _revisions = revisions;
        _indexNow = indexNow;
        _posts = posts;
        _postService = postService;
        _webhooks = webhooks;
        _fields = fields;
        _audit = audit;
        _env = env;
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
        return Ok(new { data = await PublicPostAsync(post) });
    }

    /// <summary>
    /// The public shape of a live post: what a reader can see on its page, plus <c>customFields</c> (ADR-0025). It is
    /// an explicit list, so a new editorial field (review notes, schedules, the Markdown source) never reaches the
    /// public API by default (ADR-0033). Fields kept from readers are included only for a caller who could edit this
    /// post in the Desk: its author or an Admin/Editor, with the edit permission.
    /// </summary>
    private async Task<object> PublicPostAsync(Post post)
    {
        var auth = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        var caller = auth.Succeeded ? auth.Principal : null;
        var canEdit = caller is not null && caller.HasClaim("Permission", "posts.edit")
            && (caller.FindFirstValue(ClaimTypes.NameIdentifier) == post.AuthorId.ToString() || caller.IsInRole("Admin") || caller.IsInRole("Editor"));
        var defs = await _fields.DefinitionsForOwnerAsync(post.AuthorId, pages: false);
        return new
        {
            post.Id, post.Title, post.Slug, post.Html, post.Plaintext, post.FeatureImage,
            post.MetaTitle, post.MetaDescription, post.CanonicalUrl,
            post.OgTitle, post.OgDescription, post.OgImage, post.TwitterTitle, post.TwitterDescription, post.TwitterImage,
            post.AuthorName, post.AvatarUrl, post.AuthorCredentials, post.AuthorSpecialty,
            post.PublishedAt, post.UpdatedAt, post.LastVerifiedAt, post.ViewCount, post.AllowComments, post.CommentCount,
            Categories = post.Categories.Select(c => new { c.Name, c.Slug }),
            Tags = post.Tags.Select(t => new { t.Name, t.Slug }),
            post.AnswerCapsule, post.FaqJson, post.KeyFactsJson, post.HowToJson, post.RoundupJson,
            CustomFields = CustomFields.ToApi(defs, post.CustomFieldsJson, includeHidden: canEdit),
        };
    }

    /// <summary>Validates <c>customFields</c> from a request onto the post; the errors, keyed by field, when any value is refused.</summary>
    private async Task<IReadOnlyDictionary<string, string>> ApplyCustomFieldsAsync(Post post, IReadOnlyDictionary<string, JsonElement>? sent)
    {
        if (sent is null || sent.Count == 0) return new Dictionary<string, string>();
        var result = CustomFields.FromApi(await _fields.DefinitionsAsync(User, pages: false), sent, post.CustomFieldsJson);
        if (result.Errors.Count == 0) post.CustomFieldsJson = result.Json;
        return result.Errors;
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = Bearer, Policy = "CanEditPosts")]
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
        var fieldErrors = await ApplyCustomFieldsAsync(post, req.CustomFields);
        if (fieldErrors.Count > 0) return BadRequest(FieldErrors(fieldErrors));

        var (id, error) = await _postService.CreatePostAsync(post, req.CategoryIds ?? new(), req.Tags ?? new(), userId, userEmail);
        if (error != null) return BadRequest(ApiError(400, error));
        await _audit.LogAsync(AuditActions.PostCreated, "Post", id.ToString(), post.Title, newValues: "Created through the API as a draft");
        post.Id = id;
        await _revisions.RecordAsync(post, "Created", User);

        return CreatedAtAction(nameof(GetPost), new { slug = post.Slug }, new { data = new { id, post.Slug } });
    }

    [HttpPut("{id}")]
    [Authorize(AuthenticationSchemes = Bearer, Policy = "CanEditPosts")]
    public async Task<IActionResult> UpdatePost(Guid id, [FromBody] UpdatePostRequest req)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var userEmail = User.FindFirstValue(ClaimTypes.Email)!;

        var existing = await _posts.GetByIdAsync(id);
        if (existing == null) return NotFound(ApiError(404, "Post not found."));
        if (existing.AuthorId != userId && !Privileged) return StatusCode(403, ApiError(403, "You can change only your own posts."));
        // In the Desk, saving a live post without the publish permission takes it off the site for review; the API
        // has no review step, so it refuses instead of changing what readers see.
        if (IsLiveOrScheduled(existing) && !CanPublish)
            return StatusCode(403, ApiError(403, "Changing a published or scheduled post needs the publish permission."));
        var before = new Post { Status = existing.Status, ScheduledAt = existing.ScheduledAt };
        var beforeSlug = existing.Slug;

        existing.Title = req.Title ?? existing.Title;
        ApplyBody(existing, req.Content, req.Markdown);
        existing.MetaDescription = req.Summary ?? existing.MetaDescription;
        existing.Slug = req.Slug ?? existing.Slug;
        var fieldErrors = await ApplyCustomFieldsAsync(existing, req.CustomFields);
        if (fieldErrors.Count > 0) return BadRequest(FieldErrors(fieldErrors));

        var error = await _postService.UpdatePostAsync(existing, req.CategoryIds ?? new(), req.Tags ?? new(), userId, userEmail);
        if (error != null) return BadRequest(ApiError(400, error));

        await _audit.LogAsync(AuditActions.PostUpdated, "Post", id.ToString(), existing.Title, newValues: "Updated through the API");
        await _revisions.RecordAsync(existing, "Saved", User);
        Blog.Web.Services.OgImageCache.Invalidate(_env, existing.Slug);
        if (!string.Equals(beforeSlug, existing.Slug, StringComparison.OrdinalIgnoreCase)) Blog.Web.Services.OgImageCache.Invalidate(_env, beforeSlug);
        if (existing.Status == PostStatus.Published) await _indexNow.SubmitAsync($"{Request.Scheme}://{Request.Host}/{existing.Slug}");
        await _webhooks.PostSavedAsync(HttpContext, before, existing);
        return Ok(new { data = "Updated." });
    }

    [HttpPatch("{id}/publish")]
    [Authorize(AuthenticationSchemes = Bearer, Policy = "CanPublishPosts")]
    public async Task<IActionResult> Publish(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var userEmail = User.FindFirstValue(ClaimTypes.Email)!;
        var beforePost = await _posts.GetByIdAsync(id);
        if (beforePost == null) return NotFound(ApiError(404, "Post not found."));
        if (beforePost.AuthorId != userId && !Privileged) return StatusCode(403, ApiError(403, "You can publish only your own posts."));
        // The Desk's publish gate: structured data with errors keeps a post off the site until it is fixed.
        var findings = StructuredDataLinter.Lint(beforePost);
        if (!StructuredDataLinter.CanPublish(findings))
            return UnprocessableEntity(new
            {
                statusCode = 422, error = "The structured data has to be fixed before this post can publish; it was not published.",
                problems = findings.Where(f => f.Severity == LintSeverity.Error).Select(f => $"{f.Where}: {f.Message}"),
                traceId = System.Diagnostics.Activity.Current?.Id,
            });
        var error = await _postService.PublishPostAsync(id, userId, userEmail);
        if (error != null) return NotFound(ApiError(404, error));

        var afterPost = await _posts.GetByIdAsync(id);
        await _audit.LogAsync(AuditActions.PostPublished, "Post", id.ToString(), afterPost?.Title ?? beforePost.Title, newValues: "Published through the API");
        if (afterPost is not null)
        {
            await _revisions.RecordAsync(afterPost, "Published", User);
            await _indexNow.SubmitAsync($"{Request.Scheme}://{Request.Host}/{afterPost.Slug}");
        }
        if (afterPost is not null)
            await _webhooks.PostSavedAsync(HttpContext, beforePost is null ? null : new Post { Status = beforePost.Status, ScheduledAt = beforePost.ScheduledAt }, afterPost);
        return Ok(new { data = "Published." });
    }

    [HttpDelete("{id}")]
    [Authorize(AuthenticationSchemes = Bearer, Policy = "CanEditPosts")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var existing = await _posts.GetByIdAsync(id);
        if (existing == null) return NotFound(ApiError(404, "Post not found."));
        if (existing.AuthorId != userId && !Privileged) return StatusCode(403, ApiError(403, "You can delete only your own posts."));
        if (IsLiveOrScheduled(existing) && !CanPublish)
            return StatusCode(403, ApiError(403, "Deleting a published or scheduled post needs the publish permission."));

        await _posts.DeleteAsync(id, Privileged ? null : userId);
        Blog.Web.Services.OgImageCache.Invalidate(_env, existing.Slug);
        await _audit.LogAsync(AuditActions.PostDeleted, "Post", id.ToString(), existing.Title, newValues: "Deleted through the API");
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

    private static object FieldErrors(IReadOnlyDictionary<string, string> errors) =>
        new { statusCode = 400, error = "Some custom fields were not accepted; nothing was saved.", fields = errors, traceId = System.Diagnostics.Activity.Current?.Id };
}

public record CreatePostRequest(string Title, string? Slug, string? Content, string? Summary,
    List<Guid>? CategoryIds, List<string>? Tags, string? Markdown = null, Dictionary<string, JsonElement>? CustomFields = null);
public record UpdatePostRequest(string? Title, string? Slug, string? Content, string? Summary,
    List<Guid>? CategoryIds, List<string>? Tags, string? Markdown = null, Dictionary<string, JsonElement>? CustomFields = null);
