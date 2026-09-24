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

    /// <summary>Content put back from a stored revision (payload names the revision number).</summary>
    public const string PostRevisionRestored = "Post.RevisionRestored";

    // Pages
    public const string PageCreated    = "Page.Created";
    public const string PageUpdated    = "Page.Updated";
    public const string PageDeleted    = "Page.Deleted";
    public const string PageRevisionRestored = "Page.RevisionRestored";

    // Comments
    public const string CommentApproved = "Comment.Approved";
    public const string CommentRejected = "Comment.Rejected";
    public const string CommentDeleted  = "Comment.Deleted";
    /// <summary>Deleted as spam by a moderator; the posting address was reported to the firewall.</summary>
    public const string CommentMarkedSpam = "Comment.MarkedSpam";
    /// <summary>A moderator replied from the Desk (the reply is a published comment by that user).</summary>
    public const string CommentReplied    = "Comment.Replied";

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
    /// <summary>A reset link was issued for an existing account (never logged for unknown addresses).</summary>
    public const string AuthPasswordResetRequested = "Auth.PasswordResetRequested";
    /// <summary>A password was changed through a reset link.</summary>
    public const string AuthPasswordReset = "Auth.PasswordReset";

    // Settings
    public const string SettingsUpdated = "Settings.Updated";
    /// <summary>A Search Console property was connected (payload names the property and service account, never the key).</summary>
    public const string SettingsSearchConsoleConnected    = "Settings.SearchConsoleConnected";
    public const string SettingsSearchConsoleDisconnected = "Settings.SearchConsoleDisconnected";

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
