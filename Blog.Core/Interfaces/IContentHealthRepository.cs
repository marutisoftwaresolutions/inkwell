using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IContentHealthRepository
{
    /// <summary>
    /// Every published post with the fields Admin → Content Health needs. Flags are derived in the
    /// domain model rather than in SQL so the rules stay in one testable place.
    /// </summary>
    Task<IReadOnlyList<ContentHealthItem>> GetPublishedAsync(Guid? ownerId = null);
}
