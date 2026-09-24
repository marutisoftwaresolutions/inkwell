using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

[Authorize(Roles = "Admin,Author")]
[Route("admin/users")]
public class UsersController : Controller
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly AuditService _audit;

    public UsersController(IUserRepository users, IRoleRepository roles, AuditService audit)
    {
        _users = users;
        _roles = roles;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var currentUserIdStr = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        Guid? currentUserId = Guid.TryParse(currentUserIdStr, out var id) ? id : null;
        var isAdmin = User.IsInRole("Admin");

        var allUsers = await _users.GetAllUsersAsync();
        var users = isAdmin ? allUsers : allUsers.Where(u => u.CreatedByUserId == currentUserId).ToList();

        var allRoles = await _roles.GetAllRolesAsync();
        ViewBag.AllRoles = allRoles;

        var userRoles = new Dictionary<Guid, string>();
        foreach (var user in users)
        {
            var role = await _roles.GetRoleForUserAsync(user.Id);
            userRoles[user.Id] = role?.Name ?? user.Role ?? "None";
        }
        ViewBag.UserRoles = userRoles;

        ViewData["ListSearchPlaceholder"] = "Search users by name, email or role";
        return View(Blog.Web.Models.ListPaging.Apply(this, users, q, page, u => new[] { u.DisplayName, u.Username, u.Email, u.Role }));
    }

    [HttpPost("invite")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(string displayName, string email, string? password, string roleName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(email))
        {
            TempData["Error"] = "Enter a display name and an email address.";
            return RedirectToAction("Index");
        }

        // A blank password means "generate one for me": the admin never has to invent a
        // credential for someone else. Whatever is used is shown once on the next page.
        if (string.IsNullOrWhiteSpace(password))
        {
            password = PasswordGenerator.Generate();
        }
        else if (password.Length < PasswordGenerator.MinimumLength)
        {
            TempData["Error"] = $"Password must be at least {PasswordGenerator.MinimumLength} characters, or leave it blank to generate one.";
            return RedirectToAction("Index");
        }

        var username = email.Split('@')[0].ToLower();
        var slug = username;

        var currentUserIdStr = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        Guid? createdByUserId = null;
        if (Guid.TryParse(currentUserIdStr, out var currentUserId))
        {
            createdByUserId = currentUserId;
        }

        var newUser = new User
        {
            Email = email,
            Username = username,
            DisplayName = displayName,
            Slug = slug,
            PasswordHash = HashPassword(password),
            Role = roleName,
            CreatedByUserId = createdByUserId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            var id = await _users.CreateAsync(newUser);

            var dbRole = await _roles.GetByNameAsync(roleName);
            if (dbRole != null)
            {
                await _roles.AssignRoleToUserAsync(id, dbRole.Id);
            }

            await _audit.LogAsync(AuditActions.UserInvited, "User", null, $"{displayName} ({email})",
                newValues: $"{{\"role\":\"{roleName}\"}}");
            TempData["Success"] = $"{displayName} has been added as {roleName}. They sign in with {email} and the password shown below.";
            // Shown exactly once by the Users view; TempData is consumed on that render, so a
            // refresh does not repeat it. Nothing is emailed.
            TempData["InvitePassword"] = password;
            TempData["InviteEmail"] = email;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
            when (ex.Number == 2601 || ex.Number == 2627)
        {
            TempData["Error"] = ex.Message.Contains("username", StringComparison.OrdinalIgnoreCase)
                ? $"Username '{username}' is already taken."
                : $"An account with email '{email}' already exists.";
        }
        catch (Exception)
        {
            TempData["Error"] = "Something went wrong. Please try again.";
        }

        return RedirectToAction("Index");
    }

    [HttpPost("update-role")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRole(Guid userId, string roleName)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user == null) return NotFound();

        var role = await _roles.GetByNameAsync(roleName);
        if (role == null)
        {
            TempData["Error"] = $"Role '{roleName}' not found. Please seed RBAC first.";
            return RedirectToAction("Index");
        }

        await _roles.AssignRoleToUserAsync(userId, role.Id);

        var oldRole = user.Role;
        user.Role = roleName;
        await _users.UpdateAsync(user);
        await _audit.LogAsync(AuditActions.UserRoleChanged, "User", userId.ToString(),
            user.DisplayName ?? user.Email,
            oldValues: $"{{\"role\":\"{oldRole}\"}}",
            newValues: $"{{\"role\":\"{roleName}\"}}");

        TempData["Success"] = $"Role for {user.DisplayName ?? user.Email} updated to {roleName}.";
        TempData["Warning"] = "If the user is currently logged in, they must log out and back in for new permissions to apply.";
        return RedirectToAction("Index");
    }

    [HttpPost("toggle-active")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(Guid userId)
    {
        // Self-guard: disabling your own account signs you out with no way back in.
        var currentUserIdStr = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (Guid.TryParse(currentUserIdStr, out var currentUserId) && currentUserId == userId)
        {
            TempData["Error"] = "You cannot disable your own account.";
            return RedirectToAction("Index");
        }

        var user = await _users.GetByIdAsync(userId);
        if (user == null) return NotFound();

        user.IsActive = !user.IsActive;
        await _users.UpdateAsync(user);
        await _audit.LogAsync(AuditActions.UserToggled, "User", userId.ToString(),
            user.DisplayName ?? user.Email,
            newValues: $"{{\"isActive\":{user.IsActive.ToString().ToLower()}}}");

        TempData["Success"] = $"{user.DisplayName ?? user.Email} is now {(user.IsActive ? "active" : "disabled")}.";
        return RedirectToAction("Index");
    }

    [HttpPost("delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid userId)
    {
        var targetUser = await _users.GetByIdAsync(userId);
        if (targetUser == null) return NotFound();

        var currentUserIdStr = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        Guid? currentUserId = Guid.TryParse(currentUserIdStr, out var id) ? id : null;
        var isAdmin = User.IsInRole("Admin");

        // Authors cannot delete Admins or Editors
        if (targetUser.Role == "Admin" || targetUser.Role == "Editor")
        {
            if (!isAdmin)
            {
                TempData["Error"] = "You do not have permission to delete an Admin or Editor.";
                return RedirectToAction("Index");
            }
        }

        // Only Admin or the original creator can delete
        if (!isAdmin && targetUser.CreatedByUserId != currentUserId)
        {
            TempData["Error"] = "You can only delete users you have invited.";
            return RedirectToAction("Index");
        }

        if (currentUserId == userId)
        {
            TempData["Error"] = "You cannot delete yourself.";
            return RedirectToAction("Index");
        }

        await _users.DeleteAsync(userId);
        await _audit.LogAsync(AuditActions.UserDeleted, "User", userId.ToString(),
            targetUser.DisplayName ?? targetUser.Email);
        TempData["Success"] = $"{targetUser.DisplayName ?? targetUser.Email} has been deleted.";
        return RedirectToAction("Index");
    }

    private static string HashPassword(string password)
    {
        using var rng = RandomNumberGenerator.Create();
        var salt = new byte[16];
        rng.GetBytes(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);
        
        return $"pbkdf2${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
}
