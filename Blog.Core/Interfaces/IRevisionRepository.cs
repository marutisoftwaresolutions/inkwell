using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IRevisionRepository
{
    /// <summary>Revisions for one entity, newest first, WITHOUT bodies (Html is empty; HtmlLength is set).</summary>
    Task<IReadOnlyList<Revision>> ListAsync(string entityType, Guid entityId);

    /// <summary>The newest revision for the entity, with its body, or null.</summary>
    Task<Revision?> GetLatestAsync(string entityType, Guid entityId);

    Task<Revision?> GetByIdAsync(Guid id);

    /// <summary>Assigns the next Number for the entity and inserts. Returns the number assigned.</summary>
    Task<int> CreateAsync(Revision revision);

    /// <summary>Deletes all but the newest <paramref name="keep"/> revisions for the entity; returns the count removed.</summary>
    Task<int> PruneAsync(string entityType, Guid entityId, int keep);
}
