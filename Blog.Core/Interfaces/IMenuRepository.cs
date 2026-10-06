using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IMenuRepository
{
    /// <summary>A menu's items as stored, for the editor, in sort order.</summary>
    Task<IReadOnlyList<MenuItem>> GetItemsAsync(Guid ownerId, string location);

    /// <summary>
    /// A menu's items with each target's current slug, title and availability (a page must be
    /// published), in one query — what the navbar and footer render from.
    /// </summary>
    Task<IReadOnlyList<MenuItem>> GetResolvedAsync(Guid ownerId, string location);

    /// <summary>Replaces the whole menu in one transaction; an empty list removes it.</summary>
    Task ReplaceAsync(Guid ownerId, string location, IReadOnlyList<MenuItem> items);
}
