using Blog.Core.Interfaces;
using Blog.Web.Models;
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

    public SettingsController(ISettingRepository settings, ITenantContext tenantContext, IUserRepository users)
    {
        _settings = settings;
        _tenantContext = tenantContext;
        _users = users;
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
            SiteLogoUrl = globalSettings.SiteLogoUrl,
            SiteFaviconUrl = globalSettings.SiteFaviconUrl,
            SiteCoverUrl = globalSettings.SiteCoverUrl,
            PostsPerPage = globalSettings.PostsPerPage,
            CommentsEnabled = globalSettings.CommentsEnabled,
            CommentsModeration = globalSettings.CommentsModeration,
            GoogleAnalyticsId = globalSettings.GoogleAnalyticsId,
            SocialTwitter = globalSettings.SocialTwitter,
            SocialFacebook = globalSettings.SocialFacebook,
            SocialInstagram = globalSettings.SocialInstagram,
            SocialYoutube = globalSettings.SocialYoutube,
            SocialLinkedin = globalSettings.SocialLinkedin,
            SocialGithub = globalSettings.SocialGithub
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
        globalSettings.SiteDescription = model.SiteTagline;
        globalSettings.SiteLogoUrl = model.SiteLogoUrl ?? string.Empty;
        globalSettings.SiteFaviconUrl = model.SiteFaviconUrl ?? string.Empty;
        globalSettings.SiteCoverUrl = model.SiteCoverUrl ?? string.Empty;
        globalSettings.PostsPerPage = model.PostsPerPage;
        globalSettings.CommentsEnabled = model.CommentsEnabled;
        globalSettings.CommentsModeration = model.CommentsModeration;
        globalSettings.GoogleAnalyticsId = model.GoogleAnalyticsId ?? string.Empty;
        globalSettings.SocialTwitter = model.SocialTwitter ?? string.Empty;
        globalSettings.SocialFacebook = model.SocialFacebook ?? string.Empty;
        globalSettings.SocialInstagram = model.SocialInstagram ?? string.Empty;
        globalSettings.SocialYoutube = model.SocialYoutube ?? string.Empty;
        globalSettings.SocialLinkedin = model.SocialLinkedin ?? string.Empty;
        globalSettings.SocialGithub = model.SocialGithub ?? string.Empty;

        await _settings.SaveSettingsAsync(targetId, globalSettings);

        TempData["Success"] = "Settings updated successfully.";
        return RedirectToAction("Index");
    }
}
