using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers.Api;

/// <summary>
/// Published pages, read-only (v1.0.9): what a headless front end needs to show a CMS page, including its custom
/// field values (ADR-0025). Pages are still written in the Desk only. The rule for custom fields is the posts
/// API's: fields kept from readers go only to a caller who could edit the page.
/// </summary>
[ApiController]
[Route("api/v1/pages")]
[Produces("application/json")]
public class PagesApiController : ControllerBase
{
    private readonly IPageRepository _pages;
    private readonly Blog.Web.Services.CustomFieldsEditor _fields;

    public PagesApiController(IPageRepository pages, Blog.Web.Services.CustomFieldsEditor fields)
    {
        _pages = pages;
        _fields = fields;
    }

    [HttpGet("{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPage(string slug)
    {
        var page = await _pages.GetBySlugAsync(slug);   // published pages only
        if (page == null)
            return NotFound(new { statusCode = 404, error = "Page not found.", traceId = System.Diagnostics.Activity.Current?.Id });

        // Fields kept from readers go only to a caller who could edit this page in the Desk: its author, with pages.manage.
        var auth = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
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
}
