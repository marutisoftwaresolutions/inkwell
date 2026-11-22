using Blog.Core.Domain;
using Blog.Core.Interfaces;

namespace Blog.Infrastructure.Data;

/// <summary>
/// Makes sure the default roles and permissions exist. Runs at every start, from the setup wizard and
/// from Admin → Users. It only adds what is missing, row by row, so a first run that stopped halfway
/// (roles created, permissions not) is finished by the next one. It never takes anything away and
/// never re-grants a permission a site removed from a role: a role gets its default permissions only
/// when the role was just created, when it has no permissions at all, or when the permission itself
/// was just created. It seeds nothing else: an install starts with no posts, pages, categories or
/// tags of anyone's choosing but the owner's.
/// </summary>
public class ApplicationDbSeeder
{
    private readonly IRoleRepository _roleRepo;

    public ApplicationDbSeeder(IRoleRepository roleRepo) => _roleRepo = roleRepo;

    /// <summary>The default roles, in the order they are created.</summary>
    public static readonly IReadOnlyList<(string Name, string Description)> DefaultRoles =
    [
        ("Admin", "Full access to all features"),
        ("Editor", "Manage all content but not site settings"),
        ("Author", "Manage own posts and media"),
        ("Contributor", "Create draft posts only"),
    ];

    /// <summary>The default permissions: name, action, object.</summary>
    public static readonly IReadOnlyList<(string Name, string Action, string Object)> DefaultPermissions =
    [
        ("posts.edit", "edit", "post"),
        ("posts.publish", "publish", "post"),
        ("posts.delete", "delete", "post"),
        ("pages.manage", "manage", "page"),
        ("comments.manage", "manage", "comment"),
        ("categories.manage", "manage", "category"),
        ("tags.manage", "manage", "tag"),
        ("media.manage", "manage", "media"),
        ("settings.manage", "manage", "setting"),
        ("themes.manage", "manage", "theme"),
    ];

    /// <summary>Which default permissions a default role holds.</summary>
    public static bool RoleHolds(string role, string permission) => role switch
    {
        "Admin" => true,
        "Editor" => permission is not ("settings.manage" or "themes.manage"),
        "Author" => permission is "posts.edit" or "posts.publish" or "posts.delete" or "media.manage" or "comments.manage" or "categories.manage" or "tags.manage",
        "Contributor" => permission is "posts.edit",
        _ => false,
    };

    /// <summary>Adds whatever is missing. Returns how many rows were added: 0 means everything was already in place.</summary>
    public async Task<int> SeedRolesAndPermissionsAsync()
    {
        var added = 0;

        var roles = (await _roleRepo.GetAllRolesAsync()).ToDictionary(r => r.Name, r => r.Id, StringComparer.OrdinalIgnoreCase);
        var newRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, description) in DefaultRoles)
        {
            if (roles.ContainsKey(name)) continue;
            roles[name] = await _roleRepo.CreateRoleAsync(new Role { Name = name, Description = description });
            newRoles.Add(name);
            added++;
        }

        var permissions = (await _roleRepo.GetAllPermissionsAsync()).ToDictionary(p => p.Name, p => p.Id, StringComparer.OrdinalIgnoreCase);
        var newPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, action, obj) in DefaultPermissions)
        {
            if (permissions.ContainsKey(name)) continue;
            permissions[name] = await _roleRepo.CreatePermissionAsync(new Permission { Name = name, ActionType = action, ObjectType = obj });
            newPermissions.Add(name);
            added++;
        }

        foreach (var (role, _) in DefaultRoles)
        {
            var held = (await _roleRepo.GetPermissionsForRoleAsync(roles[role])).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unfinished = newRoles.Contains(role) || held.Count == 0;
            foreach (var (permission, _, _) in DefaultPermissions)
            {
                if (!RoleHolds(role, permission) || held.Contains(permission)) continue;
                if (!unfinished && !newPermissions.Contains(permission)) continue;   // the site took this one away: leave it
                await _roleRepo.AssignPermissionToRoleAsync(permissions[permission], roles[role]);
                added++;
            }
        }
        return added;
    }
}
