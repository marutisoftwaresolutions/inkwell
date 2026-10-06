using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Models;
using Blog.Web.Services;
using Blog.Web.Services.Jobs;
using Blog.Web.Services.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Webhooks. Endpoints that receive a signed JSON POST when posts, pages or comments change,
/// with a delivery log per endpoint, redelivery and a test send. Admin-only: a webhook sends site
/// content to an address of the operator's choosing, and holds a signing secret.
/// </summary>
[Authorize(Policy = "AdminOnly")]
[Route("admin/webhooks")]
public class WebhooksController : Controller
{
    public const int MaxWebhooks = 20;
    private const int DeliveryPageSize = 25;

    private readonly IWebhookRepository _webhooks;
    private readonly WebhookPublisher _publisher;
    private readonly WebhookDeliveryJob _job;
    private readonly AuditService _audit;

    public WebhooksController(IWebhookRepository webhooks, WebhookPublisher publisher, WebhookDeliveryJob job, AuditService audit)
    {
        _webhooks = webhooks;
        _publisher = publisher;
        _job = job;
        _audit = audit;
    }

    private Guid OwnerId => WebhookPublisher.OwnerOf(HttpContext);

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var all = await _webhooks.GetAllAsync(OwnerId);
        ViewData["ListSearchPlaceholder"] = "Search webhooks by name or address";
        ViewData["Title"] = "Webhooks";
        ViewBag.Total = all.Count;
        return View(ListPaging.Apply(this, all, q, page, w => new[] { w.Name, w.Url, w.Events }));
    }

    [HttpGet("new")]
    public async Task<IActionResult> Create()
    {
        if ((await _webhooks.GetAllAsync(OwnerId)).Count >= MaxWebhooks)
        {
            TempData["Error"] = $"A site can have at most {MaxWebhooks} webhooks. Delete one you no longer use first.";
            return RedirectToAction(nameof(Index));
        }
        ViewData["Title"] = "New webhook";
        return View("Edit", new WebhookEditModel { IsEnabled = true, Events = [WebhookEvents.PostPublished], Secret = WebhookRules.NewSecret(), IsNew = true });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var webhook = await _webhooks.GetByIdAsync(id, OwnerId);
        if (webhook is null) return NotFound();
        ViewData["Title"] = "Edit webhook";
        return View("Edit", WebhookEditModel.From(webhook));
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(WebhookEditModel model)
    {
        model.Name = (model.Name ?? "").Trim();
        model.Url = (model.Url ?? "").Trim();
        model.Events = (model.Events ?? []).Where(WebhookEvents.IsSubscribable).Distinct().ToList();

        if (model.Name.Length == 0) ModelState.AddModelError(nameof(model.Name), "Give the webhook a name you will recognise, such as “Rebuild static site”.");
        else if (model.Name.Length > 100) ModelState.AddModelError(nameof(model.Name), "Names can be at most 100 characters.");
        if (WebhookRules.ValidateUrl(model.Url) is { } urlError) ModelState.AddModelError(nameof(model.Url), urlError);
        if (model.Events.Count == 0) ModelState.AddModelError(nameof(model.Events), "Choose at least one event.");

        var existing = model.IsNew ? null : await _webhooks.GetByIdAsync(model.Id, OwnerId);
        if (!model.IsNew && existing is null) return NotFound();

        var secret = (model.Secret ?? "").Trim();
        if (model.IsNew && secret.Length < 16)
            ModelState.AddModelError(nameof(model.Secret), "Use a signing secret of at least 16 characters, or keep the generated one.");
        if (!model.IsNew && secret.Length is > 0 and < 16)
            ModelState.AddModelError(nameof(model.Secret), "Use at least 16 characters, or leave it blank to keep the current secret.");
        if (model.IsNew && (await _webhooks.GetAllAsync(OwnerId)).Count >= MaxWebhooks)
            ModelState.AddModelError("", $"A site can have at most {MaxWebhooks} webhooks.");

        if (!ModelState.IsValid)
        {
            ViewData["Title"] = model.IsNew ? "New webhook" : "Edit webhook";
            return View("Edit", model);
        }

        var now = DateTime.UtcNow;
        var webhook = existing ?? new Webhook { Id = Guid.NewGuid(), OwnerId = OwnerId, CreatedAt = now };
        webhook.Name = model.Name;
        webhook.Url = model.Url;
        webhook.Events = string.Join(",", model.Events);
        webhook.IsEnabled = model.IsEnabled;
        webhook.SiteBaseUrl = _publisher.BaseUrl(HttpContext);
        webhook.UpdatedAt = now;
        if (secret.Length > 0) webhook.SecretProtected = _publisher.ProtectSecret(secret);

        if (existing is null) await _webhooks.CreateAsync(webhook);
        else await _webhooks.UpdateAsync(webhook);

        // The secret is never written to the audit trail — only whether it changed.
        await _audit.LogAsync(existing is null ? AuditActions.WebhookCreated : AuditActions.WebhookUpdated, "Webhook",
            webhook.Id.ToString(), $"{webhook.Name} → {webhook.Url} ({webhook.Events}){(existing is not null && secret.Length > 0 ? "; secret replaced" : "")}");

        if (existing is null)
        {
            // The secret is gone from the screen now; say so honestly, and land where "Send test" is the next step.
            TempData["Success"] = "Webhook created. Use Send test to check the endpoint. If you did not copy the signing secret, edit the webhook and enter a new one.";
            return RedirectToAction(nameof(Deliveries), new { id = webhook.Id });
        }
        TempData["Success"] = "Webhook saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string? q, int page = 1)
    {
        var webhook = await _webhooks.GetByIdAsync(id, OwnerId);
        if (webhook is null) return NotFound();

        await _webhooks.DeleteAsync(id, OwnerId);
        await _audit.LogAsync(AuditActions.WebhookDeleted, "Webhook", id.ToString(), $"{webhook.Name} → {webhook.Url}");
        TempData["Success"] = "Webhook deleted.";
        return RedirectToAction(nameof(Index), new { q, page });
    }

    [HttpGet("{id:guid}/deliveries")]
    public async Task<IActionResult> Deliveries(Guid id, string? q, int page = 1)
    {
        var webhook = await _webhooks.GetByIdAsync(id, OwnerId);
        if (webhook is null) return NotFound();

        var total = await _webhooks.CountDeliveriesAsync(id, OwnerId, q);
        var meta = PagingMeta.FromCounts(q, page, total, DeliveryPageSize, 0);
        var rows = await _webhooks.SearchDeliveriesAsync(id, OwnerId, q, meta.Page, DeliveryPageSize);
        ListPaging.Stash(this, PagingMeta.FromCounts(q, meta.Page, total, DeliveryPageSize, rows.Count));

        ViewBag.Webhook = webhook;
        ViewBag.TotalAll = string.IsNullOrWhiteSpace(q) ? total : await _webhooks.CountDeliveriesAsync(id, OwnerId, null);
        ViewData["ListSearchPlaceholder"] = "Filter by event, status or error";
        ViewData["Title"] = $"Deliveries — {webhook.Name}";
        return View(rows);
    }

    /// <summary>Sends the stored payload again now, as a new delivery with the same event id.</summary>
    [HttpPost("{id:guid}/deliveries/{deliveryId:long}/redeliver")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Redeliver(Guid id, long deliveryId, string? q, int page = 1)
    {
        var webhook = await _webhooks.GetByIdAsync(id, OwnerId);
        var original = await _webhooks.GetDeliveryAsync(deliveryId, OwnerId);
        if (webhook is null || original is null || original.WebhookId != id) return NotFound();

        var copy = await QueueAsync(webhook, original.EventType, original.EventId, original.PayloadJson);
        var status = copy is null ? null : await _job.AttemptAsync(webhook, copy, HttpContext.RequestAborted);

        await _audit.LogAsync(AuditActions.WebhookRedelivered, "Webhook", id.ToString(), $"{webhook.Name}: {original.EventType} (delivery {deliveryId})");
        Report(status, copy, "Redelivered");
        return RedirectToAction(nameof(Deliveries), new { id, q, page });
    }

    /// <summary>Sends a <c>webhook.test</c> event now and reports the endpoint's answer.</summary>
    [HttpPost("{id:guid}/test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(Guid id)
    {
        var webhook = await _webhooks.GetByIdAsync(id, OwnerId);
        if (webhook is null) return NotFound();

        var body = await _publisher.TestPayloadAsync(OwnerId, webhook, HttpContext);
        var copy = await QueueAsync(webhook, WebhookEvents.Test, Guid.NewGuid(), body);
        var status = copy is null ? null : await _job.AttemptAsync(webhook, copy, HttpContext.RequestAborted);

        await _audit.LogAsync(AuditActions.WebhookTested, "Webhook", id.ToString(), webhook.Name);
        Report(status, copy, "Test delivered");
        return RedirectToAction(nameof(Deliveries), new { id });
    }

    private async Task<WebhookDelivery?> QueueAsync(Webhook webhook, string eventType, Guid eventId, string body)
    {
        var now = DateTime.UtcNow;
        var delivery = new WebhookDelivery
        {
            WebhookId = webhook.Id, OwnerId = OwnerId, EventId = eventId, EventType = eventType,
            PayloadJson = body, Status = WebhookDeliveryStatus.Pending, NextAttemptAt = now, CreatedAt = now,
        };
        try
        {
            await _webhooks.CreateDeliveryAsync(delivery);
            return delivery;
        }
        catch
        {
            return null;
        }
    }

    private void Report(string? status, WebhookDelivery? delivery, string successVerb)
    {
        if (status == WebhookDeliveryStatus.Succeeded)
            TempData["Success"] = $"{successVerb}: the endpoint answered HTTP {delivery?.ResponseCode}.";
        else if (delivery is null)
            TempData["Error"] = "The delivery could not be queued. Try again.";
        else
            TempData["Error"] = $"The endpoint did not accept it: {delivery.Error ?? "no answer"}. It will be retried automatically.";
    }
}

/// <summary>The editor's form model. The secret is write-only: it is never read back into the form.</summary>
public sealed class WebhookEditModel
{
    public Guid Id { get; set; }
    public bool IsNew { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public List<string> Events { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
    /// <summary>New: pre-filled with a generated secret. Edit: blank means "keep the current one".</summary>
    public string? Secret { get; set; }

    public static WebhookEditModel From(Webhook w) => new()
    {
        Id = w.Id, Name = w.Name, Url = w.Url, Events = w.EventList.ToList(), IsEnabled = w.IsEnabled, Secret = null,
    };
}
