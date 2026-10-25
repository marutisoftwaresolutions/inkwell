using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Blog.Web.Services.Webhooks;

/// <summary>
/// Turns what happened (a post saved, a page published, a comment submitted) into queued deliveries
/// for every enabled webhook subscribed to that event. Nothing is sent here: the <c>webhooks</c> job
/// delivers, so a slow or dead endpoint can never hold up a publish. Every method swallows its own
/// failure (fail-open, architecture rule): webhooks are optional, publishing is not.
///
/// Payloads carry what a receiver needs to act (ids, title, slug, absolute URL, status, dates) and
/// never personal data it does not: a comment's author name and text are sent, its email and IP are
/// not.
/// </summary>
public sealed class WebhookPublisher
{
    private readonly IWebhookRepository _webhooks;
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly IConfiguration _config;
    private readonly IDataProtector _protector;
    private readonly ILogger<WebhookPublisher> _log;

    public const string SecretPurpose = "Inkwell.WebhookSecret.v1";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public WebhookPublisher(IWebhookRepository webhooks, ISettingRepository settings, IUserRepository users, IConfiguration config,
        IDataProtectionProvider protection, ILogger<WebhookPublisher> log)
    {
        _webhooks = webhooks;
        _settings = settings;
        _users = users;
        _config = config;
        _protector = protection.CreateProtector(SecretPurpose);
        _log = log;
    }

    // ── Secrets ──────────────────────────────────────────────────────────────

    public string ProtectSecret(string secret) => _protector.Protect(secret);

    /// <summary>The clear secret, or null when the key ring can no longer read it (re-enter it in the Desk).</summary>
    public string? UnprotectSecret(string protectedSecret)
    {
        try { return _protector.Unprotect(protectedSecret); }
        catch { return null; }
    }

    // ── Owner and site address ───────────────────────────────────────────────

    public bool IsCloudMode =>
        string.Equals(_config.GetValue<string>("DeploymentMode"), "Cloud", StringComparison.OrdinalIgnoreCase);

    /// <summary>Same tenancy rule as analytics: the tenant in cloud mode, otherwise the single site (Guid.Empty).</summary>
    public static Guid OwnerOf(HttpContext context) => TrafficRecorder.OwnerOf(context);

    /// <summary>The owner a post belongs to when there is no request (the job).</summary>
    public Guid OwnerOfPost(Post post) => IsCloudMode ? post.AuthorId : Guid.Empty;

    /// <summary>Canonical host from configuration when set, else the address the request came in on.</summary>
    public string? BaseUrl(HttpContext? context, Webhook? fallback = null)
    {
        var canonical = _config["CanonicalHost"];
        if (!string.IsNullOrWhiteSpace(canonical)) return "https://" + canonical.Trim().TrimEnd('/');
        if (context is not null) return $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}";
        return fallback?.SiteBaseUrl;
    }

    // ── Events ───────────────────────────────────────────────────────────────

    /// <summary>A post was created or saved. Decides published / updated / unpublished from what readers could see before and after.</summary>
    public async Task PostSavedAsync(HttpContext context, Post? before, Post after)
    {
        try
        {
            var now = DateTime.UtcNow;
            var wasLive = before is not null && WebhookRules.IsLive(before.Status, before.ScheduledAt, now);
            var isLive = WebhookRules.IsLive(after.Status, after.ScheduledAt, now);
            var eventType = WebhookRules.EventForPostSave(wasLive, isLive);
            if (eventType is null) return;

            await EnqueueAsync(OwnerOf(context), eventType, eventKey: null, BaseUrl(context), baseUrl => new { post = PostData(after, baseUrl) });
        }
        catch (Exception ex) { _log.LogWarning(ex, "Webhook post event not queued (non-fatal)."); }
    }

    /// <summary>A scheduled post reached its time (called by the job). Keyed so it is announced once per schedule.</summary>
    public async Task<int> ScheduledPostReachedAsync(Post post)
    {
        var key = $"{WebhookEvents.PostPublished}:{post.Id:N}:{post.ScheduledAt?.Ticks}";
        return await EnqueueAsync(OwnerOfPost(post), WebhookEvents.PostPublished, key, baseUrl: null,
            baseUrl => new { post = PostData(post, baseUrl) },
            onlyWebhooksCreatedBefore: post.ScheduledAt);
    }

    public async Task PageSavedAsync(HttpContext context, Page page)
    {
        try
        {
            if (!page.IsPublished) return;
            await EnqueueAsync(OwnerOf(context), WebhookEvents.PagePublished, null, BaseUrl(context), baseUrl => new
            {
                page = new { id = page.Id, title = page.Title, slug = page.Slug, url = Absolute(baseUrl, page.Slug), updatedAt = page.UpdatedAt }
            });
        }
        catch (Exception ex) { _log.LogWarning(ex, "Webhook page event not queued (non-fatal)."); }
    }

    public async Task CommentCreatedAsync(HttpContext context, Comment comment, Post post)
    {
        try
        {
            await EnqueueAsync(OwnerOf(context), WebhookEvents.CommentCreated, null, BaseUrl(context), baseUrl => new
            {
                comment = new
                {
                    id = comment.Id,
                    parentId = comment.ParentId,
                    authorName = comment.AuthorName,
                    content = comment.Content,
                    status = comment.Status.ToString(),
                    createdAt = comment.CreatedAt == default ? DateTime.UtcNow : comment.CreatedAt,
                },
                post = new { id = post.Id, title = post.Title, slug = post.Slug, url = Absolute(baseUrl, post.Slug) }
            });
        }
        catch (Exception ex) { _log.LogWarning(ex, "Webhook comment event not queued (non-fatal)."); }
    }

    /// <summary>
    /// A visitor sent a contact form (ADR-0030). The payload is the form's own fields as the visitor filled them in,
    /// and nothing else: never the sender's address or its hash.
    /// </summary>
    public async Task FormSubmittedAsync(HttpContext context, ContactForm form, long submissionId, IReadOnlyDictionary<string, string> values)
    {
        try
        {
            await EnqueueAsync(OwnerOf(context), WebhookEvents.FormSubmitted, null, BaseUrl(context), baseUrl => new
            {
                form = new { key = form.FormKey, name = form.Name },
                submission = new { id = submissionId, createdAt = DateTime.UtcNow, values },
            });
        }
        catch (Exception ex) { _log.LogWarning(ex, "Webhook form event not queued (non-fatal)."); }
    }

    /// <summary>The body the Desk's "Send test" button sends.</summary>
    public async Task<string> TestPayloadAsync(Guid ownerId, Webhook webhook, HttpContext context)
    {
        var baseUrl = BaseUrl(context, webhook);
        return await BuildBodyAsync(ownerId, Guid.NewGuid(), WebhookEvents.Test, baseUrl,
            new { message = "This is a test delivery from Inkwell. Verify the signature, then answer with any 2xx status." });
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private static object PostData(Post post, string? baseUrl) => new
    {
        id = post.Id,
        title = post.Title,
        slug = post.Slug,
        url = Absolute(baseUrl, post.Slug),
        status = post.Status.ToString(),
        excerpt = post.MetaDescription,
        publishedAt = post.PublishedAt ?? post.ScheduledAt,
        updatedAt = post.UpdatedAt,
    };

    private static string? Absolute(string? baseUrl, string slug) =>
        string.IsNullOrEmpty(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/{slug}";

    private async Task<int> EnqueueAsync(Guid ownerId, string eventType, string? eventKey, string? baseUrl,
        Func<string?, object> data, DateTime? onlyWebhooksCreatedBefore = null)
    {
        var targets = (await _webhooks.GetEnabledAsync(ownerId))
            .Where(w => w.EventList.Contains(eventType))
            .Where(w => onlyWebhooksCreatedBefore is null || w.CreatedAt < onlyWebhooksCreatedBefore)
            .ToList();
        if (targets.Count == 0) return 0;

        var eventId = Guid.NewGuid();
        var resolvedBase = baseUrl ?? BaseUrl(null, targets[0]);
        var body = await BuildBodyAsync(ownerId, eventId, eventType, resolvedBase, data(resolvedBase));
        var now = DateTime.UtcNow;

        return await _webhooks.EnqueueAsync(targets.Select(w => new WebhookDelivery
        {
            WebhookId = w.Id,
            OwnerId = ownerId,
            EventId = eventId,
            EventType = eventType,
            EventKey = eventKey,
            PayloadJson = body,
            Status = WebhookDeliveryStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
        }).ToList());
    }

    private async Task<string> BuildBodyAsync(Guid ownerId, Guid eventId, string eventType, string? baseUrl, object data)
    {
        string? siteName = null;
        try
        {
            // Self-hosted settings live under the first admin (as the public controllers resolve them).
            var settingsOwner = ownerId != Guid.Empty ? ownerId : (await _users.GetFirstAdminAsync())?.Id ?? Guid.Empty;
            siteName = (await _settings.GetSettingsAsync(settingsOwner)).SiteName;
        }
        catch { /* the name is optional */ }

        return JsonSerializer.Serialize(new
        {
            id = eventId,
            @event = eventType,
            createdAt = DateTime.UtcNow,
            site = new { name = siteName, url = baseUrl },
            data,
        }, Json);
    }
}
