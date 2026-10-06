using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

/// <summary>Newsletter issues and their per-recipient deliveries (ADR-0028).</summary>
public interface INewsletterRepository
{
    // ── Desk ──
    Task<IReadOnlyList<NewsletterIssue>> ListAsync(Guid ownerId);
    Task<NewsletterIssue?> GetAsync(Guid id, Guid ownerId);
    Task CreateAsync(NewsletterIssue issue);
    /// <summary>Saves subject, preheader, intro and posts; only while the issue is a draft. False otherwise.</summary>
    Task<bool> UpdateDraftAsync(NewsletterIssue issue);
    /// <summary>Deletes a draft. Queued and sent issues are history and cannot be deleted.</summary>
    Task<bool> DeleteDraftAsync(Guid id, Guid ownerId);
    /// <summary>Draft → Queued with the frozen email. Atomic: false if it was not a draft any more.</summary>
    Task<bool> QueueAsync(Guid id, Guid ownerId, string html, string text, string siteBaseUrl, Guid queuedBy, string? queuedByName, DateTime nowUtc);
    /// <summary>Queued/Sending → Cancelled; deliveries not yet sent become Cancelled. False if it was not active.</summary>
    Task<bool> CancelAsync(Guid id, Guid ownerId, DateTime nowUtc);
    /// <summary>How many copies of an issue are in each state, counted in full (the send log shows only a page).</summary>
    Task<NewsletterProgress> GetProgressAsync(Guid issueId, Guid ownerId);
    /// <summary>The latest deliveries of an issue, for the send log.</summary>
    Task<IReadOnlyList<NewsletterDelivery>> GetDeliveriesAsync(Guid issueId, Guid ownerId, int take);

    // ── Newsletter job (no tenant context) ──
    Task<IReadOnlyList<NewsletterIssue>> GetActiveAsync();
    /// <summary>
    /// For a Queued issue: one delivery per confirmed subscriber (idempotent), RecipientCount set, status Sending.
    /// Returns the number of recipients.
    /// </summary>
    Task<int> ExpandRecipientsAsync(Guid issueId, DateTime nowUtc);
    Task<IReadOnlyList<NewsletterDelivery>> GetDueAsync(Guid issueId, DateTime nowUtc, int take);
    Task MarkDeliveryAsync(long deliveryId, string status, int attempts, DateTime nextAttemptAt, DateTime? sentAt, string? error);
    /// <summary>When nothing is Pending: counts recorded, status Sent, FinishedAt set. Returns the finished issue, else null.</summary>
    Task<NewsletterIssue?> FinishIfDoneAsync(Guid issueId, DateTime nowUtc);
}
