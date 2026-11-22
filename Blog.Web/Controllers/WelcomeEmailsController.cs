using System.Security.Claims;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Blog.Web.Services.Newsletter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Newsletter → Welcome emails: up to three emails sent automatically some days after a reader
/// confirms their subscription. This screen only edits them; the <c>welcome-sequence</c> job sends.
/// Switching a first email on starts the sequence from that moment: people already on the list never
/// receive it. Saving and test sends are audited. Not offered in cloud mode, like the newsletter.
/// </summary>
[Authorize(Policy = "AdminOnly")]
[Route("admin/newsletter/welcome")]
public class WelcomeEmailsController : Controller
{
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly NewsletterService _newsletter;
    private readonly IEmailService _email;
    private readonly AuditService _audit;
    private readonly ITenantContext _tenant;

    public WelcomeEmailsController(ISettingRepository settings, IUserRepository users, NewsletterService newsletter, IEmailService email,
        AuditService audit, ITenantContext tenant)
    {
        _settings = settings;
        _users = users;
        _newsletter = newsletter;
        _email = email;
        _audit = audit;
        _tenant = tenant;
    }

    private async Task<Guid> OwnerAsync() => (await _users.GetFirstAdminAsync())?.Id ?? Guid.Empty;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (_tenant.IsCloudMode) return NotFound();
        var site = await _settings.GetSettingsAsync(await OwnerAsync());
        ViewBag.EmailConfigured = _email.IsConfigured;
        ViewBag.StartedAt = site.WelcomeSequenceStartedAt;
        return View(WelcomeSequence.Normalize(site.WelcomeEmails));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(List<WelcomeEmail>? emails)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var steps = WelcomeSequence.Normalize(emails);
        var errors = WelcomeSequence.Validate(steps);
        if (errors.Count > 0)
        {
            ViewBag.Errors = errors;
            ViewBag.EmailConfigured = _email.IsConfigured;
            return View("Index", steps);
        }

        var owner = await OwnerAsync();
        var site = await _settings.GetSettingsAsync(owner);
        var wasOn = WelcomeSequence.AnyEnabled(site.WelcomeEmails);
        var isOn = WelcomeSequence.AnyEnabled(steps);
        site.WelcomeEmails = steps;
        site.WelcomeSiteUrl = _newsletter.SiteUrl(HttpContext);
        // Starting, or starting again after being off, counts from now: nobody who confirmed earlier is mailed.
        if (isOn && !wasOn) site.WelcomeSequenceStartedAt = DateTime.UtcNow;
        await _settings.SaveSettingsAsync(owner, site);

        await _audit.LogAsync(AuditActions.NewsletterWelcomeUpdated, "Newsletter", null, "Welcome emails",
            newValues: string.Join("; ", steps.Select((s, i) => $"Email {i + 1}: {(s.Enabled ? $"on, day {s.DelayDays}, \"{s.Subject}\"" : "off")}")));
        TempData["Success"] = isOn
            ? "Welcome emails saved. They go to people who confirm from now on."
            : "Welcome emails saved. None is switched on, so nothing is sent.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Sends one saved email to the signed-in admin, marked as a test.</summary>
    [HttpPost("test/{step:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(int step)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var site = await _settings.GetSettingsAsync(await OwnerAsync());
        var steps = WelcomeSequence.Normalize(site.WelcomeEmails);
        var to = User.FindFirstValue(ClaimTypes.Email);
        if (step < 1 || step > steps.Count || steps[step - 1].Subject.Length == 0 || steps[step - 1].BodyMarkdown.Length == 0)
        {
            TempData["Error"] = "Save that email with a subject and a message first, then send the test.";
            return RedirectToAction(nameof(Index));
        }
        if (string.IsNullOrEmpty(to) || !_email.IsConfigured)
        {
            TempData["Error"] = "Outgoing email is not set up on this server, so the test could not be sent.";
            return RedirectToAction(nameof(Index));
        }

        var email = steps[step - 1];
        var siteUrl = _newsletter.SiteUrl(HttpContext);
        var (html, text) = NewsletterEmail.Render(new NewsletterContent(
            string.IsNullOrWhiteSpace(site.SiteName) ? "Newsletter" : site.SiteName, siteUrl, email.Subject, null, email.BodyMarkdown,
            Array.Empty<NewsletterPost>(), string.IsNullOrWhiteSpace(site.SiteLanguage) ? "en" : site.SiteLanguage));
        var result = await _email.SendMessageAsync(new EmailMessage(to, User.FindFirstValue(ClaimTypes.Name), "[Test] " + email.Subject,
            NewsletterEmail.Personalize(html, siteUrl, html: true), NewsletterEmail.Personalize(text, siteUrl, html: false)));

        await _audit.LogAsync(AuditActions.NewsletterTestSent, "Newsletter", null, $"Welcome email {step}");
        if (result.Sent) TempData["Success"] = $"Test of email {step} sent to {to}.";
        else TempData["Error"] = "The test could not be sent: " + result.Error;
        return RedirectToAction(nameof(Index));
    }
}
