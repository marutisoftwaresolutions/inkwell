using System.Security.Claims;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Blog.Web.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Two-factor sign-in (ADR-0021): the code step after a password, setting up an authenticator app,
/// recovery codes, and turning it off. Every outcome is audited; a wrong code counts against the
/// sign-in ticket (five tries) and is reported to the IP firewall like a wrong password.
/// </summary>
[Route("account/two-factor")]
public class TwoFactorController : Controller
{
    private readonly TwoFactorService _twoFactor;
    private readonly IUserRepository _users;
    private readonly UserSignInService _signIn;
    private readonly AuditService _audit;
    private readonly IpFirewallService _firewall;
    private readonly AuthService _auth;

    public TwoFactorController(TwoFactorService twoFactor, IUserRepository users, UserSignInService signIn,
        AuditService audit, IpFirewallService firewall, AuthService auth)
    {
        _twoFactor = twoFactor;
        _users = users;
        _signIn = signIn;
        _audit = audit;
        _firewall = firewall;
        _auth = auth;
    }

    private Guid CurrentUserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    private void DropTicket() => Response.Cookies.Delete(TwoFactorService.TicketCookie, new CookieOptions { Path = "/account/two-factor" });

    // ── The code step after a password ──────────────────────────────────────────

    [HttpGet("")]
    public IActionResult Verify()
    {
        var ticket = _twoFactor.ReadTicket(Request.Cookies[TwoFactorService.TicketCookie]);
        if (ticket is null || _twoFactor.TicketExhausted(ticket)) return Redirect("/account/login");
        return View();
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(string? code)
    {
        var ticket = _twoFactor.ReadTicket(Request.Cookies[TwoFactorService.TicketCookie]);
        if (ticket is null || _twoFactor.TicketExhausted(ticket))
        {
            DropTicket();
            TempData["Error"] = "That sign-in has expired. Enter your email and password again.";
            return Redirect("/account/login");
        }

        var user = await _users.GetByIdAsync(ticket.UserId);
        if (user is null || !user.IsActive) { DropTicket(); return Redirect("/account/login"); }

        var result = string.IsNullOrWhiteSpace(code) ? TwoFactorCheck.Invalid : await _twoFactor.VerifyAsync(user.Id, code);
        if (result is TwoFactorCheck.Ok or TwoFactorCheck.OkWithRecoveryCode)
        {
            DropTicket();
            await _signIn.SignInAsync(HttpContext, user);
            var name = user.DisplayName ?? user.Email;
            if (result == TwoFactorCheck.OkWithRecoveryCode)
            {
                var left = await _twoFactor.RecoveryCodesLeftAsync(user.Id);
                await _audit.LogAsync(AuditActions.TwoFactorRecoveryCodeUsed, "User", user.Id.ToString(), name,
                    newValues: $"{left} recovery codes left", userIdOverride: user.Id, userNameOverride: name);
                TempData["Warning"] = left <= 2
                    ? $"You signed in with a recovery code. Only {left} {(left == 1 ? "is" : "are")} left: create new ones under Two-factor sign-in."
                    : "You signed in with a recovery code. Each code works once.";
            }
            await _audit.LogAsync(AuditActions.AuthLoggedIn, "Auth", user.Id.ToString(), name, userIdOverride: user.Id, userNameOverride: name);
            return ticket.ReturnUrl is not null && Url.IsLocalUrl(ticket.ReturnUrl) ? Redirect(ticket.ReturnUrl) : Redirect("/admin/dashboard");
        }

        await _audit.LogAsync(AuditActions.TwoFactorFailed, "Auth", user.Id.ToString(), user.Email,
            userIdOverride: user.Id, userNameOverride: user.Email);
        await _firewall.RegisterFailedLoginAsync(HttpContext);
        if (!_twoFactor.RecordFailedAttempt(ticket))
        {
            DropTicket();
            TempData["Error"] = "Too many wrong codes. Enter your email and password to try again.";
            return Redirect("/account/login");
        }
        ViewBag.Error = result == TwoFactorCheck.SecretUnreadable
            ? "This server cannot check codes from your authenticator app (its encryption keys changed, for example after a restore). Enter one of your recovery codes instead."
            : "That code didn't work. Enter the 6-digit code your app shows now, or one of your recovery codes.";
        return View();
    }

    // ── Managing it (signed in) ─────────────────────────────────────────────────

    [Authorize]
    [HttpGet("/account/security")]
    public async Task<IActionResult> Security()
    {
        var user = await _users.GetByIdAsync(CurrentUserId);
        if (user is null) return Redirect("/account/login");
        var tf = await _users.GetTwoFactorAsync(user.Id);
        ViewBag.Required = TwoFactor.IsRequiredFor(user.Role, await _twoFactor.RequiredRolesAsync());
        ViewBag.CodesLeft = tf?.RecoveryHashes.Count ?? 0;
        return View(tf);
    }

    [Authorize]
    [HttpGet("setup")]
    public async Task<IActionResult> Setup(bool required = false)
    {
        var user = await _users.GetByIdAsync(CurrentUserId);
        if (user is null) return Redirect("/account/login");
        if ((await _users.GetTwoFactorAsync(user.Id))?.IsEnabled == true) return Redirect("/account/security");
        ViewBag.Required = required;
        return View(await _twoFactor.StartEnrolmentAsync(user.Email));
    }

    [Authorize]
    [HttpPost("setup")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Setup(string token, string? code, bool required = false)
    {
        var user = await _users.GetByIdAsync(CurrentUserId);
        if (user is null) return Redirect("/account/login");
        var codes = await _twoFactor.CompleteEnrolmentAsync(user.Id, token, code);
        if (codes is null)
        {
            ViewBag.Required = required;
            // Keep the same secret: the user has already added it to their app, and a new one would never match.
            var same = await _twoFactor.ResumeEnrolmentAsync(user.Email, token);
            ModelState.AddModelError("code", same is not null
                ? "That code didn't match. Check the app shows the entry for this site, then enter the code it shows now."
                : "This setup page expired. Remove the old entry from your app, scan the new code below and enter its code.");
            return View(same ?? await _twoFactor.StartEnrolmentAsync(user.Email));
        }
        await _audit.LogAsync(AuditActions.TwoFactorEnabled, "User", user.Id.ToString(), user.DisplayName ?? user.Email);
        ViewBag.First = true;
        return View("RecoveryCodes", codes);
    }

    [Authorize]
    [HttpPost("recovery-codes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecoveryCodes(string? code)
    {
        var user = await _users.GetByIdAsync(CurrentUserId);
        if (user is null) return Redirect("/account/login");
        var codes = await _twoFactor.RegenerateRecoveryCodesAsync(user.Id, code);
        if (codes is null)
        {
            TempData["FieldError"] = "regen-code|That code didn't work. Enter the 6-digit code your authenticator app shows now.";
            return Redirect("/account/security");
        }
        await _audit.LogAsync(AuditActions.TwoFactorRecoveryCodesRegenerated, "User", user.Id.ToString(), user.DisplayName ?? user.Email);
        return View("RecoveryCodes", codes);
    }

    [Authorize]
    [HttpPost("disable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable(string? password)
    {
        var user = await _users.GetByIdAsync(CurrentUserId);
        if (user is null) return Redirect("/account/login");
        if (TwoFactor.IsRequiredFor(user.Role, await _twoFactor.RequiredRolesAsync()))
        {
            TempData["Error"] = $"Two-factor sign-in is required for the {user.Role} role on this site, so it can't be turned off.";
            return Redirect("/account/security");
        }
        var (verified, _) = await _auth.ValidateLoginAsync(user.Email, password ?? "");
        if (verified is null)
        {
            await _firewall.RegisterFailedLoginAsync(HttpContext);
            TempData["FieldError"] = "disable-password|That password didn't match. Two-factor sign-in is still on.";
            return Redirect("/account/security");
        }
        await _twoFactor.DisableAsync(user.Id);
        await _audit.LogAsync(AuditActions.TwoFactorDisabled, "User", user.Id.ToString(), user.DisplayName ?? user.Email);
        TempData["Success"] = "Two-factor sign-in turned off.";
        return Redirect("/account/security");
    }

    /// <summary>An Admin turns off another user's two-factor (lost phone, no recovery codes). They set it up again at next sign-in if their role requires it.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("reset/{userId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(Guid userId)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null) return NotFound();
        await _twoFactor.DisableAsync(user.Id);
        await _audit.LogAsync(AuditActions.TwoFactorReset, "User", user.Id.ToString(), user.DisplayName ?? user.Email);
        TempData["Success"] = $"Two-factor sign-in turned off for {user.DisplayName ?? user.Email}.";
        return Redirect("/admin/users");
    }
}
