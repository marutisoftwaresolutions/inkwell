using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services.Webhooks;

namespace Blog.Web.Services.Jobs;

/// <summary>
/// Sends queued webhook deliveries, retrying failures on the <see cref="WebhookRules.RetryDelay"/>
/// schedule (1 min, 5 min, 30 min, 2 h; five attempts in all). It also announces scheduled posts
/// whose time has come, because those go live on read rather than through a save, and prunes
/// finished deliveries after <see cref="RetentionDays"/> days.
///
/// Bounded per run (<see cref="BatchSize"/> deliveries, <see cref="TimeBudget"/>) so one slow
/// endpoint cannot hold the single scheduler hostage; whatever is left waits for the next minute.
/// </summary>
public sealed class WebhookDeliveryJob : IScheduledJob
{
    public const int BatchSize = 20;
    public const int RetentionDays = 30;
    public static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(40);
    /// <summary>How far back a reached schedule is still announced (covers an app-pool outage); the event key prevents repeats.</summary>
    public static readonly TimeSpan ScheduleLookback = TimeSpan.FromHours(24);

    private readonly IWebhookRepository _webhooks;
    private readonly IPostRepository _posts;
    private readonly WebhookPublisher _publisher;
    private readonly IWebhookSender _sender;

    public WebhookDeliveryJob(IWebhookRepository webhooks, IPostRepository posts, WebhookPublisher publisher, IWebhookSender sender)
    {
        _webhooks = webhooks;
        _posts = posts;
        _publisher = publisher;
        _sender = sender;
    }

    public string Name => "webhooks";
    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var announced = 0;
        foreach (var post in await _posts.GetScheduledReachedAsync(now - ScheduleLookback, now))
            announced += await _publisher.ScheduledPostReachedAsync(post);

        var (sent, failed, gaveUp) = await DeliverDueAsync(cancellationToken);
        var pruned = await _webhooks.PruneAsync(now.AddDays(-RetentionDays));

        return $"Queued {announced} scheduled-post event(s); delivered {sent}, failed {failed} (will retry), gave up on {gaveUp}; pruned {pruned}.";
    }

    public async Task<(int Sent, int Failed, int GaveUp)> DeliverDueAsync(CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var due = await _webhooks.GetDueAsync(started, BatchSize);
        int sent = 0, failed = 0, gaveUp = 0;
        var cache = new Dictionary<(Guid, Guid), Webhook?>();

        foreach (var delivery in due)
        {
            if (ct.IsCancellationRequested || DateTime.UtcNow - started > TimeBudget) break;

            var key = (delivery.WebhookId, delivery.OwnerId);
            if (!cache.TryGetValue(key, out var webhook))
                cache[key] = webhook = await _webhooks.GetByIdAsync(delivery.WebhookId, delivery.OwnerId);
            if (webhook is null) continue;   // deleted meanwhile; the cascade removes the row

            var outcome = await AttemptAsync(webhook, delivery, ct);
            if (outcome == WebhookDeliveryStatus.Succeeded) sent++;
            else if (outcome == WebhookDeliveryStatus.Failed) gaveUp++;
            else failed++;
        }

        return (sent, failed, gaveUp);
    }

    /// <summary>One attempt, recorded. Returns the delivery's status afterwards.</summary>
    public async Task<string> AttemptAsync(Webhook webhook, WebhookDelivery delivery, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var secret = _publisher.UnprotectSecret(webhook.SecretProtected);
        var result = secret is null
            ? new WebhookSendResult(false, null, null, "The signing secret can no longer be read (the key ring changed). Enter a new secret for this webhook.")
            : await _sender.SendAsync(webhook.Url, secret, delivery.EventType, delivery.EventId, delivery.PayloadJson, ct);

        delivery.Attempts++;
        delivery.LastAttemptAt = now;
        delivery.ResponseCode = result.StatusCode;
        delivery.ResponseSnippet = Trim(result.ResponseSnippet, 500);
        delivery.Error = Trim(result.Error, 500);

        if (result.Success)
        {
            delivery.Status = WebhookDeliveryStatus.Succeeded;
        }
        else if (WebhookRules.RetryDelay(delivery.Attempts) is { } delay)
        {
            delivery.Status = WebhookDeliveryStatus.Pending;
            delivery.NextAttemptAt = now + delay;
        }
        else
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
        }

        await _webhooks.RecordAttemptAsync(delivery);
        return delivery.Status;
    }

    private static string? Trim(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;
}
