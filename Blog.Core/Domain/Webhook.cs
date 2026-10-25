namespace Blog.Core.Domain;

/// <summary>
/// An outbound webhook: a URL that receives a signed JSON POST when a subscribed event happens.
/// The signing secret is stored Data-Protection-encrypted (<see cref="SecretProtected"/>) and is never
/// shown again after it is saved.
/// </summary>
public class Webhook
{
    public Guid     Id              { get; set; }
    public Guid     OwnerId         { get; set; }
    public string   Name            { get; set; } = "";
    public string   Url             { get; set; } = "";
    public string   SecretProtected { get; set; } = "";
    /// <summary>Comma-separated <see cref="WebhookEvents"/> names.</summary>
    public string   Events          { get; set; } = "";
    public bool     IsEnabled       { get; set; } = true;
    /// <summary>The site address captured when the webhook was saved; used for absolute URLs in payloads when no canonical host is configured.</summary>
    public string?  SiteBaseUrl     { get; set; }
    public DateTime CreatedAt       { get; set; }
    public DateTime UpdatedAt       { get; set; }

    // Read-only summary for the list, filled by the repository.
    public DateTime? LastDeliveryAt     { get; set; }
    public string?   LastDeliveryStatus { get; set; }
    public int?      LastResponseCode   { get; set; }

    public IReadOnlyList<string> EventList =>
        Events.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>One attempt-tracked delivery of one event to one webhook.</summary>
public class WebhookDelivery
{
    public long      Id              { get; set; }
    public Guid      WebhookId       { get; set; }
    public Guid      OwnerId         { get; set; }
    /// <summary>Stable id of the event, sent as <c>X-Inkwell-Delivery</c>; a redelivery keeps it so receivers can de-duplicate.</summary>
    public Guid      EventId         { get; set; }
    public string    EventType       { get; set; } = "";
    /// <summary>Optional idempotency key; unique per webhook, so the same scheduled publish is never queued twice.</summary>
    public string?   EventKey        { get; set; }
    public string    PayloadJson     { get; set; } = "";
    public string    Status          { get; set; } = WebhookDeliveryStatus.Pending;
    public int       Attempts        { get; set; }
    public DateTime  NextAttemptAt   { get; set; }
    public DateTime? LastAttemptAt   { get; set; }
    public int?      ResponseCode    { get; set; }
    public string?   ResponseSnippet { get; set; }
    public string?   Error           { get; set; }
    public DateTime  CreatedAt       { get; set; }
}

public static class WebhookDeliveryStatus
{
    public const string Pending   = "Pending";
    public const string Succeeded = "Succeeded";
    /// <summary>Every retry was used; kept for the log and a manual redeliver.</summary>
    public const string Failed    = "Failed";
}

/// <summary>The events a webhook can subscribe to. Names are the public contract: never rename one.</summary>
public static class WebhookEvents
{
    public const string PostPublished   = "post.published";
    public const string PostUpdated     = "post.updated";
    public const string PostUnpublished = "post.unpublished";
    public const string PagePublished   = "page.published";
    public const string CommentCreated  = "comment.created";
    public const string FormSubmitted   = "form.submitted";
    /// <summary>Sent only by the Desk's "Send test" button; not subscribable.</summary>
    public const string Test            = "webhook.test";

    public static readonly IReadOnlyList<(string Name, string Description)> All =
    [
        (PostPublished,   "A post goes live, including a scheduled post reaching its time"),
        (PostUpdated,     "A live post is saved again"),
        (PostUnpublished, "A live post is moved back to draft"),
        (PagePublished,   "A page is published or a published page is saved"),
        (CommentCreated,  "A reader submits a comment (sent while it awaits moderation)"),
        (FormSubmitted,   "A visitor sends a contact form (the form's fields only)"),
    ];

    public static bool IsSubscribable(string name) => All.Any(e => e.Name == name);
}
