using System.Net;
using System.Security.Claims;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using Blog.Web.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Security. Reviews and edits the IP firewall: blocked addresses, the live watchlist of
/// addresses building a threat score, and the auto-block thresholds. Admin-only.
/// </summary>
[Authorize(Policy = "AdminOnly")]
[Route("admin/security")]
public class SecurityController : Controller
{
    private const int PageSize = 50;

    private readonly IIpFirewallRepository _rules;
    private readonly IpFirewallService _firewall;
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenant;
    private readonly AuditService _audit;

    public SecurityController(
        IIpFirewallRepository rules, IpFirewallService firewall, ISettingRepository settings,
        IUserRepository users, ITenantContext tenant, AuditService audit)
    {
        _rules = rules;
        _firewall = firewall;
        _settings = settings;
        _users = users;
        _tenant = tenant;
        _audit = audit;
    }

    private async Task<Guid> GetSettingsUserIdAsync()
    {
        if (_tenant.IsCloudMode)
            return Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var admin = await _users.GetFirstAdminAsync();
        return admin?.Id ?? Guid.Empty;
    }

    private string CurrentUserName() =>
        User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.Email) ?? "admin";

    [HttpGet("")]
    public async Task<IActionResult> Index(string? kind, string? q, int page = 1)
    {
        if (page < 1) page = 1;

        var (items, total) = await _rules.GetPagedAsync(page, PageSize, kind, q);
        var stats = await _rules.GetStatsAsync();
        var targetId = await GetSettingsUserIdAsync();
        var settings = await _settings.GetSettingsAsync(targetId);

        ViewBag.Items       = items;
        ViewBag.Total       = total;
        ViewBag.Page        = page;
        ViewBag.TotalPages  = (int)Math.Ceiling(total / (double)PageSize);
        ViewBag.Stats       = stats;
        ViewBag.FilterKind  = kind;
        ViewBag.FilterQuery = q;
        ViewBag.Watchlist   = _firewall.GetWatchlist();
        ViewBag.Settings    = settings;
        ViewBag.InvalidAllowlistEntries = await _firewall.GetInvalidAllowlistEntriesAsync();
        ViewBag.YourIp      = _firewall.ResolveClientIp(HttpContext);

        ViewData["Title"] = "Security";
        return View();
    }

    /// <summary>Block an address by hand. <paramref name="hours"/> 0 (or less) means permanent.</summary>
    [HttpPost("block")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Block(string ip, string? reason, int hours = 24, string? returnUrl = null)
    {
        if (!TryNormalizeIp(ip, out var normalized))
        {
            TempData["Error"] = $"'{ip}' is not a valid IP address.";
            return Back(returnUrl);
        }

        // Refuse to cut off the machine the admin is sitting at.
        if (string.Equals(normalized, _firewall.ResolveClientIp(HttpContext), StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "That is your own address — blocking it would lock you out.";
            return Back(returnUrl);
        }

        var permanent = hours <= 0;
        var rule = await _firewall.BlockAsync(
            normalized,
            string.IsNullOrWhiteSpace(reason) ? "Blocked manually by an administrator" : reason.Trim(),
            permanent ? null : TimeSpan.FromHours(hours),
            CurrentUserName(),
            permanent);

        await _audit.LogAsync(AuditActions.SecurityIpBlocked, "Security", rule.IpAddress, rule.IpAddress,
            newValues: System.Text.Json.JsonSerializer.Serialize(new
            {
                rule.Kind, rule.Source, rule.Reason,
                ExpiresAt = rule.ExpiresAt?.ToString("u") ?? "permanent"
            }));

        TempData["Success"] = permanent
            ? $"{rule.IpAddress} is blocked permanently."
            : $"{rule.IpAddress} is blocked until {rule.ExpiresAt:u} UTC.";
        return Back(returnUrl);
    }

    /// <summary>Allowlist an address — it can never be auto-blocked, and any existing block is lifted.</summary>
    [HttpPost("allow")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Allow(string ip, string? reason, string? returnUrl = null)
    {
        if (!TryNormalizeIp(ip, out var normalized))
        {
            TempData["Error"] = $"'{ip}' is not a valid IP address.";
            return Back(returnUrl);
        }

        var rule = await _firewall.AllowAsync(
            normalized,
            string.IsNullOrWhiteSpace(reason) ? "Allowlisted by an administrator" : reason.Trim(),
            CurrentUserName());

        await _audit.LogAsync(AuditActions.SecurityIpAllowed, "Security", rule.IpAddress, rule.IpAddress,
            newValues: System.Text.Json.JsonSerializer.Serialize(new { rule.Kind, rule.Reason }));

        TempData["Success"] = $"{rule.IpAddress} is allowlisted and will never be auto-blocked.";
        return Back(returnUrl);
    }

    /// <summary>Delete a rule — unblocks an address or drops an allowlist entry.</summary>
    [HttpPost("remove/{id:long}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(long id, string? kind, string? q, int page = 1)
    {
        var rule = await _rules.GetByIdAsync(id);
        var removed = await _firewall.RemoveRuleAsync(id);
        if (removed)
        {
            var wasBlock = rule?.Kind != IpRuleKinds.Allow;
            await _audit.LogAsync(
                wasBlock ? AuditActions.SecurityIpUnblocked : AuditActions.SecurityRuleDeleted,
                "Security", rule?.IpAddress ?? id.ToString(), rule?.IpAddress ?? $"rule #{id}",
                oldValues: rule is null ? null : System.Text.Json.JsonSerializer.Serialize(new
                {
                    rule.Kind, rule.Source, rule.Reason, rule.HitCount,
                    ExpiresAt = rule.ExpiresAt?.ToString("u") ?? "permanent"
                }));
            TempData["Success"] = rule is null ? "Rule removed." : $"Rule for {rule.IpAddress} removed.";
        }
        else
        {
            TempData["Error"] = "That rule no longer exists.";
        }

        return RedirectToAction(nameof(Index), new { kind, q, page });
    }

    [HttpPost("settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(
        bool firewallEnabled, int thresholdScore, int windowMinutes, int blockHours,
        bool escalate, bool protectCrawlers, bool trustProxyHeaders, bool notifyOnBlock,
        string? allowlist)
    {
        var targetId = await GetSettingsUserIdAsync();
        var settings = await _settings.GetSettingsAsync(targetId);

        var before = System.Text.Json.JsonSerializer.Serialize(new
        {
            settings.FirewallEnabled, settings.FirewallThresholdScore, settings.FirewallWindowMinutes,
            settings.FirewallBlockHours, settings.FirewallEscalateRepeatOffenders,
            settings.FirewallProtectSearchCrawlers, settings.FirewallTrustProxyHeaders,
            settings.FirewallNotifyOnBlock, settings.FirewallIpAllowlist
        });

        settings.FirewallEnabled                 = firewallEnabled;
        settings.FirewallThresholdScore          = Math.Clamp(thresholdScore, 3, 1000);
        settings.FirewallWindowMinutes           = Math.Clamp(windowMinutes, 1, 1440);
        settings.FirewallBlockHours              = Math.Clamp(blockHours, 1, 8760);
        settings.FirewallEscalateRepeatOffenders = escalate;
        settings.FirewallProtectSearchCrawlers   = protectCrawlers;
        settings.FirewallTrustProxyHeaders       = trustProxyHeaders;
        settings.FirewallNotifyOnBlock           = notifyOnBlock;
        settings.FirewallIpAllowlist             = (allowlist ?? string.Empty).Trim();

        await _settings.SaveSettingsAsync(targetId, settings);
        _firewall.Invalidate();

        var after = System.Text.Json.JsonSerializer.Serialize(new
        {
            settings.FirewallEnabled, settings.FirewallThresholdScore, settings.FirewallWindowMinutes,
            settings.FirewallBlockHours, settings.FirewallEscalateRepeatOffenders,
            settings.FirewallProtectSearchCrawlers, settings.FirewallTrustProxyHeaders,
            settings.FirewallNotifyOnBlock, settings.FirewallIpAllowlist
        });

        await _audit.LogAsync(AuditActions.SecuritySettingsUpdated, "Security", targetId.ToString(),
            "Firewall settings", oldValues: before, newValues: after);

        TempData["Success"] = "Firewall settings saved.";
        return RedirectToAction(nameof(Index));
    }

    private IActionResult Back(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction(nameof(Index));

    private static bool TryNormalizeIp(string? raw, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!IPAddress.TryParse(raw.Trim(), out var addr)) return false;
        normalized = (addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4() : addr).ToString();
        return true;
    }
}
