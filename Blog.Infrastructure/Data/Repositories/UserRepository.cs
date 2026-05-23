using Blog.Core.Domain;
using Blog.Core.Interfaces;
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
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO Users (Id, Uuid, Email, Username, DisplayName, Slug, PasswordHash, Role, Status, CreatedByUserId, Bio, ProfileImage, AvatarUrl, CoverImage, Website, Twitter, Facebook, MetaTitle, MetaDescription, IsActive, LastLogin, Credentials, Specialty, LicenseNumber, CreatedAt, UpdatedAt)
            OUTPUT INSERTED.Id
            VALUES (@Id, @Uuid, @Email, @Username, @DisplayName, @Slug, @PasswordHash, @Role, @Status, @CreatedByUserId, @Bio, @ProfileImage, @AvatarUrl, @CoverImage, @Website, @Twitter, @Facebook, @MetaTitle, @MetaDescription, @IsActive, @LastLogin, @Credentials, @Specialty, @LicenseNumber, @CreatedAt, @UpdatedAt)",
            new { user.Id, user.Uuid, user.Email, user.Username, user.DisplayName, user.Slug, user.PasswordHash, user.Role, user.Status, user.CreatedByUserId,
                  user.Bio, user.ProfileImage, user.AvatarUrl, user.CoverImage, user.Website, user.Twitter, user.Facebook, user.MetaTitle, user.MetaDescription, user.IsActive, user.LastLogin,
                  user.Credentials, user.Specialty, user.LicenseNumber,
                  CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now });
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
                  UpdatedAt = DateTime.Now, user.Id });
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
