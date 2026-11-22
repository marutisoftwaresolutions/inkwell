using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// Whether a reader's comment that passed the spam filter appears at once or waits for a moderator.
/// It waits when the site holds comments for approval, when it carries a link (the usual reason a stranger
/// comments on a site that publishes at once), or when it answers a comment readers cannot see yet.
/// </summary>
public static class CommentHolding
{
    public const string HeldMessage = "Your comment is awaiting moderation.";
    public const string PostedMessage = "Comment posted.";

    public static CommentStatus InitialStatus(bool holdForApproval, string? content, bool parentHidden) =>
        holdForApproval || parentHidden || CommentSpamFilter.CountLinks(content) > 0
            ? CommentStatus.Pending
            : CommentStatus.Approved;

    public static string Message(CommentStatus status) =>
        status == CommentStatus.Approved ? PostedMessage : HeldMessage;
}
