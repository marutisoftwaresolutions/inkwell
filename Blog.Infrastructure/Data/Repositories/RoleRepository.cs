using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly DapperContext _ctx;
    public RoleRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<List<Role>> GetAllRolesAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Role>("SELECT * FROM Roles ORDER BY Name")).ToList();
    }

    public async Task<Role?> GetByIdAsync(Guid id)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Role>(
            "SELECT * FROM Roles WHERE Id = @Id", new { Id = id });
    }

    public async Task<Role?> GetByNameAsync(string name)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Role>(
            "SELECT * FROM Roles WHERE Name = @Name", new { Name = name });
    }

    public async Task<Role?> GetRoleForUserAsync(Guid userId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Role>(@"
            SELECT r.* FROM Roles r
            INNER JOIN RolesUsers ru ON ru.RoleId = r.Id
            WHERE ru.UserId = @UserId", new { UserId = userId });
    }

    public async Task<List<Permission>> GetPermissionsForRoleAsync(Guid roleId)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Permission>(@"
            SELECT p.* FROM Permissions p
            INNER JOIN PermissionsRoles pr ON pr.PermissionId = p.Id
            WHERE pr.RoleId = @RoleId", new { RoleId = roleId })).ToList();
    }

    public async Task<List<string>> GetPermissionNamesForUserAsync(Guid userId)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<string>(@"
            SELECT DISTINCT p.Name FROM Permissions p
            INNER JOIN PermissionsRoles pr ON pr.PermissionId = p.Id
            INNER JOIN RolesUsers ru ON ru.RoleId = pr.RoleId
            WHERE ru.UserId = @UserId", new { UserId = userId })).ToList();
    }

    public async Task AssignRoleToUserAsync(Guid userId, Guid roleId)
    {
        using var conn = _ctx.CreateConnection();
        // Remove existing role assignment first
        await conn.ExecuteAsync("DELETE FROM RolesUsers WHERE UserId = @UserId", new { UserId = userId });
        await conn.ExecuteAsync(
            "INSERT INTO RolesUsers (RoleId, UserId) VALUES (@RoleId, @UserId)",
            new { RoleId = roleId, UserId = userId });
    }

    public async Task<int> AssignMissingAdminRolesAsync()
    {
        using var conn = _ctx.CreateConnection();
        // Repairs sites installed through the setup wizard before it assigned roles: the account says
        // Admin but had no row here, so permission checks that read the role tables refused it.
        return await conn.ExecuteAsync(@"
            INSERT INTO RolesUsers (RoleId, UserId)
            SELECT r.Id, u.Id
            FROM Users u
            JOIN Roles r ON r.Name = N'Admin'
            WHERE u.Role = N'Admin'
              AND NOT EXISTS (SELECT 1 FROM RolesUsers ru WHERE ru.UserId = u.Id)");
    }

    public async Task RemoveRoleFromUserAsync(Guid userId)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM RolesUsers WHERE UserId = @UserId", new { UserId = userId });
    }

    public async Task<List<Permission>> GetAllPermissionsAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Permission>("SELECT * FROM Permissions")).ToList();
    }

    public async Task<Guid> CreateRoleAsync(Role role)
    {
        using var conn = _ctx.CreateConnection();
        if (role.Id == Guid.Empty) role.Id = Guid.NewGuid();
        if (role.Uuid == Guid.Empty) role.Uuid = Guid.NewGuid();
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO Roles (Id, Uuid, Name, Description)
            OUTPUT INSERTED.Id
            VALUES (@Id, @Uuid, @Name, @Description)",
            new { role.Id, role.Uuid, role.Name, role.Description });
    }

    public async Task<Guid> CreatePermissionAsync(Permission permission)
    {
        using var conn = _ctx.CreateConnection();
        if (permission.Id == Guid.Empty) permission.Id = Guid.NewGuid();
        if (permission.Uuid == Guid.Empty) permission.Uuid = Guid.NewGuid();
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO Permissions (Id, Uuid, Name, ActionType, ObjectType)
            OUTPUT INSERTED.Id
            VALUES (@Id, @Uuid, @Name, @ActionType, @ObjectType)",
            new { permission.Id, permission.Uuid, permission.Name, permission.ActionType, permission.ObjectType });
    }

    public async Task AssignPermissionToRoleAsync(Guid permissionId, Guid roleId)
    {
        using var conn = _ctx.CreateConnection();
        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM PermissionsRoles WHERE PermissionId = @PermissionId AND RoleId = @RoleId",
            new { PermissionId = permissionId, RoleId = roleId });
        if (exists == 0)
        {
            await conn.ExecuteAsync(
                "INSERT INTO PermissionsRoles (PermissionId, RoleId) VALUES (@PermissionId, @RoleId)",
                new { PermissionId = permissionId, RoleId = roleId });
        }
    }
}
