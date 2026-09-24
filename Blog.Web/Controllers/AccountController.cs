using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Blog.Web.Controllers;

[Route("account")]
public class AccountController : Controller
{
    private readonly Blog.Core.Interfaces.IUserRepository _users;
    private readonly Blog.Core.Interfaces.IRoleRepository _roles;
    private readonly AuditService _audit;
    private readonly Blog.Web.Services.Security.IpFirewallService _firewall;
    private readonly Blog.Core.Interfaces.IPasswordResetRepository _resets;
    private readonly Blog.Core.Interfaces.IEmailService _email;
    private readonly Blog.Core.Interfaces.ISettingRepository _settings;
    private readonly Blog.Core.Services.AuthService _auth;
    private readonly Blog.Core.Interfaces.ITenantContext _tenant;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AccountController> _log;

    public AccountController(Blog.Core.Interfaces.IUserRepository users, Blog.Core.Interfaces.IRoleRepository roles,
        AuditService audit, Blog.Web.Services.Security.IpFirewallService firewall,
        Blog.Core.Interfaces.IPasswordResetRepository resets, Blog.Core.Interfaces.IEmailService email,
        Blog.Core.Interfaces.ISettingRepository settings, Blog.Core.Services.AuthService auth,
        Blog.Core.Interfaces.ITenantContext tenant, IConfiguration config, IWebHostEnvironment env,
        ILogger<AccountController> log)
    {
        _users = users;
        _roles = roles;
        _audit = audit;
        _firewall = firewall;
        _resets = resets;
        _email = email;
        _settings = settings;
        _auth = auth;
        _tenant = tenant;
        _config = config;
        _env = env;
        _log = log;
    }

    /// <summary>
    /// The owner whose settings blob holds the site name — the same key Admin → Settings writes under:
    /// the resolved tenant in cloud mode, else the first admin. Never <see cref="Guid.Empty"/>, which
    /// holds nothing on a self-hosted install.
    /// </summary>
    private async Task<Guid> ResolveSettingsOwnerAsync()
    {
        if (_tenant.IsCloudMode && _tenant.IsResolved) return _tenant.UserId;
        try { return (await _users.GetFirstAdminAsync())?.Id ?? Guid.Empty; }
        catch { return Guid.Empty; }
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    [HttpGet("login")]
    [HttpGet("")]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");

        // If no users exist, redirect to registration to claim the admin account
        if (!await _users.AnyUsersExistAsync())
        {
            return RedirectToAction("Register");
        }

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
    {
        ViewBag.Email = email;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ViewBag.Error = "Email and password are required.";
            return View();
        }

        if (!IsValidEmail(email))
        {
            ViewBag.Error = "Enter a valid email address.";
            return View();
        }

        var user = await _users.GetByEmailAsync(email);
        if (user == null || !VerifyPassword(password, user.PasswordHash))
        {
            await _audit.LogAsync(AuditActions.AuthLoginFailed, "Auth", null, email);
            // A rejected sign-in returns 200, so the firewall cannot see it from the status code —
            // report it explicitly so credential-stuffing runs block themselves.
            await _firewall.RegisterFailedLoginAsync(HttpContext);
            ViewBag.Error = "That email and password don't match.";
            return View();
        }

        if (!user.IsActive)
        {
            await _audit.LogAsync(AuditActions.AuthLoginFailed, "Auth", user.Id.ToString(), email);
            await _firewall.RegisterFailedLoginAsync(HttpContext);
            ViewBag.Error = "This account is disabled. Ask an administrator to re-enable it.";
            return View();
        }

        await SignInUser(user);
        await _audit.LogAsync(AuditActions.AuthLoggedIn, "Auth",
            user.Id.ToString(), user.DisplayName ?? user.Email,
            userIdOverride: user.Id, userNameOverride: user.DisplayName ?? user.Email);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Dashboard");
    }

    // ── Register (First Run) ──────────────────────────────────────────────────

    [HttpGet("register")]
    public async Task<IActionResult> Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");

        return View();
    }

    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(string email, string password, string confirmPassword, string name)
    {
        ViewBag.Email = email;
        ViewBag.Name = name;
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(name))
        {
            ViewBag.Error = "All fields are required.";
            return View();
        }

        if (!IsValidEmail(email))
        {
            ViewBag.Error = "Enter a valid email address.";
            return View();
        }

        if (password != confirmPassword)
        {
            ViewBag.Error = "Passwords do not match.";
            return View();
        }

        if (!IsStrongPassword(password))
        {
            ViewBag.Error = "Password must be at least 8 characters long and contain at least one uppercase letter, one lowercase letter, and one number.";
            return View();
        }

        bool isFirstUser = !await _users.AnyUsersExistAsync();

        // ── Proactive duplicate check ────────────────────────────────────────
        var existing = await _users.GetByEmailAsync(email);
        if (existing != null)
        {
            ViewBag.Error = "An account with that email address already exists. Try logging in instead.";
            return View();
        }
        // ────────────────────────────────────────────────────────────────────

        var newUser = new User
        {
            Email = email,
            Username = email.Split('@')[0], // Simple default
            DisplayName = name,
            Role = isFirstUser ? "Admin" : "Member",
            PasswordHash = HashPassword(password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            var id = await _users.CreateAsync(newUser);
            newUser.Id = id;

            // Auto-seed RBAC roles & permissions if they don't exist yet
            var allRoles = await _roles.GetAllRolesAsync();
            if (allRoles.Count == 0)
            {
                // Seed roles and permissions on first registration
                using var scope = HttpContext.RequestServices.CreateScope();
                var seeder = scope.ServiceProvider.GetRequiredService<Blog.Infrastructure.Data.ApplicationDbSeeder>();
                await seeder.SeedRolesAndPermissionsAsync();
            }

            // Assign RBAC role in RolesUsers table
            var roleName = isFirstUser ? "Admin" : "Author";
            var dbRole = await _roles.GetByNameAsync(roleName);
            if (dbRole != null)
            {
                await _roles.AssignRoleToUserAsync(id, dbRole.Id);
            }
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
            when (ex.Number == 2601 || ex.Number == 2627) // unique index / primary key violation
        {
            // Determine which field caused the conflict for a targeted message
            var msg = ex.Message.Contains("username", StringComparison.OrdinalIgnoreCase)
                ? "That username is already taken. Please choose a different one."
                : "An account with that email address already exists. Try logging in instead.";

            ViewBag.Error = msg;
            return View();
        }
        catch (Exception)
        {
            ViewBag.Error = "Something went wrong while creating your account. Please try again.";
            return View();
        }

        await SignInUser(newUser);

        TempData["Success"] = "Welcome to Inkwell.";
        return RedirectToAction("Index", "Dashboard");
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync(AuditActions.AuthLoggedOut, "Auth");
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    // ── Self-service password reset ───────────────────────────────────────────
    //
    // The flow never reveals whether an address has an account: known and unknown addresses get
    // the same page. Only the SHA-256 hash of a token is stored; the raw token lives in the email
    // link for 30 minutes and is single-use. Requests per account are capped, and a page that
    // cannot send mail says so instead of pretending.

    [HttpGet("forgot-password")]
    public IActionResult ForgotPassword()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");
        if (!_email.IsConfigured)
            ViewBag.EmailUnavailable = true;
        return View();
    }

    [HttpPost("forgot-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(string email)
    {
        if (!_email.IsConfigured)
        {
            ViewBag.EmailUnavailable = true;
            return View();
        }

        email = (email ?? string.Empty).Trim();
        if (email.Length > 0)
        {
            try
            {
                var user = await _users.GetByEmailAsync(email);
                if (user != null && user.IsActive)
                {
                    var recent = await _resets.CountRecentForUserAsync(user.Id, Blog.Core.Services.PasswordResetTokens.RequestWindow);
                    if (Blog.Core.Services.PasswordResetTokens.CanRequest(recent))
                    {
                        // A new link supersedes any outstanding one.
                        await _resets.InvalidateForUserAsync(user.Id);

                        var raw = Blog.Core.Services.PasswordResetTokens.Generate();
                        await _resets.CreateAsync(new PasswordResetToken
                        {
                            UserId = user.Id,
                            TokenHash = Blog.Core.Services.PasswordResetTokens.Hash(raw),
                            ExpiresAt = DateTime.UtcNow + Blog.Core.Services.PasswordResetTokens.Lifetime,
                            RequestIp = _firewall.ResolveClientIp(HttpContext)
                        });

                        // Behind a TLS-terminating proxy Request.Scheme is http and Request.Host may be the
                        // internal name, so the configured canonical host wins outside Development — the same
                        // condition the canonical-host redirect in Program.cs applies.
                        var canonicalHost = _env.IsDevelopment() ? null : _config["CanonicalHost"];
                        var linkBase = Blog.Core.Services.PasswordResetTokens.ResolveLinkBase(canonicalHost, Request.Scheme, Request.Host.Value ?? string.Empty);
                        var link = $"{linkBase}/account/reset-password?token={Uri.EscapeDataString(raw)}";
                        var siteName = (await _settings.GetSettingsAsync(await ResolveSettingsOwnerAsync())).SiteName;
                        if (string.IsNullOrWhiteSpace(siteName))
                            siteName = Uri.TryCreate(linkBase, UriKind.Absolute, out var origin) ? origin.Host : Request.Host.Value;
                        Func<string?, string?> enc = s => System.Net.WebUtility.HtmlEncode(s);
                        var body =
                            $"<p>Someone asked to reset the password for <b>{enc(email)}</b> on {enc(siteName)}.</p>" +
                            $"<p><a href=\"{enc(link)}\">Choose a new password</a></p>" +
                            $"<p style='color:#666;font-size:12px'>The link works once and expires in {Blog.Core.Services.PasswordResetTokens.Lifetime.TotalMinutes:0} minutes. " +
                            "If you did not ask for this, ignore this email — your password has not changed.</p>";
                        await _email.SendAsync(email, user.DisplayName ?? email, $"Reset your {siteName} password", body);

                        // Audited only for a real account — logging unknown addresses would leak them into the trail.
                        await _audit.LogAsync(AuditActions.AuthPasswordResetRequested, "Auth", user.Id.ToString(), email);
                    }
                    else
                    {
                        _log.LogInformation("Password reset for {Email} suppressed: request cap reached.", email);
                    }
                }
            }
            catch (Exception ex)
            {
                // The reader still sees the neutral page; the operator sees the log.
                _log.LogWarning(ex, "Password reset request failed for {Email}.", email);
            }
        }

        ViewBag.Sent = true;
        return View();
    }

    [HttpGet("reset-password")]
    public async Task<IActionResult> ResetPassword(string? token)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");

        var row = string.IsNullOrWhiteSpace(token) ? null
            : await _resets.GetByHashAsync(Blog.Core.Services.PasswordResetTokens.Hash(token));
        if (row == null || !Blog.Core.Services.PasswordResetTokens.IsUsable(row.UsedAt, row.ExpiresAt, DateTime.UtcNow))
        {
            ViewBag.Invalid = true;
            return View();
        }

        ViewBag.Token = token;
        return View();
    }

    [HttpPost("reset-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string? token, string? password, string? confirmPassword)
    {
        var row = string.IsNullOrWhiteSpace(token) ? null
            : await _resets.GetByHashAsync(Blog.Core.Services.PasswordResetTokens.Hash(token));
        if (row == null || !Blog.Core.Services.PasswordResetTokens.IsUsable(row.UsedAt, row.ExpiresAt, DateTime.UtcNow))
        {
            ViewBag.Invalid = true;
            return View();
        }

        var error = Blog.Core.Services.PasswordResetTokens.ValidateNewPassword(password, confirmPassword);
        if (error != null)
        {
            ViewBag.Error = error;
            ViewBag.Token = token;
            return View();
        }

        var user = await _users.GetByIdAsync(row.UserId);
        if (user == null || !user.IsActive)
        {
            ViewBag.Invalid = true;
            return View();
        }

        user.PasswordHash = _auth.HashPassword(password!);
        user.UpdatedAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);
        await _resets.MarkUsedAsync(row.Id);
        await _resets.InvalidateForUserAsync(user.Id);
        await _audit.LogAsync(AuditActions.AuthPasswordReset, "Auth", user.Id.ToString(), user.Email);

        TempData["Success"] = "Your password has been changed. Sign in with the new one.";
        return RedirectToAction("Login");
    }

    // ── Access Denied ────────────────────────────────────────────────────────

    [HttpGet("accessdenied")]
    public IActionResult AccessDenied()
    {
        TempData["Error"] = "Access Denied: You do not have permission to view that page.";
        return RedirectToAction("Index", "Dashboard");
    }

    // ── Profile ───────────────────────────────────────────────────────────────

    [HttpGet("profile")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Profile()
    {
        var idString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idString == null || !Guid.TryParse(idString, out var id)) return RedirectToAction("Login");

        var user = await _users.GetByIdAsync(id);
        if (user == null) return RedirectToAction("Login");

        return View(user);
    }

    [HttpPost("profile")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(string displayName, string email, string? bio, string? avatarUrl, IFormFile? avatarFile, bool removeAvatar, string? website, string? newPassword, string? credentials, string? specialty, string? licenseNumber, string? slug)
    {
        var idString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idString == null || !Guid.TryParse(idString, out var id)) return RedirectToAction("Login");

        var user = await _users.GetByIdAsync(id);
        if (user == null) return RedirectToAction("Login");

        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(email))
        {
            ViewBag.Error = "Name and Email are required.";
            return View(user);
        }

        if (!IsValidEmail(email))
        {
            ViewBag.Error = "Enter a valid email address.";
            return View(user);
        }

        user.DisplayName = displayName;
        user.Email = email;
        user.Bio = bio;
        user.Website = website;
        user.Credentials = credentials;
        user.Specialty = specialty;
        user.LicenseNumber = licenseNumber;
        user.UpdatedAt = DateTime.UtcNow;

        // Author-page slug (/author/{slug}) — use the provided value, else keep existing, else derive
        // from the display name. Always normalized and made unique across users.
        var slugSource = !string.IsNullOrWhiteSpace(slug) ? slug
            : !string.IsNullOrWhiteSpace(user.Slug) ? user.Slug
            : displayName;
        user.Slug = await _users.GenerateUniqueSlugAsync(slugSource, user.Id);

        if (removeAvatar)
        {
            RemoveOldAvatarFile(user.AvatarUrl);
            user.AvatarUrl = null;
        }
        else if (avatarFile != null && avatarFile.Length > 0)
        {
            RemoveOldAvatarFile(user.AvatarUrl);
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(avatarFile.FileName);
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await avatarFile.CopyToAsync(fileStream);
            }

            user.AvatarUrl = "/uploads/avatars/" + uniqueFileName;
        }
        else if (avatarUrl != null)
        {
            RemoveOldAvatarFile(user.AvatarUrl);
            user.AvatarUrl = avatarUrl;
        }

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            if (!IsStrongPassword(newPassword))
            {
                ViewBag.Error = "New password must be at least 8 characters long and contain at least one uppercase letter, one lowercase letter, and one number.";
                return View(user);
            }
            user.PasswordHash = HashPassword(newPassword);
        }

        await _users.UpdateAsync(user);
        
        // Refresh cookie claims if name/email changed
        await SignInUser(user);

        TempData["Success"] = "Profile updated successfully.";
        return RedirectToAction("Profile");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RemoveOldAvatarFile(string? currentAvatarUrl)
    {
        if (string.IsNullOrEmpty(currentAvatarUrl) || !currentAvatarUrl.StartsWith("/uploads/avatars/"))
            return;

        var fileName = currentAvatarUrl.Substring("/uploads/avatars/".Length);
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars", fileName);

        if (System.IO.File.Exists(filePath))
        {
            System.IO.File.Delete(filePath);
        }
    }

    private async Task SignInUser(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName ?? user.Email),
            new(ClaimTypes.Role, user.Role ?? "Author")
        };

        // Load RBAC permissions from the database and add as claims
        var permissions = await _roles.GetPermissionNamesForUserAsync(user.Id);
        foreach (var permission in permissions)
        {
            claims.Add(new Claim("Permission", permission));
        }
        
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });
    }

    private static string HashPassword(string password)
    {
        // PBKDF2 with SHA256
        using var rng = RandomNumberGenerator.Create();
        var salt = new byte[16];
        rng.GetBytes(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);
        
        return $"pbkdf2${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string savedHash)
    {
        try
        {
            var parts = savedHash.Split('$');
            if (parts.Length != 3 || parts[0] != "pbkdf2") return false;
            var salt = Convert.FromBase64String(parts[1]);
            var stored = Convert.FromBase64String(parts[2]);
            var computed = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(computed, stored);
        }
        catch { return false; }
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[a-zA-Z]{2,}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsStrongPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8) return false;
        
        bool hasUpper = false, hasLower = false, hasDigit = false;
        foreach (char c in password)
        {
            if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsLower(c)) hasLower = true;
            else if (char.IsDigit(c)) hasDigit = true;
        }
        
        return hasUpper && hasLower && hasDigit;
    }
}
