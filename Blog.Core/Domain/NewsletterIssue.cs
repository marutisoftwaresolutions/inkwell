namespace Blog.Core.Domain;

/// <summary>
/// One newsletter (ADR-0028): written as a draft, queued by an Admin, then sent by the newsletter job to every
/// confirmed subscriber. Once queued, the rendered email is frozen in <see cref="RenderedHtml"/> /
/// <see cref="RenderedText"/> so every recipient gets the same issue even if the posts change meanwhile.
/// </summary>
public class NewsletterIssue
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Subject { get; set; } = string.Empty;
    /// <summary>The line inbox lists show after the subject. Optional.</summary>
    public string? Preheader { get; set; }
    /// <summary>The opening text, in Markdown.</summary>
    public string? IntroMarkdown { get; set; }
    /// <summary>The posts featured, in order, as comma-separated ids.</summary>
    public string? PostIds { get; set; }
    public string Status { get; set; } = NewsletterStatus.Draft;
    public string? SiteBaseUrl { get; set; }
    public string? RenderedHtml { get; set; }
    public string? RenderedText { get; set; }
    public int RecipientCount { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? QueuedBy { get; set; }
    public string? QueuedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? QueuedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public IReadOnlyList<Guid> PostIdList =>
        (PostIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();

    public bool IsDraft => Status == NewsletterStatus.Draft;
    public bool IsActive => Status is NewsletterStatus.Queued or NewsletterStatus.Sending;
}

public static class NewsletterStatus
{
    public const string Draft = "Draft";
    public const string Queued = "Queued";
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Cancelled = "Cancelled";
}

/// <summary>One recipient of a queued issue, with the member's unsubscribe token for the per-recipient link.</summary>
public class NewsletterDelivery
{
    public long Id { get; set; }
    public Guid IssueId { get; set; }
    public Guid MemberId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Status { get; set; } = NewsletterDeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? Error { get; set; }

    // From Members, read with the delivery so the job never sends to someone who left meanwhile.
    public string? MemberName { get; set; }
    public string? MemberStatus { get; set; }
    public string? UnsubscribeToken { get; set; }
}

/// <summary>Copies of an issue by state.</summary>
public sealed record NewsletterProgress(int Pending, int Sent, int Failed, int Skipped, int Cancelled)
{
    public int Total => Pending + Sent + Failed + Skipped + Cancelled;
}

public static class NewsletterDeliveryStatus
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    /// <summary>Unsubscribed or removed after the issue was queued: not sent.</summary>
    public const string Skipped = "Skipped";
    public const string Cancelled = "Cancelled";
}
