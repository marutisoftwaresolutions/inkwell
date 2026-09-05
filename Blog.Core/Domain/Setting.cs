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

    // IP Firewall (Admin → Security) — automatic blocking of hostile addresses. Stored in the
    // Settings JSON blob (no schema change). Ships enabled with conservative defaults so a tenant
    // that never opens the Security screen is still protected; every field falls back to the
    // default below when absent from an older JSON payload.
    public bool   FirewallEnabled                 { get; set; } = true;
    /// <summary>Threat score within the window that triggers an automatic block.</summary>
    public int    FirewallThresholdScore          { get; set; } = 10;
    /// <summary>Sliding window, in minutes, over which the score accumulates.</summary>
    public int    FirewallWindowMinutes           { get; set; } = 10;
    /// <summary>Duration of a first-offense block, in hours.</summary>
    public int    FirewallBlockHours              { get; set; } = 24;
    /// <summary>Second offense blocks for a week; a third makes the block permanent.</summary>
    public bool   FirewallEscalateRepeatOffenders { get; set; } = true;
    /// <summary>Never auto-block verified-looking search/AI crawlers on 404 noise alone.</summary>
    public bool   FirewallProtectSearchCrawlers   { get; set; } = true;
    /// <summary>Read the client IP from CF-Connecting-IP / X-Forwarded-For. Enable only behind a trusted proxy.</summary>
    public bool   FirewallTrustProxyHeaders       { get; set; } = false;
    /// <summary>Never-block addresses / CIDR ranges, comma- or newline-separated.</summary>
    public string FirewallIpAllowlist             { get; set; } = string.Empty;
    /// <summary>Email the error-notification recipients whenever an address is auto-blocked.</summary>
    public bool   FirewallNotifyOnBlock           { get; set; } = false;

    // ── Entity / knowledge graph ──────────────────────────────────────────────
    // Who this publication *is*, as an entity rather than a website. Search engines and answer
    // engines resolve a brand by cross-referencing authoritative profiles; without them a domain
    // that once published something else keeps answering to its former identity.

    /// <summary>Whether the publisher is an organisation or an individual. "Organization" or "Person".</summary>
    public string EntityType { get; set; } = "Organization";

    /// <summary>Registered or formal name, when it differs from the site name.</summary>
    public string EntityLegalName { get; set; } = string.Empty;

    /// <summary>
    /// Authoritative profile URLs beyond the social links — Wikipedia, Wikidata, Crunchbase, a
    /// company register, an ORCID. One per line. These are what an engine cross-references to decide
    /// two mentions are the same entity.
    /// </summary>
    public string EntitySameAs { get; set; } = string.Empty;

    /// <summary>Founder or the person behind the publication.</summary>
    public string EntityFounder { get; set; } = string.Empty;

    /// <summary>ISO-8601 date (YYYY-MM-DD or YYYY) the publication began.</summary>
    public string EntityFoundingDate { get; set; } = string.Empty;

    /// <summary>
    /// A plain-language statement of what this publication is and is not, written for answer
    /// engines. Emitted verbatim in llms.txt's Identity section. Left empty, the generated default
    /// stands.
    /// </summary>
    public string EntityIdentityStatement { get; set; } = string.Empty;

    // Social Links
    public string SocialTwitter { get; set; } = string.Empty;
    public string SocialFacebook { get; set; } = string.Empty;
    public string SocialInstagram { get; set; } = string.Empty;
    public string SocialYoutube { get; set; } = string.Empty;
    public string SocialLinkedin { get; set; } = string.Empty;
    public string SocialGithub { get; set; } = string.Empty;
}
