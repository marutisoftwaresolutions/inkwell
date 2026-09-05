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
