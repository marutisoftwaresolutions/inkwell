using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IContentHealthRepository
{
    /// <summary>
    /// Every published post with the fields Admin → Content Health needs. Flags are derived in the
    /// domain model rather than in SQL so the rules stay in one testable place.
    /// </summary>
    Task<IReadOnlyList<ContentHealthItem>> GetPublishedAsync(Guid? ownerId = null);

    /// <summary>
    /// The bodies of the same published posts, keyed by post id. Loaded only for the accessibility view, which has to
    /// read the HTML; every other view stays body-free.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetPublishedHtmlAsync(Guid? ownerId = null);
}
