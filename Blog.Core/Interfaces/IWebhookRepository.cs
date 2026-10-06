using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IWebhookRepository
{
    /// <summary>The owner's webhooks with their latest delivery summary, newest first. Bounded (a handful per site).</summary>
    Task<IReadOnlyList<Webhook>> GetAllAsync(Guid ownerId);

    Task<Webhook?> GetByIdAsync(Guid id, Guid ownerId);

    /// <summary>Enabled webhooks of this owner, for fan-out. The caller filters by subscribed event.</summary>
    Task<IReadOnlyList<Webhook>> GetEnabledAsync(Guid ownerId);

    Task CreateAsync(Webhook webhook);
    Task UpdateAsync(Webhook webhook);

    /// <summary>Deletes the webhook and (by cascade) its delivery log; returns false when it is not the owner's.</summary>
    Task<bool> DeleteAsync(Guid id, Guid ownerId);

    /// <summary>
    /// Queues deliveries. A delivery with an <see cref="WebhookDelivery.EventKey"/> already queued for
    /// the same webhook is skipped, so repeating an announcement is harmless. Returns rows queued.
    /// </summary>
    Task<int> EnqueueAsync(IReadOnlyCollection<WebhookDelivery> deliveries);

    /// <summary>Inserts one delivery (no event-key de-duplication) and returns its id. Used for tests and redeliveries sent at once.</summary>
    Task<long> CreateDeliveryAsync(WebhookDelivery delivery);

    /// <summary>Pending deliveries whose next attempt is due, oldest first, whose webhook is still enabled.</summary>
    Task<IReadOnlyList<WebhookDelivery>> GetDueAsync(DateTime nowUtc, int take);

    /// <summary>Writes the outcome of an attempt (status, attempts, next attempt, response).</summary>
    Task RecordAttemptAsync(WebhookDelivery delivery);

    Task<WebhookDelivery?> GetDeliveryAsync(long id, Guid ownerId);

    /// <summary>One page of a webhook's delivery log, newest first, optionally filtered by event or status.</summary>
    Task<IReadOnlyList<WebhookDelivery>> SearchDeliveriesAsync(Guid webhookId, Guid ownerId, string? q, int page, int pageSize);
    Task<int> CountDeliveriesAsync(Guid webhookId, Guid ownerId, string? q);

    /// <summary>Removes finished deliveries (succeeded or failed) created before the cut-off; returns rows removed.</summary>
    Task<int> PruneAsync(DateTime olderThanUtc);
}
