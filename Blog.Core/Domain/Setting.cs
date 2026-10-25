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

    // Cookie-consent banner on the public site (v1.0.8). On by default, including for existing sites
    // whose stored settings predate the field: Google Analytics then loads only after a reader accepts.
    // Off restores the earlier behaviour (Analytics on every page, no banner).
    public bool CookieConsentEnabled { get; set; } = true;

    // Roles that must use two-factor sign-in (v1.0.8, ADR-0021). Empty by default: two-factor is
    // optional for everyone until an Admin requires it for a role in Settings → Notifications & housekeeping.
    public List<string> TwoFactorRequiredRoles { get; set; } = new();

    /// <summary>The site's custom fields (ADR-0025). Empty by default: no field appears anywhere until one is defined.</summary>
    public List<CustomFieldDefinition> CustomFields { get; set; } = new();

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

    /// <summary>
    /// Days of AI/search crawler visits kept in <c>CrawlerVisits</c> before the nightly retention
    /// job prunes them (Admin → AI Crawlers). Stored in the Settings JSON blob; older payloads fall
    /// back to the default. 7–3650.
    /// </summary>
    public int CrawlerVisitRetentionDays { get; set; } = 90;

    /// <summary>
    /// How many days of raw page views the page-view-retention job keeps (Admin → Analytics). 0, the default,
    /// keeps every view, as before this setting existed; otherwise 31–3650. Only days already summarised into
    /// PageViewDaily are removed, so the Analytics report keeps its full history (ADR-0037). Stored in the
    /// Settings JSON blob; older payloads fall back to the default.
    /// </summary>
    public int PageViewRetentionDays { get; set; } = 0;

    // ── Search Console (Admin → Settings → Search Console) ──────────────────
    // Per-tenant service-account access to the Search Console API. The operator pastes the key
    // file Google issues; it is stored Data-Protection-encrypted and never returned to the UI,
    // logged, audited or exported. Empty = not connected; every consumer degrades to "no data".
    /// <summary>The property as Search Console names it: "sc-domain:example.com" or "https://www.example.com/".</summary>
    public string SearchConsoleProperty { get; set; } = string.Empty;
    /// <summary>The service-account JSON, protected with IDataProtector. Never plain text.</summary>
    public string SearchConsoleCredentialProtected { get; set; } = string.Empty;
    /// <summary>The service account's email, kept in clear so the Settings screen can say who is connected.</summary>
    public string SearchConsoleClientEmail { get; set; } = string.Empty;
    /// <summary>Months of daily rows kept in SearchPerformance. Search Console itself keeps 16. 1–16.</summary>
    public int SearchPerformanceRetentionMonths { get; set; } = 16;

    /// <summary>
    /// Time zone used to display dates and to interpret a typed schedule time. Storage is always
    /// UTC. A Windows or IANA id the host recognises; empty or unknown means UTC.
    /// </summary>
    public string DisplayTimeZoneId { get; set; } = "UTC";

    /// <summary>
    /// The site's country (ISO 3166-1 alpha-2, from the Countries table), which narrows the time zone and date
    /// format choices in Settings (ADR-0027). Empty until chosen; nothing depends on it at render time.
    /// </summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>
    /// The locale whose date conventions every human-readable date follows (BCP-47, from the DateLocales table,
    /// e.g. "en-IN"). Default "en-US": the month-first long dates most layouts showed before (ADR-0027).
    /// </summary>
    public string DateLocale { get; set; } = "en-US";

    /// <summary>"locale" (the locale's own patterns) or "iso8601" (2026-10-05 everywhere).</summary>
    public string DateFormat { get; set; } = "locale";

    /// <summary>"locale", "24h" or "12h": the clock used wherever a time of day is shown.</summary>
    public string ClockFormat { get; set; } = "locale";

    /// <summary>Revisions kept per post or page; the oldest are pruned as new ones are written. 1–200.</summary>
    public int RevisionsPerItem { get; set; } = 25;

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

    /// <summary>
    /// How readers and customers reach the publisher, emitted as the Organization's <c>contactPoint</c> (and listed in
    /// llms.txt) only when an email or a contact page is set. Empty, the structured data is exactly as before.
    /// </summary>
    public string EntityContactEmail { get; set; } = string.Empty;

    /// <summary>A contact page (absolute http(s) URL, or a path on this site such as /contact).</summary>
    public string EntityContactUrl { get; set; } = string.Empty;

    /// <summary>schema.org <c>contactType</c>, for example "customer support" or "editorial". Empty means "customer support".</summary>
    public string EntityContactType { get; set; } = string.Empty;

    // Social Links
    public string SocialTwitter { get; set; } = string.Empty;
    public string SocialFacebook { get; set; } = string.Empty;
    public string SocialInstagram { get; set; } = string.Empty;
    public string SocialYoutube { get; set; } = string.Empty;
    public string SocialLinkedin { get; set; } = string.Empty;
    public string SocialGithub { get; set; } = string.Empty;
}
