using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
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

    public NewsletterController(IMemberRepository members, IEmailService email,
        ISettingRepository settings, ITenantContext tenantContext, IUserRepository users,
        ReCaptchaService recaptcha)
    {
        _members = members;
        _email = email;
        _settings = settings;
        _tenantContext = tenantContext;
        _users = users;
        _recaptcha = recaptcha;
    }

    [HttpPost("subscribe")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe(string email, string? name)
    {
        email = email?.Trim().ToLowerInvariant() ?? string.Empty;
        var referer = Request.Headers.Referer.ToString() is { Length: > 0 } rf ? rf : "/";
        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        IActionResult Fail(string msg)
        {
            if (isAjax) return Json(new { success = false, message = msg });
            TempData["NewsletterError"] = msg;
            return Redirect(referer);
        }

        IActionResult Ok(string msg)
        {
            if (isAjax) return Json(new { success = true, message = msg });
            TempData["NewsletterMessage"] = msg;
            return Redirect(referer);
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

    [HttpGet("unsubscribe/{token}")]
    public async Task<IActionResult> Unsubscribe(string token)
    {
        var member = await _members.GetByUnsubscribeTokenAsync(token);
        if (member == null)
            return View("NewsletterResult", new NewsletterResultViewModel("Invalid unsubscribe link.", false));

        await _members.UnsubscribeAsync(member.Id);
        return View("NewsletterResult", new NewsletterResultViewModel("You've been unsubscribed. Sorry to see you go!", true));
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
        var greeting = string.IsNullOrEmpty(name) ? "Hi there" : $"Hi {name}";
        return $"""
            <!DOCTYPE html><html><body style="font-family:sans-serif;max-width:600px;margin:40px auto;color:#111;line-height:1.6">
            <h2 style="margin-bottom:8px">{System.Net.WebUtility.HtmlEncode(siteName)}</h2>
            <p>{greeting},</p>
            <p>Thanks for signing up! Click the button below to confirm your subscription.</p>
            <p style="margin:32px 0">
              <a href="{confirmUrl}" style="background:#111;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:600">Confirm Subscription</a>
            </p>
            <p style="font-size:13px;color:#666">Or paste this link into your browser:<br><a href="{confirmUrl}">{confirmUrl}</a></p>
            <p style="font-size:12px;color:#999;margin-top:40px">If you didn't sign up, you can safely ignore this email.</p>
            </body></html>
            """;
    }

    private static string BuildWelcomeEmail(string siteName, string name, string unsubUrl)
    {
        return $"""
            <!DOCTYPE html><html><body style="font-family:sans-serif;max-width:600px;margin:40px auto;color:#111;line-height:1.6">
            <h2 style="margin-bottom:8px">Welcome to {System.Net.WebUtility.HtmlEncode(siteName)}!</h2>
            <p>Hi {System.Net.WebUtility.HtmlEncode(name)},</p>
            <p>You're now subscribed. We'll notify you whenever new articles are published.</p>
            <p style="font-size:12px;color:#999;margin-top:40px">
              Don't want to hear from us? <a href="{unsubUrl}" style="color:#999">Unsubscribe</a>
            </p>
            </body></html>
            """;
    }
}

public record NewsletterResultViewModel(string Message, bool Success);
