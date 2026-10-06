using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// The editorial workflow's rules, pure so each is tested: which status a save produces, who may
/// approve or return a post, and — the promise readers depend on — which statuses are ever public.
///
/// The status a save produces is decided here from the button pressed and the user's permission,
/// never from a submitted Status field: before this, a request carrying Status=Published with any
/// other action published a post for a user who lacked the publish permission.
/// </summary>
public static class EditorialWorkflow
{
    public const int MaxNoteLength = 1000;

    /// <summary>Editor buttons (the form's <c>submitAction</c>).</summary>
    public static class Actions
    {
        public const string Publish = "publish";
        public const string Draft   = "draft";
        public const string Review  = "review";
    }

    /// <summary>
    /// Readers can see the post: published, or scheduled with its time passed. Nothing else — not
    /// drafts, and never InReview or NeedsChanges — is public, on any route.
    /// </summary>
    public static bool IsLive(PostStatus status, DateTime? scheduledAtUtc, DateTime nowUtc) =>
        status == PostStatus.Published || (status == PostStatus.Scheduled && scheduledAtUtc <= nowUtc);

    public static bool IsInWorkflow(PostStatus status) => status is PostStatus.InReview or PostStatus.NeedsChanges;

    /// <summary>The outcome of a save: the status to store, and a message for the user when it differs from what they asked.</summary>
    public sealed record SaveDecision(PostStatus Status, string? Notice, bool SubmittedForReview);

    /// <summary>
    /// <list type="bullet">
    /// <item><b>publish</b> — with the publish permission: Published, or Scheduled when the time is in the
    /// future. Without it: submitted for review (the old behaviour silently saved a draft).</item>
    /// <item><b>review</b> — submitted for review, by anyone.</item>
    /// <item><b>draft</b> — back to Draft (also withdraws a post from review).</item>
    /// <item>anything else — the status is left as it was (a new post starts as Draft).</item>
    /// </list>
    /// </summary>
    public static SaveDecision DecideOnSave(PostStatus? current, string? action, bool canPublish, DateTime? scheduledAtUtc, DateTime nowUtc)
    {
        // Was the post on the site before this save? Taking it off must be said, never silent.
        var wasLive = current is { } c && IsLive(c, scheduledAtUtc, nowUtc) || current == PostStatus.Published;
        const string OffSite = " The post is off the site until an editor approves it.";

        switch (action)
        {
            case Actions.Publish when canPublish:
                return new(scheduledAtUtc > nowUtc ? PostStatus.Scheduled : PostStatus.Published, null, false);
            case Actions.Publish:
                return new(PostStatus.InReview,
                    "Submitted for review. An editor will publish it or send it back with notes." + (wasLive ? OffSite : ""), true);
            case Actions.Review:
                return new(PostStatus.InReview, "Submitted for review." + (wasLive ? OffSite : ""), true);
            // Saving work on a returned post keeps it "needs changes" (and the note visible).
            case Actions.Draft when current == PostStatus.NeedsChanges:
                return new(PostStatus.NeedsChanges, null, false);
            // An editor saving edits to a post in review keeps it in review: saving is not a decision.
            case Actions.Draft when current == PostStatus.InReview && canPublish:
                return new(PostStatus.InReview, "Saved. The post is still waiting for review.", false);
            case Actions.Draft:
                return new(PostStatus.Draft,
                    wasLive ? "Saved as a draft. The post is no longer on the site."
                    : current == PostStatus.InReview ? "Saved as a draft and withdrawn from review." : null, false);
            default:
                return new(current ?? PostStatus.Draft, null, false);
        }
    }

    /// <summary>Only a post waiting for review can be approved or returned, and only by someone who may publish.</summary>
    public static bool CanReview(PostStatus status, bool canPublish) => canPublish && status == PostStatus.InReview;

    /// <summary>A returned post needs a note saying what to change. Returns null when valid.</summary>
    public static string? ValidateNote(string? note)
    {
        var n = (note ?? "").Trim();
        if (n.Length == 0) return "Say what needs to change, so the author knows what to fix.";
        if (n.Length > MaxNoteLength) return $"Keep the note to {MaxNoteLength} characters.";
        return null;
    }
}
