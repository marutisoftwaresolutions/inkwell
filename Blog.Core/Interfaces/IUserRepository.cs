using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);
    Task<User?> GetByEmailAsync(string email);
    Task<Guid> CreateAsync(User user);
    Task UpdateAsync(User user);
    Task<bool> AnyUsersExistAsync();
    Task<User?> GetBySlugAsync(string slug);
    Task<User?> GetFirstAdminAsync();
    Task<List<User>> GetAllUsersAsync();
    Task DeleteAsync(Guid id);

    /// <summary>Generates a URL-safe slug from <paramref name="baseName"/> that is unique across Users
    /// (appending -2, -3, … on collision). Pass the user's own Id as <paramref name="excludeId"/> when editing.</summary>
    Task<string> GenerateUniqueSlugAsync(string baseName, Guid? excludeId = null);

    /// <summary>Assigns a unique slug to every user that currently has none (NULL/empty). Idempotent.
    /// Returns the number of users updated. Enables author E-E-A-T pages for pre-existing users.</summary>
    Task<int> BackfillMissingSlugsAsync();
}
