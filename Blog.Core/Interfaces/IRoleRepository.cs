using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IRoleRepository
{
    Task<List<Role>> GetAllRolesAsync();
    Task<Role?> GetByIdAsync(Guid id);
    Task<Role?> GetByNameAsync(string name);
    Task<Role?> GetRoleForUserAsync(Guid userId);
    Task<List<Permission>> GetPermissionsForRoleAsync(Guid roleId);
    Task<List<Permission>> GetAllPermissionsAsync();
    Task<List<string>> GetPermissionNamesForUserAsync(Guid userId);
    Task AssignRoleToUserAsync(Guid userId, Guid roleId);
    /// <summary>Gives the Admin role to every account marked Admin that has no role assignment; returns how many.</summary>
    Task<int> AssignMissingAdminRolesAsync();
    Task RemoveRoleFromUserAsync(Guid userId);
    Task<Guid> CreateRoleAsync(Role role);
    Task<Guid> CreatePermissionAsync(Permission permission);
    Task AssignPermissionToRoleAsync(Guid permissionId, Guid roleId);
}
