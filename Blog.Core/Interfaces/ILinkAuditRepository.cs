using Blog.Core.Domain;
using Blog.Core.Services;

namespace Blog.Core.Interfaces;

/// <summary>Everything the link audit needs, in one round trip.</summary>
public record LinkAuditData(
    IReadOnlyList<LinkAuditDocument> Documents,
    ISet<string> PublishedSlugs,
    IReadOnlyDictionary<string, RedirectRule> Redirects);

public interface ILinkAuditRepository
{
    /// <summary>
    /// Published posts and pages to scan, the slugs a link may legitimately resolve to, and every
    /// redirect rule. Loaded together so the analysis is a pure function over a consistent snapshot.
    /// </summary>
    Task<LinkAuditData> LoadAsync(Guid? ownerId = null);
}
