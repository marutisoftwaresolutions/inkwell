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
    private readonly Blog.Web.Services.SearchConsole.SearchConsoleCredentialStore _searchCredentials;
    private readonly ISearchPerformanceRepository _searchRows;

    public SettingsController(ISettingRepository settings, ITenantContext tenantContext, IUserRepository users, AuditService audit,
        Blog.Web.Services.SearchConsole.SearchConsoleCredentialStore searchCredentials, ISearchPerformanceRepository searchRows)
    {
        _settings = settings;
        _tenantContext = tenantContext;
        _users = users;
        _audit = audit;
        _searchCredentials = searchCredentials;
        _searchRows = searchRows;
    }

    /// <summary>
    /// The time zones and date formats for a country (all of them when empty), with the zone and format to
    /// suggest when the country has just been picked (ADR-0027). Read-only; used by the Display tab.
    /// </summary>
    [HttpGet("date-options")]
    public async Task<IActionResult> DateOptions(string? country, string? zone, string? locale)
    {
        var catalog = HttpContext.RequestServices.GetRequiredService<Blog.Web.Services.DateSettingsCatalog>();
        var cc = (country ?? "").Trim().ToUpperInvariant();
        var settings = await _settings.GetSettingsAsync(await GetSettingsUserIdAsync());
        var (suggestZone, suggestLocale) = cc.Length == 2 ? await catalog.SuggestAsync(cc, settings.SiteLanguage) : (null, null);
        return Json(new
        {
            zones = (await catalog.ZoneOptionsAsync(cc, zone)).Select(o => new { value = o.Value, label = o.Label }),
            locales = (await catalog.LocaleOptionsAsync(cc, locale)).Select(o => new { value = o.Value, label = o.Label }),
            suggestZone,
            suggestLocale,
        });
    }

    /// <summary>Today's date and time as the given settings would show it, for the live preview.</summary>
    [HttpGet("date-preview")]
    public IActionResult DatePreview(string? zone, string? locale, string? format, string? clock)
    {
        var o = Blog.Web.Services.SiteDates.From(new Blog.Core.Domain.UserSettings
        {
            DisplayTimeZoneId = zone ?? "UTC", DateLocale = locale ?? "en-US", DateFormat = format ?? "locale", ClockFormat = clock ?? "locale",
        });
        var now = DateTime.UtcNow;
        return Json(new
        {
            known = Blog.Core.Services.TimeZoneHelper.IsKnown(o.TimeZoneId),
            @long = Blog.Core.Services.DateDisplay.Format(now, o, Blog.Core.Services.DateStyle.Long),
            medium = Blog.Core.Services.DateDisplay.Format(now, o, Blog.Core.Services.DateStyle.Medium),
            @short = Blog.Core.Services.DateDisplay.Format(now, o, Blog.Core.Services.DateStyle.Short),
            dateTime = Blog.Core.Services.DateDisplay.Format(now, o, Blog.Core.Services.DateStyle.DateTime),
        });
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
            CrawlerVisitRetentionDays = Blog.Web.Services.Jobs.CrawlerVisitRetentionJob.ClampDays(globalSettings.CrawlerVisitRetentionDays),
            DisplayTimeZoneId = Blog.Core.Services.TimeZoneHelper.ToIana(globalSettings.DisplayTimeZoneId),
            Country = globalSettings.Country,
            DateLocale = globalSettings.DateLocale,
            DateFormat = globalSettings.DateFormat,
            ClockFormat = globalSettings.ClockFormat,
            ColorScheme = globalSettings.Theme is "light" or "dark" ? globalSettings.Theme : "system",
            RevisionsPerItem = RevisionService.ClampKeep(globalSettings.RevisionsPerItem),
            GoogleAnalyticsId = globalSettings.GoogleAnalyticsId,
            CookieConsentEnabled = globalSettings.CookieConsentEnabled,
            TwoFactorRequiredRoles = globalSettings.TwoFactorRequiredRoles?.ToList() ?? new(),
            CustomFieldsJson = System.Text.Json.JsonSerializer.Serialize(globalSettings.CustomFields ?? new()),
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
            EntityIdentityStatement = globalSettings.EntityIdentityStatement,

            SearchConsoleProperty          = globalSettings.SearchConsoleProperty,
            SearchConsoleConnectedAs       = _searchCredentials.IsConfigured(globalSettings) ? globalSettings.SearchConsoleClientEmail : null,
            SearchConsoleCredentialStored  = !string.IsNullOrEmpty(globalSettings.SearchConsoleCredentialProtected),
            SearchPerformanceRetentionMonths = Math.Clamp(globalSettings.SearchPerformanceRetentionMonths <= 0 ? 16 : globalSettings.SearchPerformanceRetentionMonths, 1, 16)
        };
        if (model.SearchConsoleConnectedAs is not null)
        {
            try { model.SearchConsoleLatestDate = await _searchRows.GetLatestDateAsync(targetId); } catch { }
        }

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
        globalSettings.CrawlerVisitRetentionDays = Blog.Web.Services.Jobs.CrawlerVisitRetentionJob.ClampDays(model.CrawlerVisitRetentionDays);

        // Date and time (ADR-0027). The zone must be one this host knows (a typo would silently show UTC) and is
        // stored as its IANA id, so a Windows id saved before keeps working and moves to the standard name.
        var tz = Blog.Core.Services.TimeZoneHelper.ToIana(model.DisplayTimeZoneId);
        if (!Blog.Core.Services.TimeZoneHelper.IsKnown(tz))
            ModelState.AddModelError(nameof(model.DisplayTimeZoneId), "This server does not know that time zone. Pick another from the list.");
        var dateLocale = (model.DateLocale ?? "").Trim();
        if (!Blog.Core.Services.DateDisplay.IsKnownLocale(dateLocale))
            ModelState.AddModelError(nameof(model.DateLocale), "Pick a date format from the list.");
        var country = (model.Country ?? "").Trim().ToUpperInvariant();
        if (!await HttpContext.RequestServices.GetRequiredService<Blog.Web.Services.DateSettingsCatalog>().IsKnownCountryAsync(country))
            ModelState.AddModelError(nameof(model.Country), "Pick a country from the list, or leave it unset.");
        if (!ModelState.IsValid) return View("Index", model);
        globalSettings.DisplayTimeZoneId = tz;
        globalSettings.Country = country;
        globalSettings.DateLocale = dateLocale;
        globalSettings.DateFormat = Blog.Core.Services.DateDisplay.IsKnownFormat(model.DateFormat) ? model.DateFormat! : Blog.Core.Services.DateDisplay.FormatLocale;
        globalSettings.ClockFormat = Blog.Core.Services.DateDisplay.IsKnownClock(model.ClockFormat) ? model.ClockFormat! : Blog.Core.Services.DateDisplay.ClockLocale;
        globalSettings.Theme = model.ColorScheme is "light" or "dark" ? model.ColorScheme : "system";
        globalSettings.RevisionsPerItem = RevisionService.ClampKeep(model.RevisionsPerItem);
        globalSettings.GoogleAnalyticsId = (model.GoogleAnalyticsId ?? string.Empty).Trim();
        globalSettings.CookieConsentEnabled = model.CookieConsentEnabled;

        // Custom fields: existing fields keep their keys (and so their values); errors go back to the row.
        if (model.CustomFieldsPosted)
        {
            List<Blog.Core.Domain.CustomFieldDefinition>? postedFields;
            try
            {
                postedFields = System.Text.Json.JsonSerializer.Deserialize<List<Blog.Core.Domain.CustomFieldDefinition>>(
                    string.IsNullOrWhiteSpace(model.CustomFieldsJson) ? "[]" : model.CustomFieldsJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (System.Text.Json.JsonException) { postedFields = null; }
            var (fields, fieldErrors) = Blog.Core.Services.CustomFields.NormalizeDefinitions(postedFields, globalSettings.CustomFields);
            if (postedFields is null || fieldErrors.Count > 0)
            {
                ViewData["CustomFieldErrors"] = fieldErrors;
                ModelState.AddModelError(nameof(model.CustomFieldsJson), postedFields is null
                    ? "The custom fields could not be read. Reload the page and try again."
                    : "Some custom fields need attention.");
                return View("Index", model);
            }
            globalSettings.CustomFields = fields;
        }
        // Only an Admin decides who must use two-factor; anyone else's save leaves it as it was.
        if (model.TwoFactorRolesPosted && User.IsInRole("Admin"))
        {
            globalSettings.TwoFactorRequiredRoles = (model.TwoFactorRequiredRoles ?? new())
                .Where(r => Blog.Core.Services.TwoFactor.Roles.Contains(r)).Distinct().ToList();
            HttpContext.RequestServices.GetRequiredService<Blog.Web.Services.TwoFactorService>().Forget(Guid.Empty);
        }
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

        // ── Search Console ──────────────────────────────────────────────────
        // The key file is write-only: it is protected on the way in, never echoed back, and only
        // replaced when a new one is pasted. "Disconnect" clears everything. The audit payload
        // names the property and the service account, never the key.
        var wasConfigured = _searchCredentials.IsConfigured(globalSettings);
        if (model.SearchConsoleDisconnect)
        {
            globalSettings.SearchConsoleProperty = string.Empty;
            globalSettings.SearchConsoleCredentialProtected = string.Empty;
            globalSettings.SearchConsoleClientEmail = string.Empty;
        }
        else
        {
            var property = Blog.Core.Services.SearchConsoleJwt.NormaliseProperty(model.SearchConsoleProperty);
            if (!string.IsNullOrWhiteSpace(model.SearchConsoleProperty) && property is null)
            {
                ModelState.AddModelError(nameof(model.SearchConsoleProperty),
                    "Enter the property as Search Console shows it: a domain (example.com) or a URL prefix (https://www.example.com/).");
                return View("Index", model);
            }
            globalSettings.SearchConsoleProperty = property ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(model.SearchConsoleServiceAccountJson))
            {
                var credential = Blog.Core.Services.ServiceAccountCredential.TryParse(model.SearchConsoleServiceAccountJson, out var credentialError);
                if (credential is null)
                {
                    ModelState.AddModelError(nameof(model.SearchConsoleServiceAccountJson), credentialError ?? "Invalid credential.");
                    return View("Index", model);
                }
                globalSettings.SearchConsoleCredentialProtected = _searchCredentials.Protect(model.SearchConsoleServiceAccountJson.Trim());
                globalSettings.SearchConsoleClientEmail = credential.ClientEmail;
            }
        }
        globalSettings.SearchPerformanceRetentionMonths = Math.Clamp(model.SearchPerformanceRetentionMonths <= 0 ? 16 : model.SearchPerformanceRetentionMonths, 1, 16);
        var nowConfigured = _searchCredentials.IsConfigured(globalSettings);

        await _settings.SaveSettingsAsync(targetId, globalSettings);
        await _audit.LogAsync(AuditActions.SettingsUpdated, "Settings", targetId.ToString(), "Site Settings");
        if (nowConfigured && (!wasConfigured || !string.IsNullOrWhiteSpace(model.SearchConsoleServiceAccountJson)))
            await _audit.LogAsync(AuditActions.SettingsSearchConsoleConnected, "Settings", targetId.ToString(),
                $"{globalSettings.SearchConsoleProperty} as {globalSettings.SearchConsoleClientEmail}");
        else if (wasConfigured && !nowConfigured)
        {
            // Disconnecting also drops the stored performance rows: they were pulled under the credential
            // the operator just removed, and with no connection nothing would refresh or prune them.
            // The disconnect itself is already saved, so a failure here must not turn into a 500.
            var removed = 0;
            try { removed = await _searchRows.DeleteForOwnerAsync(targetId); }
            catch { /* rows linger until the next disconnect; the settings change stands */ }
            await _audit.LogAsync(AuditActions.SettingsSearchConsoleDisconnected, "Settings", targetId.ToString(),
                removed > 0 ? $"Search Console ({removed:N0} stored performance rows removed)" : "Search Console");
        }

        TempData["Success"] = "Settings updated successfully.";
        return RedirectToAction("Index");
    }
}
