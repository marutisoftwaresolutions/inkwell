using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;

namespace Blog.Web.Services.Newsletter;

/// <summary>
/// Builds a newsletter issue into email (ADR-0028): the featured posts as cards (title, image, summary, date in
/// the site's format), the intro, and the site name. Used for the Desk preview, the test send and the frozen
/// copy queued for sending. Only published posts are included; a post unpublished meanwhile drops out.
/// </summary>
public sealed class NewsletterService
{
    public const int MaxPosts = 6;

    private readonly IPostRepository _posts;
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly IEmailService _email;
    private readonly SiteDates _dates;
    private readonly IConfiguration _config;

    public NewsletterService(IPostRepository posts, ISettingRepository settings, IUserRepository users, IEmailService email,
        SiteDates dates, IConfiguration config)
    {
        _posts = posts;
        _settings = settings;
        _users = users;
        _email = email;
        _dates = dates;
        _config = config;
    }

    /// <summary>The site's public address: the configured canonical host, else the address this request came in on.</summary>
    public string SiteUrl(HttpContext context)
    {
        var canonical = _config["CanonicalHost"];
        return !string.IsNullOrWhiteSpace(canonical)
            ? "https://" + canonical.Trim().TrimEnd('/')
            : $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}";
    }

    public async Task<IReadOnlyList<Post>> FeaturedPostsAsync(NewsletterIssue issue)
    {
        var list = new List<Post>();
        foreach (var id in issue.PostIdList.Take(MaxPosts))
        {
            var p = await _posts.GetByIdAsync(id, null);
            if (p is not null && IsPublic(p)) list.Add(p);
        }
        return list;
    }

    public static bool IsPublic(Post p) =>
        p.Status == PostStatus.Published || (p.Status == PostStatus.Scheduled && p.ScheduledAt <= DateTime.UtcNow);

    public async Task<(string Html, string Text)> RenderAsync(NewsletterIssue issue, string siteUrl)
    {
        var owner = (await _users.GetFirstAdminAsync())?.Id ?? Guid.Empty;
        var settings = await _settings.GetSettingsAsync(owner);
        var siteName = settings.SiteName;
        var posts = (await FeaturedPostsAsync(issue)).Select(p => new NewsletterPost(
            p.Title,
            "/" + p.Slug,
            !string.IsNullOrWhiteSpace(p.MetaDescription) ? p.MetaDescription : TextHelper.SmartTruncate(p.Plaintext),
            string.IsNullOrWhiteSpace(p.OgImage) ? p.FeatureImage : p.OgImage,
            _dates.Long(p.PublishedAt))).ToList();
        return NewsletterEmail.Render(new NewsletterContent(
            string.IsNullOrWhiteSpace(siteName) ? "Newsletter" : siteName, siteUrl, issue.Subject, issue.Preheader, issue.IntroMarkdown, posts,
            string.IsNullOrWhiteSpace(settings.SiteLanguage) ? "en" : settings.SiteLanguage));
    }

    /// <summary>A copy to one address, marked as a test. Its unsubscribe link points at the site, not at anyone's subscription.</summary>
    public async Task<EmailSendResult> SendTestAsync(NewsletterIssue issue, string siteUrl, string toEmail, string? toName)
    {
        var (html, text) = await RenderAsync(issue, siteUrl);
        return await _email.SendMessageAsync(new EmailMessage(toEmail, toName, "[Test] " + issue.Subject,
            NewsletterEmail.Personalize(html, siteUrl, html: true), NewsletterEmail.Personalize(text, siteUrl, html: false)));
    }
}
