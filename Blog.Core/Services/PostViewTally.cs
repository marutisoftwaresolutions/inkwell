namespace Blog.Core.Services;

/// <summary>
/// "Most read" (<c>Posts.ViewCount</c>) is counted from the same page-view beacon as the Analytics
/// screen, so both figures mean a reader who ran the page's script (ADR-0014). The traffic recorder
/// hands each flushed batch of recorded paths here; one row per post comes back, and the repository
/// adds the counts in one round trip per batch rather than one write per view.
/// </summary>
public static class PostViewTally
{
    /// <summary>
    /// Counts views per (owner, slug). Only single-segment paths can be posts: the home page and
    /// anything under a second slash are skipped, and the slug is lower-cased so /My-Post and
    /// /my-post count together. Whether a slug names a published post is the repository's check.
    /// </summary>
    public static IReadOnlyList<PostViewCount> Count(IEnumerable<(Guid OwnerId, string Path)> views) =>
        views
            .Select(v => (v.OwnerId, Slug: SlugOf(v.Path)))
            .Where(v => v.Slug is not null)
            .GroupBy(v => (v.OwnerId, v.Slug))
            .Select(g => new PostViewCount(g.Key.OwnerId, g.Key.Slug!, g.Count()))
            .ToList();

    /// <summary>The slug a beacon path names, or null for the home page or a nested path.</summary>
    public static string? SlugOf(string? path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/' || path.Length < 2) return null;
        var slug = path[1..];
        return slug.Contains('/') || slug.Contains('?') ? null : slug.ToLowerInvariant();
    }
}

/// <summary>Views to add to one post. <see cref="OwnerId"/> is <see cref="Guid.Empty"/> outside cloud mode.</summary>
public sealed record PostViewCount(Guid OwnerId, string Slug, int Views);
