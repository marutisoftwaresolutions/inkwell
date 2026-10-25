using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Core.Services.Forms;
using Blog.Web.Services;
using Blog.Web.Services.Forms;
using Blog.Web.Services.Webhooks;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Receives contact-form submissions from public pages (ADR-0030): <c>POST /forms/{key}</c>. Fields are checked
/// against the form's definition and the spam gate (signed-in staff skip it, as with comments); spam is discarded
/// with the same reply a real submission gets and reported to the IP firewall. A kept submission is stored, the
/// form's notification address is emailed, and <c>form.submitted</c> is sent to webhooks; email and webhooks fail
/// soft. Without JavaScript the reader is sent back to the form with the outcome; with it, the answer is JSON.
/// </summary>
public class PublicFormsController : Controller
{
    private readonly IFormRepository _forms;
    private readonly CommentFormTokenService _tokens;
    private readonly Blog.Web.Services.Security.IpFirewallService _firewall;
    private readonly IpHasher _ipHasher;
    private readonly IEmailService _email;
    private readonly WebhookPublisher _webhooks;
    private readonly ISettingRepository _settings;
    private readonly ILogger<PublicFormsController> _log;

    public PublicFormsController(IFormRepository forms, CommentFormTokenService tokens, Blog.Web.Services.Security.IpFirewallService firewall,
        IpHasher ipHasher, IEmailService email, WebhookPublisher webhooks, ISettingRepository settings, ILogger<PublicFormsController> log)
    {
        _forms = forms;
        _tokens = tokens;
        _firewall = firewall;
        _ipHasher = ipHasher;
        _email = email;
        _webhooks = webhooks;
        _settings = settings;
        _log = log;
    }

    public const string Expired = "This form has expired. Reload the page and send it again.";

    [HttpPost("forms/{key}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(string key)
    {
        var owner = TrafficRecorder.OwnerOf(HttpContext);
        var form = await _forms.GetByKeyAsync((key ?? "").ToLowerInvariant(), owner);
        if (form is null || !form.IsActive) return NotFound();

        var fields = FormRules.ParseFields(form.FieldsJson);
        var (values, problems) = FormRules.CheckSubmission(fields, k => Request.Form[FormEmbedRenderer.FieldPrefix + k].ToString());
        var success = string.IsNullOrWhiteSpace(form.SuccessMessage) ? "Thank you. Your message has been sent." : form.SuccessMessage!;
        var json = Request.Headers.XRequestedWith == "fetch";

        if (problems.Count > 0)
            return Outcome(form, json, 400, errors: problems.Where(p => p.Field is not null).ToDictionary(p => p.Field!, p => p.Message), values: values);

        var ip = _firewall.ResolveClientIp(HttpContext);
        if (string.IsNullOrEmpty(ip)) ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var addressHash = string.IsNullOrEmpty(ip) ? null : _ipHasher.Hash(ip);
        var contentHash = FormRules.ContentHash(values);

        if (User.Identity?.IsAuthenticated != true)
        {
            var state = _tokens.Validate(Request.Form[FormEmbedRenderer.TokenField].ToString(), form.Id, out var age);
            var verdict = FormSpamFilter.Evaluate(new FormSpamCheck(values, fields,
                Request.Form[CommentFormTokenService.HoneypotFieldName].ToString(), state, age,
                await _forms.HasRecentRepeatAsync(form.Id, contentHash, DateTime.UtcNow - FormSpamFilter.RepeatWindow),
                addressHash is null ? 0 : await _forms.CountRecentFromAddressAsync(owner, addressHash, DateTime.UtcNow - FormSpamFilter.RateWindow)));
            switch (verdict.Action)
            {
                case CommentSpamAction.AskToRetry:
                    return Outcome(form, json, 400, notice: Expired, values: values);
                case CommentSpamAction.RejectSilently:
                    // Nothing is stored; the reply matches a real submission so the sender learns nothing.
                    _log.LogInformation("Form {Key} submission discarded as spam: {Reason}", form.FormKey, verdict.Reason);
                    await _firewall.RegisterCommentSpamAsync(HttpContext, "form: " + verdict.Reason);
                    return Outcome(form, json, 200, sent: success);
            }
        }

        var id = await _forms.AddSubmissionAsync(new FormSubmission
        {
            FormId = form.Id, OwnerId = owner, ValuesJson = JsonSerializer.Serialize(values),
            SearchText = FormRules.SearchText(values), AddressHash = addressHash, ContentHash = contentHash,
        });
        await NotifyAsync(form, fields, values, id);
        await _webhooks.FormSubmittedAsync(HttpContext, form, id, values);
        return Outcome(form, json, 200, sent: success);
    }

    public static readonly TimeSpan NotifyTimeout = TimeSpan.FromSeconds(15);

    private async Task NotifyAsync(ContactForm form, IReadOnlyList<FormField> fields, IReadOnlyDictionary<string, string> values, long id)
    {
        if (string.IsNullOrWhiteSpace(form.NotifyEmail) || !_email.IsConfigured) return;
        try
        {
            var site = (await _settings.GetSettingsAsync(form.OwnerId)).SiteName;
            var rows = fields.Select(f => (f.Label, Value: values.GetValueOrDefault(f.Key) ?? "")).ToList();
            var html = new System.Text.StringBuilder("<!DOCTYPE html><html><body style=\"font-family:sans-serif;line-height:1.6;color:#111\">")
                .Append("<h2 style=\"margin:0 0 12px\">").Append(System.Net.WebUtility.HtmlEncode(form.Name)).Append("</h2><table cellpadding=\"6\" style=\"border-collapse:collapse\">");
            foreach (var (label, value) in rows)
                html.Append("<tr><th align=\"left\" valign=\"top\" style=\"border-bottom:1px solid #ddd\">").Append(System.Net.WebUtility.HtmlEncode(label))
                    .Append("</th><td style=\"border-bottom:1px solid #ddd;white-space:pre-wrap\">").Append(System.Net.WebUtility.HtmlEncode(value)).Append("</td></tr>");
            html.Append("</table><p style=\"color:#555;font-size:13px\">Sent from ").Append(System.Net.WebUtility.HtmlEncode(site ?? "your site"))
                .Append(". All submissions are in Admin → Forms.</p></body></html>");
            var text = string.Join("\n", rows.Select(r => $"{r.Label}: {r.Value}")) + "\n\nAll submissions are in Admin → Forms.";
            var reply = FormRules.ReplyAddress(fields, values);
            // The submission is already stored: a mail server that accepts the connection and never answers must not
            // keep the reader waiting (SendMailAsync ignores SmtpClient.Timeout; only the token stops it).
            // Not linked to the request: a visitor who closes the tab must not cancel the owner's notification.
            using var timeout = new CancellationTokenSource();
            timeout.CancelAfter(NotifyTimeout);
            var result = await _email.SendMessageAsync(new EmailMessage(form.NotifyEmail!, null, $"New message: {form.Name}", html.ToString(), text,
                reply is null ? null : new Dictionary<string, string> { ["Reply-To"] = reply }), timeout.Token);
            if (!result.Sent) _log.LogWarning("Form {Key} notification for submission {Id} not sent: {Error}", form.FormKey, id, result.Error);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Form {Key} notification failed (non-fatal).", form.FormKey); }
    }

    /// <summary>JSON for the in-page script; otherwise TempData and a redirect back to the form on the page it came from.</summary>
    private IActionResult Outcome(ContactForm form, bool json, int status, IReadOnlyDictionary<string, string>? errors = null,
        IReadOnlyDictionary<string, string>? values = null, string? notice = null, string? sent = null)
    {
        if (json)
            return new JsonResult(new { ok = sent is not null, message = sent ?? notice, errors = errors ?? new Dictionary<string, string>() }) { StatusCode = status };

        if (sent is not null) TempData[FormEmbedRenderer.SentKey(form.FormKey)] = sent;
        else
        {
            if (notice is not null) TempData[FormEmbedRenderer.NoticeKey(form.FormKey)] = notice;
            if (errors is { Count: > 0 }) TempData[FormEmbedRenderer.ErrorsKey(form.FormKey)] = JsonSerializer.Serialize(errors);
            if (values is { Count: > 0 }) TempData[FormEmbedRenderer.ValuesKey(form.FormKey)] = JsonSerializer.Serialize(values);
        }
        var back = Request.Form[FormEmbedRenderer.ReturnField].ToString();
        if (string.IsNullOrEmpty(back) || !Url.IsLocalUrl(back)) back = "/";
        return LocalRedirect(back + "#form-" + form.FormKey);
    }
}
