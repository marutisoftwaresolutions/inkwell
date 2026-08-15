namespace Blog.Core.Domain;

/// <summary>
/// A Series (a.k.a. collection) is an ordered set of published posts that form a reading
/// sequence — e.g. a multi-part guide. Shaped like a taxonomy entity (Category/Tag) but with
/// explicit post ordering (via the SeriesPosts join table's SortOrder) and a public reading flow.
/// </summary>
public class Series
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid AuthorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Count of published posts in the series — computed, not stored.</summary>
    public int PostCount { get; set; }
}

/// <summary>
/// The reading-flow context for a single post that belongs to a series: the series itself, the
/// full ordered list of (published) posts in it, and this post's position. Drives the in-article
/// "Part N of M · Previous / Next" navigation. Never thrown from — absent when a post has no series.
/// </summary>
public record SeriesNav(Series Series, IReadOnlyList<Post> Posts, int CurrentIndex)
{
    public Post? Previous => CurrentIndex > 0 ? Posts[CurrentIndex - 1] : null;
    public Post? Next => CurrentIndex >= 0 && CurrentIndex < Posts.Count - 1 ? Posts[CurrentIndex + 1] : null;
    public int Part => CurrentIndex + 1;
    public int Total => Posts.Count;
}
