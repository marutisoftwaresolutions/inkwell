using Blog.Core.Domain;
using Blog.Core.Interfaces;
using System.Security.Claims;

namespace Blog.Web.Services;

/// <summary>
/// Thin service that captures per-request context (OwnerId, UserId, IP, UA) and delegates
/// to IAuditRepository. Swallows all exceptions so audit failures never surface to the user.
/// </summary>
public class AuditService
{
    private readonly IAuditRepository _repo;
    private readonly IHttpContextAccessor _http;
    private readonly ITenantContext _tenant;

    public AuditService(IAuditRepository repo, IHttpContextAccessor http, ITenantContext tenant)
    {
        _repo   = repo;
        _http   = http;
        _tenant = tenant;
    }

    /// <param name="action">Use a constant from <see cref="AuditActions"/> — e.g. AuditActions.PostPublished.</param>
    /// <param name="entityType">Entity category — e.g. "Post", "Comment".</param>
    /// <param name="entityId">String PK or slug of the entity.</param>
    /// <param name="entityName">Human-readable snapshot — title, email, slug, etc.</param>
    /// <param name="oldValues">JSON of key fields before the change (optional).</param>
    /// <param name="newValues">JSON of key fields after the change (optional).</param>
    /// <param name="userIdOverride">Explicitly set UserId — use for auth events where claims are not yet populated.</param>
    /// <param name="userNameOverride">Explicitly set UserName — use for auth events.</param>
    public async Task LogAsync(
        string action, string entityType,
        string? entityId = null, string? entityName = null,
        string? oldValues = null, string? newValues = null,
        Guid? userIdOverride = null, string? userNameOverride = null)
    {
        try
        {
            var ctx = _http.HttpContext;

            var claimsId = ctx?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userId   = userIdOverride ?? (Guid.TryParse(claimsId, out var id) ? id : Guid.Empty);
            var userName = userNameOverride
                ?? ctx?.User.FindFirstValue(ClaimTypes.Name)
                ?? ctx?.User.FindFirstValue(ClaimTypes.Email)
                ?? "system";

            var ownerId = (_tenant.IsCloudMode && _tenant.IsResolved) ? _tenant.UserId : Guid.Empty;

            var ua = ctx?.Request.Headers.UserAgent.ToString() ?? "";

            await _repo.LogAsync(new AuditLog
            {
                OwnerId    = ownerId,
                UserId     = userId,
                UserName   = userName,
                Action     = action,
                EntityType = entityType,
                EntityId   = entityId,
                EntityName = entityName,
                OldValues  = oldValues,
                NewValues  = newValues,
                IpAddress  = ctx?.Connection.RemoteIpAddress?.ToString(),
                UserAgent  = ua.Length > 500 ? ua[..500] : ua,
                CreatedAt  = DateTime.UtcNow
            });
        }
        catch { /* audit must never crash the calling request */ }
    }
}
