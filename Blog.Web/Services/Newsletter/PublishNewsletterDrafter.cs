using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;

namespace Blog.Web.Services.Newsletter;

/// <summary>
/// When a post goes live and the site has asked for it (Settings, General, "Prepare a newsletter when a
/// post is published"), a newsletter draft featuring that post is created, once per post. It is a draft
/// on purpose: nothing is emailed until an Admin opens it under Newsletter and sends it, the same
/// decision every other newsletter needs, so a mistaken publish, an import or a bulk action can never
/// mail the list by itself. Unlisted posts are left out (they are not announced anywhere). Not used in
/// cloud mode, where there is no newsletter. Never fails a publish: any error is logged and dropped.
/// </summary>
public class PublishNewsletterDrafter
{
    /// <summary>The subject line is the post's title, cut to what the newsletter editor accepts.</summary>
    public const int MaxSubjectLength = 150;   // under the editor's own limit of 200

    private readonly INewsletterRepository _issues;
    private readonly IPostRepository _posts;
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly AuditService _audit;
    private readonly IConfiguration _config;
    private readonly ILogger<PublishNewsletterDrafter> _log;

    public PublishNewsletterDrafter(INewsletterRepository issues, IPostRepository posts, ISettingRepository settings, IUserRepository users,
        AuditService audit, IConfiguration config, ILogger<PublishNewsletterDrafter> log)
    {
        _issues = issues;
        _posts = posts;
        _settings = settings;
        _users = users;
        _audit = audit;
        _config = config;
        _log = log;
    }

    /// <summary>The subject for a post's newsletter: its title on one line, capped.</summary>
    public static string SubjectFor(string? title)
    {
        var clean = string.Join(' ', (title ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) return "New post";
        return clean.Length <= MaxSubjectLength ? clean : clean[..(MaxSubjectLength - 1)].TrimEnd() + "…";
    }

    /// <summary>Called when a post has just gone live. Returns true when a draft was created.</summary>
    public async Task<bool> PostWentLiveAsync(Post post)
    {
        try
        {
            if (string.Equals(_config["DeploymentMode"], "Cloud", StringComparison.OrdinalIgnoreCase)) return false;
            if (PostVisibility.IsUnlisted(post.Visibility)) return false;

            var admin = await _users.GetFirstAdminAsync();
            if (admin is null) return false;
            var site = await _settings.GetSettingsAsync(admin.Id);
            if (!site.NewsletterDraftOnPublish) return false;

            // A post an Admin already put in a newsletter needs no second one.
            // The newsletter belongs to the site (owner Guid.Empty outside cloud mode), as in the Desk.
            if (await _issues.HasIssueForPostAsync(Guid.Empty, post.Id)) return false;

            // Once per post, for good: the claim is a column on the post, so a draft the Admin deleted or
            // repurposed is not prepared again when the post is saved again, republished, or revisited by
            // the scheduled-post job (which looks back a day, every minute).
            if (!await _posts.TryMarkNewsletterDraftedAsync(post.Id)) return false;

            var issue = new NewsletterIssue
            {
                OwnerId = Guid.Empty,
                Subject = SubjectFor(post.Title),
                PostIds = post.Id.ToString(),
                CreatedBy = post.AuthorId,
            };
            try { await _issues.CreateAsync(issue); }
            catch { await _posts.ClearNewsletterDraftedAsync(post.Id); throw; }   // nothing was prepared: let a later publish try again

            // AuditService swallows its own failures and names "system" when a job, not a person, published the post.
            await _audit.LogAsync(AuditActions.NewsletterDrafted, "Newsletter", issue.Id.ToString(), issue.Subject,
                newValues: "Prepared automatically when the post was published. Not sent.");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Newsletter draft for a published post was not created (non-fatal).");
            return false;
        }
    }
}
