using System.Text.Json.Serialization;

namespace Blog.Core.Domain;

public class UserSettings
{
    [JsonIgnore]
    public Guid UserId { get; set; }
    public string SiteName { get; set; } = "Blogs";
    public string SiteDescription { get; set; } = "A modern, self-hosted multi-tenant blogging platform built with .NET";
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

    // Social Links
    public string SocialTwitter { get; set; } = string.Empty;
    public string SocialFacebook { get; set; } = string.Empty;
    public string SocialInstagram { get; set; } = string.Empty;
    public string SocialYoutube { get; set; } = string.Empty;
    public string SocialLinkedin { get; set; } = string.Empty;
    public string SocialGithub { get; set; } = string.Empty;
}
