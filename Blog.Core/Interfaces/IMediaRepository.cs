using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

/// <summary>A post or page that embeds a media file (in its body HTML or one of its image fields).</summary>
public sealed record MediaReference(string Kind, Guid Id, string Title, string Slug);

public interface IMediaRepository
{
    Task<List<Media>> GetAllAsync(Guid uploadedBy, int page = 1, int pageSize = 50);
    Task<Media?> GetByIdAsync(Guid id, Guid? uploadedBy = null);
    Task<Guid> CreateAsync(Media media);
    Task DeleteAsync(Guid id, Guid? uploadedBy = null);
    Task<int> GetTotalCountAsync(Guid? uploadedBy = null);

    /// <summary>
    /// Files whose stored or original name contains <paramref name="q"/> (empty = every file), newest first,
    /// paged in SQL. <paramref name="folder"/> narrows to one upload folder — <c>"images"</c>, <c>"og"</c> or
    /// <c>"twitter"</c>; null or empty means every folder.
    /// </summary>
    Task<List<Media>> SearchAsync(Guid uploadedBy, string q, int page, int pageSize, string? folder = null);
    Task<int> CountSearchAsync(Guid uploadedBy, string q, string? folder = null);

    /// <summary>
    /// Posts and pages that still embed each of <paramref name="items"/> (body HTML, feature, Open Graph or
    /// Twitter image), keyed by <see cref="Media.Id"/>. One query; the caller bounds it to one page of files.
    /// A file with no references has no entry.
    /// </summary>
    Task<Dictionary<Guid, List<MediaReference>>> FindReferencesAsync(IReadOnlyCollection<Media> items);
}
