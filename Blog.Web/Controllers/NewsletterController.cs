using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

[Route("newsletter")]
public class NewsletterController : Controller
{
    private readonly IMemberRepository _members;
    private readonly IEmailService _email;
    private readonly ISettingRepository _settings;
    private readonly ITenantContext _tenantContext;
    private readonly IUserRepository _users;
    private readonly ReCaptchaService _recaptcha;
    private readonly IAntiforgery _antiforgery;

    public NewsletterController(IMemberRepository members, IEmailService email,
        ISettingRepository settings, ITenantContext tenantContext, IUserRepository users,
        ReCaptchaService recaptcha, IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
        _members = members;
        _email = email;
        _settings = settings;
        _tenantContext = tenantContext;
        _users = users;
        _recaptcha = recaptcha;
    }

    /// <summary>
    /// The sign-up form's anti-forgery token, fetched by its script when the reader presses Subscribe.
    /// The form is rendered without one, so the home page and posts carry no cookie and no
    /// <c>no-store</c>, and a CDN can cache them. This response is never cached (the antiforgery
    /// service marks it <c>no-store</c>).
    /// </summary>
    [HttpGet("token")]
    public IActionResult Token()
    {
        Response.Headers["X-Robots-Tag"] = "noindex";
        return Json(new { token = _antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    // The anti-forgery check is made here rather than by [ValidateAntiForgeryToken], so that a form
    // posted without a token (no JavaScript, or a page served from a cache) gets a page to finish on
    // instead of a 400. Without a valid token nothing is written.
    [HttpPost("subscribe")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Subscribe(string email, string? name)
    {
        email = email?.Trim().ToLowerInvariant() ?? string.Empty;
        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (!await _antiforgery.IsRequestValidAsync(HttpContext))
        {
            if (isAjax)
                return BadRequest(new { success = false, message = "The sign-up form has expired. Reload the page and try again." });
            return View("Subscribe", new NewsletterSubscribeViewModel(email, name?.Trim(), null, false));
        }

        IActionResult Fail(string msg)
        {
            if (isAjax) return Json(new { success = false, message = msg });
            return View("Subscribe", new NewsletterSubscribeViewModel(email, name?.Trim(), msg, false));
        }

        IActionResult Ok(string msg)
        {
            if (isAjax) return Json(new { success = true, message = msg });
            return View("Subscribe", new NewsletterSubscribeViewModel(email, name?.Trim(), msg, true));
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return Fail("Please enter a valid email address.");

        var captchaToken = Request.Form["g-recaptcha-response"].ToString();
        if (!await _recaptcha.ValidateAsync(captchaToken))
            return Fail("reCAPTCHA verification failed. Please try again.");

        var existing = await _members.GetByEmailAsync(email);
        if (existing != null)
        {
            var dupMsg = existing.Status == "confirmed"
                ? "You're already subscribed!"
                : "Check your inbox — a confirmation email was already sent.";
            return Ok(dupMsg);
        }

        var member = new Member
        {
            Email = email,
            Name = name?.Trim(),
            Status = "pending",
            Subscribed = false
        };
        await _members.CreateAsync(member);

        var ownerId = await GetOwnerIdAsync();
        var siteSettings = await _settings.GetSettingsAsync(ownerId);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var confirmUrl = $"{baseUrl}/newsletter/confirm/{member.ConfirmToken}";

        var siteName = siteSettings.SiteName ?? "Blog";
        var html = BuildConfirmEmail(siteName, confirmUrl, name);
        await _email.SendAsync(email, name ?? email, $"Confirm your subscription to {siteName}", html);

        return Ok("Almost there! Check your inbox to confirm your subscription.");
    }

    [HttpGet("confirm/{token}")]
    public async Task<IActionResult> Confirm(string token)
    {
        var member = await _members.GetByConfirmTokenAsync(token);
        if (member == null)
            return View("NewsletterResult", new NewsletterResultViewModel("Invalid or expired confirmation link.", false));

        if (member.Status == "confirmed")
            return View("NewsletterResult", new NewsletterResultViewModel("You're already confirmed — welcome!", true));

        await _members.ConfirmAsync(member.Id);

        var ownerId = await GetOwnerIdAsync();
        var siteSettings = await _settings.GetSettingsAsync(ownerId);
        var siteName = siteSettings.SiteName ?? "Blog";
        var unsubUrl = $"{Request.Scheme}://{Request.Host}/newsletter/unsubscribe/{member.UnsubscribeToken}";
        var html = BuildWelcomeEmail(siteName, member.Name ?? member.Email, unsubUrl);
        await _email.SendAsync(member.Email, member.Name ?? member.Email, $"Welcome to {siteName}!", html);

        return View("NewsletterResult", new NewsletterResultViewModel($"You're confirmed! Welcome to {siteName}.", true));
    }

    /// <summary>
    /// The link in every newsletter and welcome email. It only asks: mail systems open links to scan them, and a
    /// GET that unsubscribed would remove people who never clicked. The button posts to the action below.
    /// </summary>
    [HttpGet("unsubscribe/{token}")]
    public async Task<IActionResult> Unsubscribe(string token)
    {
        var member = await _members.GetByUnsubscribeTokenAsync(token);
        if (member == null)
            return View("NewsletterResult", new NewsletterResultViewModel("This unsubscribe link is not valid. It may already have been used to leave and rejoin.", false));
        if (member.Status == "unsubscribed")
            return View("NewsletterResult", new NewsletterResultViewModel("You are already unsubscribed. You will not get any more emails.", true));
        Response.Headers["X-Robots-Tag"] = "noindex";
        return View("Unsubscribe", new NewsletterUnsubscribeViewModel(token, member.Email));
    }

    /// <summary>
    /// Unsubscribes. Also the RFC 8058 one-click target of the List-Unsubscribe-Post header, which mail providers
    /// call without cookies, so there is no anti-forgery token: the unguessable token in the address is the proof.
    /// </summary>
    [HttpPost("unsubscribe/{token}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UnsubscribeConfirmed(string token)
    {
        var member = await _members.GetByUnsubscribeTokenAsync(token);
        var oneClick = Request.HasFormContentType && Request.Form["List-Unsubscribe"] == "One-Click";
        if (member == null)
            return oneClick ? NotFound() : View("NewsletterResult", new NewsletterResultViewModel("This unsubscribe link is not valid.", false));
        if (member.Status != "unsubscribed") await _members.UnsubscribeAsync(member.Id);
        if (oneClick) return Content("Unsubscribed.", "text/plain");
        return View("NewsletterResult", new NewsletterResultViewModel("You are unsubscribed. You will not get any more emails.", true));
    }

    private async Task<Guid> GetOwnerIdAsync()
    {
        if (_tenantContext.IsCloudMode && _tenantContext.IsResolved)
            return _tenantContext.UserId;
        var admin = await _users.GetFirstAdminAsync();
        return admin?.Id ?? Guid.Empty;
    }

    private static string BuildConfirmEmail(string siteName, string confirmUrl, string? name)
    {
        var greeting = string.IsNullOrEmpty(name) ? "Hi there" : $"Hi {System.Net.WebUtility.HtmlEncode(name)}";   // the name is what the reader typed
        return $"""
            <!DOCTYPE html><html><body style="font-family:sans-serif;max-width:600px;margin:40px auto;color:#111;line-height:1.6">
            <h2 style="margin-bottom:8px">{System.Net.WebUtility.HtmlEncode(siteName)}</h2>
            <p>{greeting},</p>
            <p>Thanks for signing up! Click the button below to confirm your subscription.</p>
            <p style="margin:32px 0">
              <a href="{confirmUrl}" style="background:#111;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:600">Confirm Subscription</a>
            </p>
            <p style="font-size:13px;color:#666">Or paste this link into your browser:<br><a href="{confirmUrl}">{confirmUrl}</a></p>
            <p style="font-size:12px;color:#6b6b6b;margin-top:40px">If you didn't sign up, you can safely ignore this email.</p>
            </body></html>
            """;
    }

    private static string BuildWelcomeEmail(string siteName, string name, string unsubUrl)
    {
        return $"""
            <!DOCTYPE html><html><body style="font-family:sans-serif;max-width:600px;margin:40px auto;color:#111;line-height:1.6">
            <h2 style="margin-bottom:8px">Welcome to {System.Net.WebUtility.HtmlEncode(siteName)}!</h2>
            <p>Hi {System.Net.WebUtility.HtmlEncode(name)},</p>
            <p>You're now subscribed. You'll get our newsletter whenever we send one.</p>
            <p style="font-size:12px;color:#6b6b6b;margin-top:40px">
              Don't want to hear from us? <a href="{unsubUrl}" style="color:#6b6b6b">Unsubscribe</a>
            </p>
            </body></html>
            """;
    }
}

public record NewsletterResultViewModel(string Message, bool Success);

/// <summary>The sign-up page for a form posted without JavaScript: a confirm step, an error to fix, or the result.</summary>
/// <summary>The unsubscribe confirmation page.</summary>
public record NewsletterUnsubscribeViewModel(string Token, string Email);

public record NewsletterSubscribeViewModel(string Email, string? Name, string? Message, bool Success);
