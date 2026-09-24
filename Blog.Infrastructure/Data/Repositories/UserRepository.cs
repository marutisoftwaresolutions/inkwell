using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Infrastructure.Data; // SlugHelper
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class UserRepository : IUserRepository
{
    private readonly DapperContext _ctx;
    public UserRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<User?> GetByIdAsync(Guid id)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<User>("SELECT * FROM Users WHERE Id = @Id", new { Id = id });
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE Email = @Email", new { Email = email });
    }

    // Deprecated methods removed

    public async Task<Guid> CreateAsync(User user)
    {
        using var conn = _ctx.CreateConnection();
        if (user.Id == Guid.Empty) user.Id = Guid.NewGuid();
        if (user.Uuid == Guid.Empty) user.Uuid = Guid.NewGuid();
        // Every user gets a unique slug so their author E-E-A-T page (/author/{slug}) works out of the box.
        if (string.IsNullOrWhiteSpace(user.Slug))
        {
            var baseName = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName
                : !string.IsNullOrWhiteSpace(user.Username) ? user.Username
                : (user.Email ?? "user").Split('@')[0];
            user.Slug = await GenerateUniqueSlugCoreAsync(conn, baseName, user.Id);
        }
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO Users (Id, Uuid, Email, Username, DisplayName, Slug, PasswordHash, Role, Status, CreatedByUserId, Bio, ProfileImage, AvatarUrl, CoverImage, Website, Twitter, Facebook, MetaTitle, MetaDescription, IsActive, LastLogin, Credentials, Specialty, LicenseNumber, CreatedAt, UpdatedAt)
            OUTPUT INSERTED.Id
            VALUES (@Id, @Uuid, @Email, @Username, @DisplayName, @Slug, @PasswordHash, @Role, @Status, @CreatedByUserId, @Bio, @ProfileImage, @AvatarUrl, @CoverImage, @Website, @Twitter, @Facebook, @MetaTitle, @MetaDescription, @IsActive, @LastLogin, @Credentials, @Specialty, @LicenseNumber, @CreatedAt, @UpdatedAt)",
            new { user.Id, user.Uuid, user.Email, user.Username, user.DisplayName, user.Slug, user.PasswordHash, user.Role, user.Status, user.CreatedByUserId,
                  user.Bio, user.ProfileImage, user.AvatarUrl, user.CoverImage, user.Website, user.Twitter, user.Facebook, user.MetaTitle, user.MetaDescription, user.IsActive, user.LastLogin,
                  user.Credentials, user.Specialty, user.LicenseNumber,
                  CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
    }

    public async Task UpdateAsync(User user)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE Users SET Email=@Email, Username=@Username, DisplayName=@DisplayName, Slug=@Slug,
            PasswordHash=@PasswordHash, Role=@Role, Status=@Status, Bio=@Bio, ProfileImage=@ProfileImage, AvatarUrl=@AvatarUrl, CoverImage=@CoverImage, Website=@Website,
            Twitter=@Twitter, Facebook=@Facebook, MetaTitle=@MetaTitle, MetaDescription=@MetaDescription,
            IsActive=@IsActive, LastLogin=@LastLogin, Credentials=@Credentials, Specialty=@Specialty, LicenseNumber=@LicenseNumber,
            UpdatedAt=@UpdatedAt WHERE Id=@Id",
            new { user.Email, user.Username, user.DisplayName, user.Slug, user.PasswordHash, user.Role, user.Status,
                  user.Bio, user.ProfileImage, user.AvatarUrl, user.CoverImage, user.Website, user.Twitter, user.Facebook, user.MetaTitle, user.MetaDescription, user.IsActive, user.LastLogin,
                  user.Credentials, user.Specialty, user.LicenseNumber,
                  UpdatedAt = DateTime.UtcNow, user.Id });
    }

    // Authentication tracking methods (AccessFailedCount / LockoutEnd / RefreshToken) have been removed

    public async Task<bool> AnyUsersExistAsync()
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users") > 0;
    }

    public async Task<User?> GetBySlugAsync(string slug)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE Slug = @Slug", new { Slug = slug });
    }

    public async Task<string> GenerateUniqueSlugAsync(string baseName, Guid? excludeId = null)
    {
        using var conn = _ctx.CreateConnection();
        return await GenerateUniqueSlugCoreAsync(conn, baseName, excludeId);
    }

    // Core generator — reuses SlugHelper for the base slug, then appends -2, -3, … until unique.
    private static async Task<string> GenerateUniqueSlugCoreAsync(System.Data.IDbConnection conn, string baseName, Guid? excludeId)
    {
        var baseSlug = SlugHelper.Generate(baseName);
        if (string.IsNullOrEmpty(baseSlug)) baseSlug = "user";
        var slug = baseSlug;
        var n = 2;
        while (await conn.ExecuteScalarAsync<int>(
                   "SELECT COUNT(*) FROM Users WHERE Slug = @Slug" + (excludeId.HasValue ? " AND Id <> @ExcludeId" : ""),
                   new { Slug = slug, ExcludeId = excludeId }) > 0)
        {
            slug = $"{baseSlug}-{n++}";
        }
        return slug;
    }

    public async Task<int> BackfillMissingSlugsAsync()
    {
        using var conn = _ctx.CreateConnection();
        var users = (await conn.QueryAsync<User>(
            "SELECT * FROM Users WHERE Slug IS NULL OR LTRIM(RTRIM(Slug)) = ''")).ToList();
        var count = 0;
        foreach (var u in users)
        {
            var baseName = !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName
                : !string.IsNullOrWhiteSpace(u.Username) ? u.Username
                : (u.Email ?? "user").Split('@')[0];
            var slug = await GenerateUniqueSlugCoreAsync(conn, baseName, u.Id);
            await conn.ExecuteAsync("UPDATE Users SET Slug = @Slug, UpdatedAt = @Now WHERE Id = @Id",
                new { Slug = slug, Now = DateTime.UtcNow, u.Id });
            count++;
        }
        return count;
    }

    public async Task<User?> GetFirstAdminAsync()
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT TOP 1 * FROM Users WHERE Role = 'Admin' AND IsActive = 1 ORDER BY CreatedAt");
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<User>("SELECT * FROM Users ORDER BY CreatedAt DESC")).ToList();
    }

    public async Task DeleteAsync(Guid id)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM RolesUsers WHERE UserId = @Id", new { Id = id });
        await conn.ExecuteAsync("DELETE FROM Users WHERE Id = @Id", new { Id = id });
    }
}
