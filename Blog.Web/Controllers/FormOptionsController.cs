using Blog.Core.Interfaces;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// The forms the page builder's Form block can place (ADR-0030): names and keys only, for anyone who may edit pages.
/// Its own controller because FormsController is Admin-only, and ASP.NET Core requires a controller's policy and an
/// action's policy both, so an action there could never be opened to Editors.
/// </summary>
[Authorize(Policy = "CanManagePages")]
[Route("admin/forms")]
public class FormOptionsController : Controller
{
    private readonly IFormRepository _forms;
    public FormOptionsController(IFormRepository forms) => _forms = forms;

    [HttpGet("options")]
    public async Task<IActionResult> Options() =>
        Json((await _forms.ListAsync(TrafficRecorder.OwnerOf(HttpContext))).Select(f => new { key = f.FormKey, name = f.Name, active = f.IsActive }));
}
