using System.Text.Json.Serialization;

namespace Blog.Core.Domain;

public class UserSettings
{
    [JsonIgnore]
    public Guid UserId { get; set; }
    public string SiteName { get; set; } = "Blogs";
    public string SiteDescription { get; set; } = "A modern, self-hosted multi-tenant blogging platform built with .NET";
    // BCP-47 language code for the site's content (e.g. "en", "es", "hi"). Drives <html lang>,
    // hreflang, og:locale, and schema inLanguage so non-English tenants declare the right language.
    public string SiteLanguage { get; set; } = "en";
    public bool CommentsEnabled { get; set; } = true;
    public bool CommentsModeration { get; set; } = false;
    public int PostsPerPage { get; set; } = 10;
    public string Theme { get; set; } = "system"; // light, dark, system
    public string SiteLogoUrl { get; set; } = string.Empty;
    public string SiteFaviconUrl { get; set; } = string.Empty;
    public string SiteCoverUrl { get; set; } = string.Empty;

    // Onboarding checklist state
    public bool OnboardingDismissed { get; set; } = false;
    public List<string> OnboardingCompletedTasks { get; set; } = new();

    // Analytics
    public string GoogleAnalyticsId { get; set; } = string.Empty;

    // Search-engine ownership verification — rendered as <meta> tags in the public <head>.
    // Store just the token/content value (not the full meta tag).
    public string GoogleSiteVerification { get; set; } = string.Empty;
    public string BingSiteVerification { get; set; } = string.Empty;

    // IndexNow — instant search-engine indexing (Bing, Yandex, Seznam, Naver via api.indexnow.org).
    // Stored in the Settings JSON blob (no schema change). When enabled with a key, published/updated
    // posts are pinged to IndexNow so search engines re-crawl within minutes instead of days. The key
    // is served at /indexnow/{key}.txt for ownership verification. Empty key = feature inert.
    public bool IndexNowEnabled { get; set; } = false;
    public string IndexNowApiKey { get; set; } = string.Empty;

    // Error notifications — email an alert when a NEW server-error (5xx) signature is detected.
    // Recipients is a comma/semicolon/newline-separated list; degrades to no-op when disabled/empty.
    public bool ErrorNotificationsEnabled { get; set; } = false;
    public string ErrorNotificationEmails { get; set; } = string.Empty;

    // Social Links
    public string SocialTwitter { get; set; } = string.Empty;
    public string SocialFacebook { get; set; } = string.Empty;
    public string SocialInstagram { get; set; } = string.Empty;
    public string SocialYoutube { get; set; } = string.Empty;
    public string SocialLinkedin { get; set; } = string.Empty;
    public string SocialGithub { get; set; } = string.Empty;
}
