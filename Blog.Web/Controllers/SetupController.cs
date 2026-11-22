using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Blog.Web.Services.Backups;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

public class SetupController : Controller
{
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly AuthService _auth;
    private readonly BackupService _backups;
    private readonly AuditService _audit;
    private readonly IRoleRepository _roles;
    private readonly Blog.Infrastructure.Data.ApplicationDbSeeder _seeder;
    private readonly StarterSiteService _starters;

    public SetupController(ISettingRepository settings, IUserRepository users, AuthService auth,
        BackupService backups, AuditService audit, IRoleRepository roles, Blog.Infrastructure.Data.ApplicationDbSeeder seeder, StarterSiteService starters)
    {
        _settings = settings;
        _users = users;
        _auth = auth;
        _backups = backups;
        _audit = audit;
        _roles = roles;
        _seeder = seeder;
        _starters = starters;
    }

    private async Task<IActionResult> GuardAsync()
    {
        // Installed once either exists: the first admin can also be created at /account/register,
        // which writes no settings row, and the wizard must not stay open to create a second admin.
        if (await _settings.IsInstalledAsync() || await _users.AnyUsersExistAsync())
            return RedirectToAction("Index", "Dashboard", new { area = "" });
        return null!;
    }

    // Named outright: "/setup" on its own was read as a post address (BlogController's "{slug}" route wins over this
    // controller's conventional route), so the wizard a new install is sent to answered 404. Every other step keeps
    // its conventional address (/setup/step2 ...).
    [HttpGet("/setup")]
    [HttpGet("/setup/index")]
    public async Task<IActionResult> Index()
    {
        var guard = await GuardAsync();
        if (guard != null) return guard;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Step2()
    {
        var guard = await GuardAsync();
        if (guard != null) return guard;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Step3()
    {
        var guard = await GuardAsync();
        if (guard != null) return guard;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Review()
    {
        var guard = await GuardAsync();
        if (guard != null) return guard;
        var blogName = TempData.Peek("BlogName")?.ToString() ?? "(not set)";
        var email = TempData.Peek("AdminEmail")?.ToString() ?? "(not set)";
        ViewBag.BlogName = blogName;
        ViewBag.AdminEmail = email;
        ViewBag.Starter = StarterSites.Find(TempData.Peek("Starter")?.ToString())?.Name ?? "Empty site";
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStep2(string blogName, string tagline, string? starter = null)
    {
        // Errors are keyed by field name so Step2.cshtml can place them at the input.
        if (string.IsNullOrWhiteSpace(blogName))
        {
            ModelState.AddModelError("blogName", "Enter a name for your blog.");
            return View("Step2");
        }
        TempData["BlogName"] = blogName;
        TempData["Tagline"] = tagline;
        TempData["Starter"] = StarterSites.Normalize(starter);
        return RedirectToAction("Step3");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStep3(string email, string password, string confirmPassword)
    {
        // Errors are keyed by field name so Step3.cshtml can place them at the input.
        if (string.IsNullOrWhiteSpace(email))
            ModelState.AddModelError("email", "Enter the email address you will sign in with.");
        if (string.IsNullOrWhiteSpace(password))
            ModelState.AddModelError("password", "Enter a password of at least 8 characters.");
        if (!ModelState.IsValid)
            return View("Step3");
        if (password.Length < 8)
        {
            ModelState.AddModelError("password", "Password must be at least 8 characters.");
            return View("Step3");
        }
        if (password != confirmPassword)
        {
            ModelState.AddModelError("confirmPassword", "Passwords do not match. Type the same password in both boxes.");
            return View("Step3");
        }
        TempData["AdminEmail"] = email;
        TempData["AdminPassword"] = password;
        return RedirectToAction("Review");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Launch()
    {
        var blogName = TempData["BlogName"]?.ToString();
        var tagline = TempData["Tagline"]?.ToString() ?? "";
        var email = TempData["AdminEmail"]?.ToString();
        var password = TempData["AdminPassword"]?.ToString();
        var starterKey = TempData["Starter"]?.ToString();

        if (string.IsNullOrWhiteSpace(blogName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return RedirectToAction("Index");

        // Create admin user
        var user = new User
        {
            Email = email,
            Username = "admin",
            DisplayName = "Administrator",
            PasswordHash = _auth.HashPassword(password),
            Role = "Admin",
            IsActive = true
        };
        var userId = await _users.CreateAsync(user);

        // Save settings using the new repository method
        var settings = new UserSettings
        {
            UserId = userId,
            SiteName = blogName,
            SiteDescription = tagline,
            PostsPerPage = 20,
            CommentsEnabled = true,
            CommentsModeration = true
        };
        
        await _settings.SaveSettingsAsync(userId, settings);

        // The roles and their permissions, and the first admin's place in them. Without this a site
        // installed through the wizard had no roles at all, so Author and Contributor accounts created
        // later had no permissions. (A second settings row under Guid.Empty used to be written here;
        // the Settings foreign key rejects it, which ended the wizard on an error page.)
        await _seeder.SeedRolesAndPermissionsAsync();
        var adminRole = await _roles.GetByNameAsync("Admin");
        if (adminRole != null) await _roles.AssignRoleToUserAsync(userId, adminRole.Id);

        // The starter site the owner picked: pages, layout and preset. It only adds; a failure here must
        // not undo an install that is otherwise complete, so it is reported, not thrown.
        try
        {
            var outcome = await _starters.ApplyAsync(userId, starterKey);
            if (outcome.PagesCreated > 0) TempData["StarterPages"] = outcome.PagesCreated;
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<SetupController>>().LogError(ex, "Starter site could not be applied");
            TempData["StarterFailed"] = true;
        }

        return RedirectToAction("Complete");
    }

    public IActionResult Complete() => View();

    // ── Restore from a backup (ADR-0019) ────────────────────────────────────────
    // Only while the install is fresh, and only from a file already in the server's backups folder:
    // copying a file there needs server access, so an anonymous visitor can never supply the data.

    [HttpGet]
    public async Task<IActionResult> Restore()
    {
        var guard = await GuardAsync();
        if (guard != null) return guard;
        return View(_backups.List());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName("Restore")]
    public async Task<IActionResult> RestorePost(string? name)
    {
        var guard = await GuardAsync();
        if (guard != null) return guard;

        if (string.IsNullOrEmpty(name) || _backups.Find(name) is null)
        {
            ModelState.AddModelError("name", "Choose one of the backups listed below.");
            return View("Restore", _backups.List());
        }

        RestoreOutcome outcome;
        try
        {
            // Not the request's token: a closed browser tab must not abandon a restore half-way
            // (it is one transaction, so it would roll back, but the work would be wasted).
            outcome = await _backups.RestoreAsync(name, CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("name", ex.Message);
            return View("Restore", _backups.List());
        }
        catch (Exception ex)
        {
            // The database load is one transaction, so a failure leaves the install as empty as it was.
            HttpContext.RequestServices.GetRequiredService<ILogger<SetupController>>().LogError(ex, "Restore of {Name} failed.", name);
            ModelState.AddModelError("name", $"The restore failed and nothing was changed. {ex.Message}");
            return View("Restore", _backups.List());
        }

        var rows = outcome.Rows.Values.Sum();
        await _audit.LogAsync(AuditActions.BackupRestored, "Backup", name, name,
            newValues: $"{rows} rows, {outcome.MediaFiles} media files, from Inkwell {outcome.Manifest.AppVersion}",
            userNameOverride: "setup");
        return View("Restored", outcome);
    }
}
