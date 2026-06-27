using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;

namespace Blog.Web.Controllers;

[Authorize(Policy = "AdminOnly")]
[Route("admin/audit")]
public class AuditController : Controller
{
    private readonly IAuditRepository _audit;
    private readonly IUserRepository  _users;
    private readonly ITenantContext   _tenant;

    public AuditController(IAuditRepository audit, IUserRepository users, ITenantContext tenant)
    {
        _audit  = audit;
        _users  = users;
        _tenant = tenant;
    }

    private Task<Guid> GetOwnerIdAsync()
    {
        if (_tenant.IsCloudMode && _tenant.IsResolved) return Task.FromResult(_tenant.UserId);
        return Task.FromResult(Guid.Empty);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? entityType, string? action, string? userId,
        string? from, string? to, int page = 1)
    {
        var ownerId = await GetOwnerIdAsync();
        const int pageSize = 50;

        DateTime? fromDate = DateTime.TryParse(from, out var fd) ? fd : null;
        DateTime? toDate   = DateTime.TryParse(to,   out var td) ? td : null;
        Guid?     userGuid = Guid.TryParse(userId, out var ug) ? ug : null;

        var (items, total) = await _audit.GetPagedAsync(
            ownerId, page, pageSize, entityType, action, userGuid, fromDate, toDate);

        var allUsers = await _users.GetAllUsersAsync();

        ViewBag.Items      = items;
        ViewBag.Total      = total;
        ViewBag.Page       = page;
        ViewBag.PageSize   = pageSize;
        ViewBag.TotalPages = (int)Math.Ceiling((double)total / pageSize);

        // Filter state (preserve in view)
        ViewBag.FilterEntityType = entityType;
        ViewBag.FilterAction     = action;
        ViewBag.FilterUserId     = userId;
        ViewBag.FilterFrom       = from;
        ViewBag.FilterTo         = to;

        // Dropdown sources
        ViewBag.EntityTypes = new[]
        {
            "Post", "Page", "Comment", "Media", "User",
            "Auth", "Settings", "Theme", "Category", "Tag",
            "Newsletter", "Subscriber", "Redirect"
        };
        ViewBag.AllUsers = allUsers.Select(u => (u.Id, u.DisplayName ?? u.Email)).ToList();

        ViewData["Title"] = "Audit Trail";
        return View();
    }

    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv(
        string? entityType, string? action, string? userId,
        string? from, string? to)
    {
        var ownerId = await GetOwnerIdAsync();

        DateTime? fromDate = DateTime.TryParse(from, out var fd) ? fd : null;
        DateTime? toDate   = DateTime.TryParse(to,   out var td) ? td : null;
        Guid?     userGuid = Guid.TryParse(userId, out var ug) ? ug : null;

        // Export up to 10 000 rows
        var (items, _) = await _audit.GetPagedAsync(
            ownerId, 1, 10_000, entityType, action, userGuid, fromDate, toDate);

        var sb = new StringBuilder();
        sb.AppendLine("Time (UTC),User,Action,EntityType,EntityId,EntityName,IP");
        foreach (var row in items)
        {
            sb.AppendLine(string.Join(",",
                $"\"{row.CreatedAt:yyyy-MM-dd HH:mm:ss}\"",
                $"\"{row.UserName}\"",
                $"\"{row.Action}\"",
                $"\"{row.EntityType}\"",
                $"\"{row.EntityId}\"",
                $"\"{row.EntityName}\"",
                $"\"{row.IpAddress}\""));
        }

        var fileName = $"audit-{DateTime.UtcNow:yyyy-MM-dd}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
    }
}
