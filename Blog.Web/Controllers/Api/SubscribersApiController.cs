using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers.Api;

/// <summary>
/// The subscriber list over the API, for keeping a mail tool or a CRM in step. The list is personal
/// data, so it follows the Desk exactly: only an Admin reaches it, and an API key reaches it only when
/// it was created with the subscribers scope (an Admin's ordinary key does not). The confirm and
/// unsubscribe tokens never leave the server. Every read is in the audit trail, like the CSV export.
/// </summary>
[ApiController]
[Route("api/v1/subscribers")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "AdminOnly")]
public class SubscribersApiController : ControllerBase
{
    public const int MaxPageSize = 200;
    public const int MaxPage = 100_000;
    private readonly IMemberRepository _members;
    private readonly AuditService _audit;

    public SubscribersApiController(IMemberRepository members, AuditService audit)
    {
        _members = members;
        _audit = audit;
    }

    private static object Error(int status, string message) =>
        new { statusCode = status, error = message, traceId = System.Diagnostics.Activity.Current?.Id };

    /// <summary>One page of subscribers, newest first. Confirmed ones unless another status is asked for.</summary>
    [HttpGet]
    public async Task<IActionResult> List(string? status = "confirmed", int page = 1, int pageSize = 100)
    {
        if (HttpContext.RequestServices.GetRequiredService<ITenantContext>().IsCloudMode) return NotFound(Error(404, "Not found."));   // the list is site-wide; cloud mode has no newsletter
        if (ApiKeyAuthenticator.IsKeyCaller(User) && !ApiKeyAuthenticator.HasScope(User, ApiKeyFormat.Subscribers))
            return StatusCode(403, Error(403, "Reading subscribers needs a key with the subscribers scope."));

        string? wanted;
        switch ((status ?? "").Trim().ToLowerInvariant())
        {
            case "confirmed": case "pending": case "unsubscribed": case "bounced": wanted = status!.Trim().ToLowerInvariant(); break;
            case "all": wanted = null; break;
            default: return BadRequest(Error(400, "status is one of confirmed, pending, unsubscribed, bounced or all."));
        }
        page = Math.Clamp(page, 1, MaxPage);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (items, total) = await _members.GetPagedAsync(page, pageSize, wanted);
        await _audit.LogAsync(AuditActions.SubscriberExported, "Subscriber", null,
            $"{items.Count} {wanted ?? "of every status"} read through the API (page {page})");

        return Ok(new
        {
            data = items.Select(m => new { m.Email, m.Name, m.Status, m.ConfirmedAt, SubscribedAt = m.CreatedAt, m.SourcePath }),
            meta = new { page, pageSize, total, totalPages = (int)Math.Ceiling(total / (double)pageSize) },
        });
    }
}
