using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Infrastructure.Data;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers.Api;

/// <summary>
/// Pages over the API. Anyone can read a published page, including its custom field values (ADR-0025);
/// fields kept from readers go only to a caller who could edit the page. A caller with the pages
/// permission can list their own pages and create and change them. A page made through the API starts
/// unpublished. Publishing, and changing a page that is already published, needs more than the write
/// scope when the caller is an API key, so a drafts-only key cannot alter what readers see (the same
/// line the posts API draws). Bodies are HTML.
/// </summary>
[ApiController]
[Route("api/v1/pages")]
[Produces("application/json")]
public class PagesApiController : ControllerBase
{
    private const string Bearer = JwtBearerDefaults.AuthenticationScheme;
    private readonly IPageRepository _pages;
    private readonly CustomFieldsEditor _fields;

    public PagesApiController(IPageRepository pages, CustomFieldsEditor fields)
    {
        _pages = pages;
        _fields = fields;
    }

    private Guid CallerId => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    private static object Error(int status, string message) =>
        new { statusCode = status, error = message, traceId = System.Diagnostics.Activity.Current?.Id };

    /// <summary>A key limited to drafts may not decide what readers see. A signed-in person, or a key with the publish scope, may.</summary>
    private bool MayChangeLivePages =>
        !ApiKeyAuthenticator.IsKeyCaller(User) || ApiKeyAuthenticator.HasScope(User, ApiKeyFormat.Publish);

    [HttpGet("{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPage(string slug)
    {
        var page = await _pages.GetBySlugAsync(slug);   // published pages only
        if (page == null) return NotFound(Error(404, "Page not found."));

        // Fields kept from readers go only to a caller who could edit this page in the Desk: its author, with pages.manage.
        var auth = await HttpContext.AuthenticateAsync(Bearer);
        var canEdit = auth.Succeeded && auth.Principal!.HasClaim("Permission", "pages.manage")
            && auth.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value == page.AuthorId.ToString();
        var defs = await _fields.DefinitionsForOwnerAsync(page.AuthorId, pages: true);
        return Ok(new
        {
            data = new
            {
                page.Id, page.Title, page.Slug, page.Content, page.AuthorName, page.ParentId,
                page.PublishedAt, page.UpdatedAt, page.FeaturedImageUrl,
                page.MetaTitle, page.MetaDescription, page.CanonicalUrl,
                CustomFields = CustomFields.ToApi(defs, page.CustomFieldsJson, includeHidden: canEdit),
            }
        });
    }

    /// <summary>The caller's own pages, unpublished ones included.</summary>
    [HttpGet]
    [Authorize(AuthenticationSchemes = Bearer, Policy = "CanManagePages")]
    public async Task<IActionResult> ListPages()
    {
        var pages = await _pages.GetAllAsync(CallerId);
        return Ok(new { data = pages.Select(p => new { p.Id, p.Title, p.Slug, p.IsPublished, p.PublishedAt, p.UpdatedAt }) });
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = Bearer, Policy = "CanManagePages")]
    public async Task<IActionResult> CreatePage([FromBody] SavePageRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest(Error(400, "A page needs a title."));
        if (req.Publish == true && !MayChangeLivePages)
            return StatusCode(403, Error(403, "Publishing a page needs a key with the publish scope."));

        var userId = CallerId;
        var page = new Page
        {
            Title = req.Title.Trim(),
            Slug = SlugHelper.Generate(string.IsNullOrWhiteSpace(req.Slug) ? req.Title : req.Slug),
            Content = req.Content ?? "",
            MetaDescription = req.Summary,
            AuthorId = userId,
            IsPublished = req.Publish == true,
            IsInNav = req.ShowInNavigation ?? false,
        };
        if (string.IsNullOrEmpty(page.Slug)) return BadRequest(Error(400, "The slug needs letters or digits."));
        if (await _pages.SlugExistsAsync(page.Slug, userId) || await _pages.GetBySlugAsync(page.Slug) is not null)
            return Conflict(Error(409, "A page with that slug already exists."));
        page.PublishedAt = page.IsPublished ? DateTime.UtcNow : null;

        page.Id = await _pages.CreateAsync(page);
        await AfterSaveAsync(page, AuditActions.PageCreated, page.IsPublished ? "Published" : "Created",
            page.IsPublished ? "Created and published through the API" : "Created through the API, unpublished");
        return StatusCode(201, new { data = new { id = page.Id, page.Slug, page.IsPublished } });
    }

    [HttpPut("{id:guid}")]
    [Authorize(AuthenticationSchemes = Bearer, Policy = "CanManagePages")]
    public async Task<IActionResult> UpdatePage(Guid id, [FromBody] SavePageRequest req)
    {
        var userId = CallerId;
        var page = await _pages.GetByIdAsync(id, userId);
        if (page is null) return NotFound(Error(404, "Page not found."));
        if ((page.IsPublished || req.Publish == true) && !MayChangeLivePages)
            return StatusCode(403, Error(403, "Changing or publishing a published page needs a key with the publish scope."));

        if (req.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest(Error(400, "A page needs a title."));
            page.Title = req.Title.Trim();
        }
        if (req.Slug is not null)
        {
            var slug = SlugHelper.Generate(req.Slug);
            if (string.IsNullOrEmpty(slug)) return BadRequest(Error(400, "The slug needs letters or digits."));
            if (await _pages.SlugExistsAsync(slug, userId, id)) return Conflict(Error(409, "A page with that slug already exists."));
            page.Slug = slug;
        }
        if (req.Content is not null) page.Content = req.Content;
        if (req.Summary is not null) page.MetaDescription = req.Summary;
        if (req.ShowInNavigation is not null) page.IsInNav = req.ShowInNavigation.Value;
        if (req.Publish is not null)
        {
            page.IsPublished = req.Publish.Value;
            if (page.IsPublished && page.PublishedAt is null) page.PublishedAt = DateTime.UtcNow;
        }

        await _pages.UpdateAsync(page);
        await AfterSaveAsync(page, AuditActions.PageUpdated, req.Publish == true ? "Published" : "Saved", "Updated through the API");
        return Ok(new { data = new { id = page.Id, page.Slug, page.IsPublished } });
    }

    /// <summary>What every page save does in the Desk: audit entry, revision, webhook.</summary>
    private async Task AfterSaveAsync(Page page, string auditAction, string revisionReason, string note)
    {
        var services = HttpContext.RequestServices;
        await services.GetRequiredService<AuditService>().LogAsync(auditAction, "Page", page.Id.ToString(), page.Title, newValues: note);
        await services.GetRequiredService<RevisionService>().RecordAsync(page, revisionReason, User);
        await services.GetRequiredService<Blog.Web.Services.Webhooks.WebhookPublisher>().PageSavedAsync(HttpContext, page);
    }
}

/// <summary>Create or change a page. On update every field is optional: send only what changes.</summary>
public record SavePageRequest(string? Title, string? Slug, string? Content, string? Summary, bool? Publish = null, bool? ShowInNavigation = null);
