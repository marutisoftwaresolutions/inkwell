using Blog.Core.Services;

namespace Blog.Core.Interfaces;

public interface ILinkSuggestionRepository
{
    /// <summary>
    /// Every published post with the taxonomy and body the suggester needs. Loaded in one pass so
    /// scoring is a pure function over a consistent snapshot.
    /// </summary>
    Task<IReadOnlyList<LinkCandidate>> GetCandidatesAsync(Guid? ownerId = null);
}
