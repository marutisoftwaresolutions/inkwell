using System.Security.Claims;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Profile → API keys: a signed-in user creates and revokes their own keys for scripts and AI
/// assistants. A key acts as its owner, narrowed by the scopes chosen here, and is shown once: only
/// its hash is stored. Creating and revoking are audited. Nobody manages another person's keys from
/// this screen; disabling an account stops all of its keys at once.
/// </summary>
[Authorize]
[Route("account/api-keys")]
public class ApiKeysController : Controller
{
    /// <summary>Usable keys one account may hold; enough for every tool a person runs, small enough to review.</summary>
    public const int MaxActiveKeys = 20;

    /// <summary>Lifetimes offered, in days; 0 means the key does not expire.</summary>
    public static readonly IReadOnlyList<(int Days, string Label)> Lifetimes =
    [
        (30, "30 days"), (90, "90 days"), (365, "1 year"), (0, "No expiry"),
    ];

    private readonly IApiKeyRepository _keys;
    private readonly AuditService _audit;

    public ApiKeysController(IApiKeyRepository keys, AuditService audit)
    {
        _keys = keys;
        _audit = audit;
    }

    private Guid CurrentUserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    /// <summary>Scopes this account can usefully grant: a scope whose permissions the account lacks is not offered.</summary>
    private IReadOnlyList<(string Scope, string Label, string Meaning)> OfferedScopes()
    {
        var mine = User.FindAll("Permission").Select(c => c.Value).ToList();
        return ApiKeyFormat.AllScopes
            .Where(s => s.Scope == ApiKeyFormat.Read
                || (s.Scope == ApiKeyFormat.Subscribers ? User.IsInRole("Admin") : ApiKeyFormat.AllowedPermissions([s.Scope], mine).Count > 0))
            .ToList();
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Offered = OfferedScopes();
        return View(await _keys.ListForUserAsync(CurrentUserId));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string? name, string[]? scopes, int days = 90)
    {
        var userId = CurrentUserId;
        var cleanName = ApiKeyFormat.CleanName(name);
        if (cleanName.Length == 0)
        {
            TempData["FieldError"] = "key-name|Give the key a name that says what uses it, such as \"Claude Desktop\".";
            return RedirectToAction(nameof(Index));
        }
        if (Lifetimes.All(l => l.Days != days)) days = 90;
        if (await _keys.CountActiveForUserAsync(userId) >= MaxActiveKeys)
        {
            TempData["Error"] = $"You already have {MaxActiveKeys} usable keys. Revoke one you no longer need, then create the new one.";
            return RedirectToAction(nameof(Index));
        }

        // Only scopes this account can back are stored, so the list never promises more than the key can do.
        var offered = OfferedScopes().Select(s => s.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var granted = ApiKeyFormat.NormalizeScopes((scopes ?? []).Where(offered.Contains));

        var (key, prefix) = ApiKeyFormat.Generate();
        var record = new ApiKey
        {
            UserId = userId,
            Name = cleanName,
            Prefix = prefix,
            KeyHash = ApiKeyFormat.Hash(key),
            Scopes = ApiKeyFormat.JoinScopes(granted),
            ExpiresAt = days == 0 ? null : DateTime.UtcNow.AddDays(days),
        };
        record.Id = await _keys.CreateAsync(record);
        await _audit.LogAsync(AuditActions.ApiKeyCreated, "ApiKey", record.Id.ToString(), cleanName,
            newValues: $"Scopes: {record.Scopes}; expires: {(record.ExpiresAt is null ? "never" : record.ExpiresAt.Value.ToString("yyyy-MM-dd"))}");

        // Shown on this response only: never stored, never put in a cookie or a redirect.
        Response.Headers.CacheControl = "no-store";
        ViewBag.Key = key;
        return View("Created", record);
    }

    [HttpPost("{id:guid}/revoke")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var mine = (await _keys.ListForUserAsync(CurrentUserId)).FirstOrDefault(k => k.Id == id);
        if (mine is not null && await _keys.RevokeAsync(id, CurrentUserId))
        {
            await _audit.LogAsync(AuditActions.ApiKeyRevoked, "ApiKey", id.ToString(), mine.Name);
            TempData["Success"] = "Key revoked. Anything still using it is refused from now on.";
        }
        return RedirectToAction(nameof(Index));
    }
}
