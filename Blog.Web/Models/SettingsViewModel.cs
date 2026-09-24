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

    [Display(Name = "Display time zone")]
    public string? DisplayTimeZoneId { get; set; } = "UTC";

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
    public string? GoogleAnalyticsId { get; set; }

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
}
