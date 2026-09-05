using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Models;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Blog.Web.Controllers;

[Authorize(Policy = "CanManageSettings")]
[Route("admin/settings")]
public class SettingsController : Controller
{
    private readonly ISettingRepository _settings;
    private readonly ITenantContext _tenantContext;
    private readonly IUserRepository _users;
    private readonly AuditService _audit;

    public SettingsController(ISettingRepository settings, ITenantContext tenantContext, IUserRepository users, AuditService audit)
    {
        _settings = settings;
        _tenantContext = tenantContext;
        _users = users;
        _audit = audit;
    }

    private async Task<Guid> GetSettingsUserIdAsync()
    {
        if (_tenantContext.IsCloudMode)
            return Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            
        var admin = await _users.GetFirstAdminAsync();
        return admin?.Id ?? Guid.Empty;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var targetId = await GetSettingsUserIdAsync();
        var globalSettings = await _settings.GetSettingsAsync(targetId);

        var model = new SettingsViewModel
        {
            SiteName = globalSettings.SiteName,
            SiteTagline = globalSettings.SiteDescription,
            SiteLanguage = globalSettings.SiteLanguage,
            SiteLogoUrl = globalSettings.SiteLogoUrl,
            SiteFaviconUrl = globalSettings.SiteFaviconUrl,
            SiteCoverUrl = globalSettings.SiteCoverUrl,
            PostsPerPage = globalSettings.PostsPerPage,
            CommentsEnabled = globalSettings.CommentsEnabled,
            CommentsModeration = globalSettings.CommentsModeration,
            GoogleAnalyticsId = globalSettings.GoogleAnalyticsId,
            GoogleSiteVerification = globalSettings.GoogleSiteVerification,
            BingSiteVerification = globalSettings.BingSiteVerification,
            IndexNowEnabled = globalSettings.IndexNowEnabled,
            IndexNowApiKey = globalSettings.IndexNowApiKey,
            ErrorNotificationsEnabled = globalSettings.ErrorNotificationsEnabled,
            ErrorNotificationEmails = globalSettings.ErrorNotificationEmails,
            SocialTwitter = globalSettings.SocialTwitter,
            SocialFacebook = globalSettings.SocialFacebook,
            SocialInstagram = globalSettings.SocialInstagram,
            SocialYoutube = globalSettings.SocialYoutube,
            SocialLinkedin = globalSettings.SocialLinkedin,
            SocialGithub = globalSettings.SocialGithub,

            EntityType              = globalSettings.EntityType,
            EntityLegalName         = globalSettings.EntityLegalName,
            EntitySameAs            = globalSettings.EntitySameAs,
            EntityFounder           = globalSettings.EntityFounder,
            EntityFoundingDate      = globalSettings.EntityFoundingDate,
            EntityIdentityStatement = globalSettings.EntityIdentityStatement
        };

        ViewData["Title"] = "General Settings";
        return View(model);
    }

    [HttpPost("update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(SettingsViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        var targetId = await GetSettingsUserIdAsync();
        var globalSettings = await _settings.GetSettingsAsync(targetId);
        
        globalSettings.SiteName = model.SiteName;
        globalSettings.SiteDescription = model.SiteTagline ?? string.Empty;
        globalSettings.SiteLanguage = string.IsNullOrWhiteSpace(model.SiteLanguage) ? "en" : model.SiteLanguage.Trim();
        globalSettings.SiteLogoUrl = model.SiteLogoUrl ?? string.Empty;
        globalSettings.SiteFaviconUrl = model.SiteFaviconUrl ?? string.Empty;
        globalSettings.SiteCoverUrl = model.SiteCoverUrl ?? string.Empty;
        globalSettings.PostsPerPage = model.PostsPerPage;
        globalSettings.CommentsEnabled = model.CommentsEnabled;
        globalSettings.CommentsModeration = model.CommentsModeration;
        globalSettings.GoogleAnalyticsId = model.GoogleAnalyticsId ?? string.Empty;
        globalSettings.GoogleSiteVerification = (model.GoogleSiteVerification ?? string.Empty).Trim();
        globalSettings.BingSiteVerification = (model.BingSiteVerification ?? string.Empty).Trim();

        // IndexNow: keep only valid key chars (a-z A-Z 0-9 and dash, per the IndexNow spec).
        // Auto-generate a compliant 32-char key when enabling without one, so the operator never
        // has to hand-craft it. Clearing the key disables discovery even if the box stays checked.
        globalSettings.IndexNowEnabled = model.IndexNowEnabled;
        var indexNowKey = new string((model.IndexNowApiKey ?? string.Empty).Trim()
            .Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        if (globalSettings.IndexNowEnabled && string.IsNullOrEmpty(indexNowKey))
            indexNowKey = Guid.NewGuid().ToString("N"); // 32 hex chars — a valid IndexNow key
        globalSettings.IndexNowApiKey = indexNowKey;

        globalSettings.ErrorNotificationsEnabled = model.ErrorNotificationsEnabled;
        globalSettings.ErrorNotificationEmails = (model.ErrorNotificationEmails ?? string.Empty).Trim();

        globalSettings.SocialTwitter = model.SocialTwitter ?? string.Empty;
        globalSettings.SocialFacebook = model.SocialFacebook ?? string.Empty;
        globalSettings.SocialInstagram = model.SocialInstagram ?? string.Empty;
        globalSettings.SocialYoutube = model.SocialYoutube ?? string.Empty;
        globalSettings.SocialLinkedin = model.SocialLinkedin ?? string.Empty;
        globalSettings.SocialGithub = model.SocialGithub ?? string.Empty;

        // An unknown publisher type falls back rather than emitting an invalid schema.org @type.
        globalSettings.EntityType              = Blog.Core.Services.EntityGraph.ResolveType(model.EntityType);
        globalSettings.EntityLegalName         = (model.EntityLegalName ?? string.Empty).Trim();
        globalSettings.EntitySameAs            = (model.EntitySameAs ?? string.Empty).Trim();
        globalSettings.EntityFounder           = (model.EntityFounder ?? string.Empty).Trim();
        globalSettings.EntityFoundingDate      = (model.EntityFoundingDate ?? string.Empty).Trim();
        globalSettings.EntityIdentityStatement = (model.EntityIdentityStatement ?? string.Empty).Trim();

        await _settings.SaveSettingsAsync(targetId, globalSettings);
        await _audit.LogAsync(AuditActions.SettingsUpdated, "Settings", targetId.ToString(), "Site Settings");

        TempData["Success"] = "Settings updated successfully.";
        return RedirectToAction("Index");
    }
}
