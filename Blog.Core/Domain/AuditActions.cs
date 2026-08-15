namespace Blog.Core.Domain;

/// <summary>
/// Canonical action strings for the audit log. Format: EntityType.Verb.
/// Always use these constants — never free-form strings — so the filter dropdown works.
/// </summary>
public static class AuditActions
{
    // Posts
    public const string PostCreated    = "Post.Created";
    public const string PostUpdated    = "Post.Updated";
    public const string PostPublished  = "Post.Published";
    public const string PostUnpublished = "Post.Unpublished";
    public const string PostScheduled  = "Post.Scheduled";
    public const string PostDeleted    = "Post.Deleted";

    // Pages
    public const string PageCreated    = "Page.Created";
    public const string PageUpdated    = "Page.Updated";
    public const string PageDeleted    = "Page.Deleted";

    // Comments
    public const string CommentApproved = "Comment.Approved";
    public const string CommentRejected = "Comment.Rejected";
    public const string CommentDeleted  = "Comment.Deleted";

    // Media
    public const string MediaUploaded = "Media.Uploaded";
    public const string MediaDeleted  = "Media.Deleted";

    // Users
    public const string UserInvited      = "User.Invited";
    public const string UserDeleted      = "User.Deleted";
    public const string UserRoleChanged  = "User.RoleChanged";
    public const string UserToggled      = "User.ActiveToggled";

    // Auth
    public const string AuthLoggedIn    = "Auth.LoggedIn";
    public const string AuthLoggedOut   = "Auth.LoggedOut";
    public const string AuthLoginFailed = "Auth.LoginFailed";

    // Settings
    public const string SettingsUpdated = "Settings.Updated";

    // Theme
    public const string ThemeUpdated      = "Theme.Updated";
    public const string ThemePresetApplied = "Theme.PresetApplied";
    public const string ThemeLayoutApplied = "Theme.LayoutApplied";

    // Taxonomy
    public const string CategoryCreated = "Category.Created";
    public const string CategoryDeleted = "Category.Deleted";
    public const string TagCreated      = "Tag.Created";
    public const string TagDeleted      = "Tag.Deleted";
    public const string SeriesCreated      = "Series.Created";
    public const string SeriesUpdated      = "Series.Updated";
    public const string SeriesDeleted      = "Series.Deleted";
    public const string SeriesPostsUpdated = "Series.PostsUpdated";

    // Newsletter / Subscribers
    public const string NewsletterSent      = "Newsletter.Sent";
    public const string SubscriberDeleted   = "Subscriber.Deleted";
    public const string SubscriberExported  = "Subscriber.Exported";

    // Import (WordPress / Ghost migration)
    public const string ImportStarted   = "Import.Started";
    public const string ImportCompleted = "Import.Completed";
    public const string ImportCancelled = "Import.Cancelled";

    // Security / IP firewall
    public const string SecurityIpBlocked       = "Security.IpBlocked";
    public const string SecurityIpUnblocked     = "Security.IpUnblocked";
    public const string SecurityIpAllowed       = "Security.IpAllowed";
    public const string SecurityRuleDeleted     = "Security.RuleDeleted";
    public const string SecuritySettingsUpdated = "Security.SettingsUpdated";

    // Redirects
    public const string RedirectCreated = "Redirect.Created";
    public const string RedirectUpdated = "Redirect.Updated";
    public const string RedirectDeleted = "Redirect.Deleted";
}
