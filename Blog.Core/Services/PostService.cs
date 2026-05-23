using Blog.Core.Domain;
using Blog.Core.Interfaces;
using System.Text.RegularExpressions;

namespace Blog.Core.Services;

public class PostService
{
    private readonly IPostRepository _posts;
    private readonly ICategoryRepository _categories;
    private readonly ITagRepository _tags;
    private readonly IRedirectRepository _redirects;

    private static readonly Regex YearInSlug = new(@"\b(20\d{2})\b", RegexOptions.Compiled);

    public PostService(IPostRepository posts, ICategoryRepository categories,
        ITagRepository tags, IRedirectRepository redirects)
    {
        _posts = posts;
        _categories = categories;
        _tags = tags;
        _redirects = redirects;
    }

    public async Task<(Guid id, string? error)> CreatePostAsync(Post post, List<Guid> categoryIds, List<string> tagNames, Guid userId, string userEmail)
    {
        if (string.IsNullOrWhiteSpace(post.Title))
            return (Guid.Empty, "Title is required.");

        if (string.IsNullOrWhiteSpace(post.Slug))
            post.Slug = GenerateSlug(post.Title);

        if (await _posts.SlugExistsAsync(post.Slug))
            post.Slug = $"{post.Slug}-{DateTime.Now.Ticks % 10000}";

        if (post.Status == PostStatus.Published && !post.PublishedAt.HasValue)
            post.PublishedAt = DateTime.Now;

        var id = await _posts.CreateAsync(post);

        if (categoryIds.Any())
            await _posts.AssignCategoriesAsync(id, categoryIds);

        if (tagNames.Any())
        {
            var tagIds = new List<Guid>();
            foreach (var name in tagNames)
            {
                var tag = await _tags.GetOrCreateAsync(name.Trim(), userId);
                tagIds.Add(tag.Id);
            }
            await _posts.AssignTagsAsync(id, tagIds);
        }

        return (id, null);
    }

    public async Task<string?> UpdatePostAsync(Post post, List<Guid> categoryIds, List<string> tagNames, Guid userId, string userEmail)
    {
        var existing = await _posts.GetByIdAsync(post.Id);
        if (existing == null) return "Post not found.";

        if (string.IsNullOrWhiteSpace(post.Slug))
            post.Slug = GenerateSlug(post.Title);

        if (await _posts.SlugExistsAsync(post.Slug, post.Id))
            return "Slug already in use by another post.";

        if (post.Status == PostStatus.Published && !post.PublishedAt.HasValue)
            post.PublishedAt = existing.PublishedAt ?? DateTime.Now;

        post.AuthorId = existing.AuthorId;
        post.CreatedAt = existing.CreatedAt;
        post.ViewCount = existing.ViewCount;
        post.CommentCount = existing.CommentCount;

        // Auto-update year in slug when PublishedAt, LastVerifiedAt, or NextReviewAt changes,
        // and create a redirect from the old slug to the new one.
        var oldSlug = existing.Slug;
        var slugYearMatch = YearInSlug.Match(post.Slug);
        if (slugYearMatch.Success)
        {
            // Determine the best year: prefer PublishedAt, fall back to LastVerifiedAt
            var primaryDate = post.PublishedAt ?? post.LastVerifiedAt;
            if (primaryDate.HasValue)
            {
                var newYear = primaryDate.Value.Year.ToString();
                var oldYear = slugYearMatch.Value;
                if (newYear != oldYear)
                {
                    var updatedSlug = YearInSlug.Replace(post.Slug, newYear, 1);
                    // Only switch if the new slug isn't already taken by another post
                    if (!await _posts.SlugExistsAsync(updatedSlug, post.Id))
                    {
                        post.Slug = updatedSlug;
                        await _redirects.UpsertAsync($"/{oldSlug}", $"/{updatedSlug}");
                    }
                }
            }
        }

        // If the slug itself changed (manually or via year update), create a redirect
        if (oldSlug != post.Slug && !YearInSlug.IsMatch(oldSlug))
            await _redirects.UpsertAsync($"/{oldSlug}", $"/{post.Slug}");

        await _posts.UpdateAsync(post);
        await _posts.AssignCategoriesAsync(post.Id, categoryIds);

        var tagIds = new List<Guid>();
        foreach (var name in tagNames)
        {
            var tag = await _tags.GetOrCreateAsync(name.Trim(), userId);
            tagIds.Add(tag.Id);
        }
        await _posts.AssignTagsAsync(post.Id, tagIds);

        return null;
    }

    public async Task<string?> PublishPostAsync(Guid id, Guid userId, string userEmail)
    {
        var post = await _posts.GetByIdAsync(id);
        if (post == null) return "Post not found.";

        post.Status = PostStatus.Published;
        post.PublishedAt ??= DateTime.Now;
        await _posts.UpdateAsync(post);

        return null;
    }

    private static string GenerateSlug(string title)
    {
        var slug = title.ToLowerInvariant().Replace("_", "-");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"-+", "-");
        return slug.Trim('-');
    }
}
