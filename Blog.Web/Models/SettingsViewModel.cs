using System.ComponentModel.DataAnnotations;

namespace Blog.Web.Models;

public class SettingsViewModel
{
    [Required]
    [Display(Name = "Blog Name")]
    public string SiteName { get; set; } = string.Empty;

    [Display(Name = "Tagline")]
    public string? SiteTagline { get; set; }

    [Display(Name = "Content Language (e.g. en, es, hi)")]
    public string? SiteLanguage { get; set; }

    [Display(Name = "Site Logo URL")]
    public string? SiteLogoUrl { get; set; }

    [Display(Name = "Favicon URL")]
    public string? SiteFaviconUrl { get; set; }

    [Display(Name = "Cover Image URL")]
    public string? SiteCoverUrl { get; set; }

    [Required]
    [Range(1, 100)]
    [Display(Name = "Posts Per Page")]
    public int PostsPerPage { get; set; } = 10;

    [Display(Name = "Enable Comments")]
    public bool CommentsEnabled { get; set; }

    [Display(Name = "Comment Moderation")]
    public bool CommentsModeration { get; set; }

    [Required]
    [Range(7, 3650)]
    [Display(Name = "Keep AI-crawler visits for (days)")]
    public int CrawlerVisitRetentionDays { get; set; } = 90;

    [Required]
    [Range(0, 3650, ErrorMessage = "Enter 0 to keep every page view, or a number of days up to 3650.")]
    [Display(Name = "Keep page-view details for (days)")]
    public int PageViewRetentionDays { get; set; } = 0;

    [Display(Name = "Display time zone")]
    public string? DisplayTimeZoneId { get; set; } = "UTC";

    /// <summary>ISO 3166-1 alpha-2; narrows the zone and date-format lists (ADR-0027).</summary>
    [Display(Name = "Country")]
    public string? Country { get; set; }

    /// <summary>The locale whose date conventions every date follows, e.g. "en-IN".</summary>
    [Display(Name = "Date format")]
    public string? DateLocale { get; set; } = "en-US";

    /// <summary>"locale" or "iso8601".</summary>
    public string? DateFormat { get; set; } = "locale";

    /// <summary>"locale", "24h" or "12h".</summary>
    [Display(Name = "Clock")]
    public string? ClockFormat { get; set; } = "locale";

    /// <summary>system (follow the reader's OS) · light · dark. Applies to the public site and the Desk.</summary>
    [Display(Name = "Colour scheme")]
    public string? ColorScheme { get; set; } = "system";

    [Required]
    [Range(1, 200)]
    [Display(Name = "Revisions kept per post or page")]
    public int RevisionsPerItem { get; set; } = 25;

    // ── Search Console ───────────────────────────────────────────────────────
    [Display(Name = "Search Console property")]
    public string? SearchConsoleProperty { get; set; }

    /// <summary>Write-only: pasted to connect or replace; never populated from stored settings.</summary>
    [Display(Name = "Service-account key file (JSON)")]
    public string? SearchConsoleServiceAccountJson { get; set; }

    [Display(Name = "Disconnect Search Console")]
    public bool SearchConsoleDisconnect { get; set; }

    [Range(1, 16)]
    [Display(Name = "Keep search data for (months)")]
    public int SearchPerformanceRetentionMonths { get; set; } = 16;

    /// <summary>Display only: the connected service account's email, or null when not connected.</summary>
    public string? SearchConsoleConnectedAs { get; set; }
    /// <summary>Display only: a credential is stored (even if the property is missing).</summary>
    public bool SearchConsoleCredentialStored { get; set; }
    /// <summary>Display only: newest date with pulled data.</summary>
    public DateTime? SearchConsoleLatestDate { get; set; }

    [Display(Name = "Google Analytics Measurement ID")]
    [RegularExpression(@"^\s*((G|UA|GT|AW)-[A-Z0-9-]{4,24})?\s*$",
        ErrorMessage = "Enter the Measurement ID exactly as Google shows it, for example G-ABC123XYZ9, or leave it blank.")]
    public string? GoogleAnalyticsId { get; set; }

    [Display(Name = "Ask readers for cookie consent")]
    public bool CookieConsentEnabled { get; set; } = true;

    /// <summary>Roles that must use two-factor sign-in (Admin-only field).</summary>
    public List<string> TwoFactorRequiredRoles { get; set; } = new();

    /// <summary>The custom field definitions as JSON, edited by the Custom fields tab (ADR-0025).</summary>
    public string? CustomFieldsJson { get; set; }

    /// <summary>True when the form carried the Custom fields tab, so a save without it never clears the definitions.</summary>
    public bool CustomFieldsPosted { get; set; }
    /// <summary>True when the form carried the role boxes (only Admins see them), so an empty list means "none ticked".</summary>
    public bool TwoFactorRolesPosted { get; set; }

    [Display(Name = "Google Search Console verification code")]
    public string? GoogleSiteVerification { get; set; }

    [Display(Name = "Bing Webmaster verification code")]
    public string? BingSiteVerification { get; set; }

    [Display(Name = "Enable IndexNow (instant indexing)")]
    public bool IndexNowEnabled { get; set; }

    [Display(Name = "IndexNow API Key")]
    public string? IndexNowApiKey { get; set; }

    [Display(Name = "Email me when a server error occurs")]
    public bool ErrorNotificationsEnabled { get; set; }

    [Display(Name = "Error notification recipients")]
    public string? ErrorNotificationEmails { get; set; }

    [Display(Name = "X (Twitter) URL")]
    public string? SocialTwitter { get; set; }

    [Display(Name = "Facebook URL")]
    public string? SocialFacebook { get; set; }

    [Display(Name = "Instagram URL")]
    public string? SocialInstagram { get; set; }

    [Display(Name = "YouTube URL")]
    public string? SocialYoutube { get; set; }

    [Display(Name = "LinkedIn URL")]
    public string? SocialLinkedin { get; set; }

    [Display(Name = "GitHub URL")]
    public string? SocialGithub { get; set; }

    // ── Publisher identity (knowledge graph) ─────────────────────────────────

    [Display(Name = "Publisher type")]
    public string? EntityType { get; set; }

    [Display(Name = "Legal name")]
    public string? EntityLegalName { get; set; }

    [Display(Name = "Authority profiles")]
    public string? EntitySameAs { get; set; }

    [Display(Name = "Founder")]
    public string? EntityFounder { get; set; }

    [Display(Name = "Founded")]
    public string? EntityFoundingDate { get; set; }

    [Display(Name = "Identity statement")]
    public string? EntityIdentityStatement { get; set; }

    [Display(Name = "Contact email")]
    [EmailAddress(ErrorMessage = "Enter an email address such as hello@example.com.")]
    [StringLength(254)]
    public string? EntityContactEmail { get; set; }

    [Display(Name = "Contact page")]
    [RegularExpression(@"^(https?://\S+|/\S*)$", ErrorMessage = "Enter a full web address starting with https://, or a path on this site such as /contact.")]
    [StringLength(500)]
    public string? EntityContactUrl { get; set; }

    [Display(Name = "Contact type")]
    [StringLength(60)]
    public string? EntityContactType { get; set; }
}
