using System.Text.Json;
using System.Text.RegularExpressions;
using Blog.Core.Domain;
using Blog.Core.Interfaces;

namespace Blog.Web.Services.Import;

public record ImportBatchResult(int Processed, int Imported, int Skipped, int Failed, bool HasMore, List<string> Errors);

/// <summary>
/// Runs one batch of an import job: pulls the next Pending items (in dependency order), materialises
/// each into a Post/Page/Category/Tag/Media/Comment via the existing repositories, records the
/// outcome on the <see cref="ImportItem"/> row, and refreshes the job counters. Idempotent and
/// resumable — a re-run only touches remaining Pending items.
/// </summary>
public class ImportProcessor
{
    private readonly IImportJobRepository _imports;
    private readonly IPostRepository _posts;
    private readonly IPageRepository _pages;
    private readonly ICategoryRepository _categories;
    private readonly ITagRepository _tags;
    private readonly ICommentRepository _comments;
    private readonly IMediaRepository _media;
    private readonly IRedirectRepository _redirects;
    private readonly ImageImportService _imageImport;
    private readonly HtmlRewriter _rewriter;
    private readonly ILogger<ImportProcessor> _logger;

    public ImportProcessor(IImportJobRepository imports, IPostRepository posts, IPageRepository pages,
        ICategoryRepository categories, ITagRepository tags, ICommentRepository comments, IMediaRepository media,
        IRedirectRepository redirects, ImageImportService imageImport, HtmlRewriter rewriter, ILogger<ImportProcessor> logger)
    {
        _imports = imports;
        _posts = posts;
        _pages = pages;
        _categories = categories;
        _tags = tags;
        _comments = comments;
        _media = media;
        _redirects = redirects;
        _imageImport = imageImport;
        _rewriter = rewriter;
        _logger = logger;
    }

    public async Task<ImportBatchResult> ProcessBatchAsync(ImportJob job, int take)
    {
        var items = await _imports.GetPendingBatchAsync(job.Id, take);
        var opts = job.Options;
        var errors = new List<string>();
        int imported = 0, skipped = 0, failed = 0;

        foreach (var item in items)
        {
            try
            {
                var (status, targetId, error) = await ProcessItemAsync(item, opts, job);
                item.Status = status;
                item.TargetId = targetId;
                item.Error = error;
                switch (status)
                {
                    case ImportItemStatus.Imported: imported++; break;
                    case ImportItemStatus.Skipped: skipped++; break;
                    default:
                        failed++;
                        if (!string.IsNullOrEmpty(error)) errors.Add($"{item.ItemType} \"{item.Title}\": {error}");
                        break;
                }
            }
            catch (Exception ex)
            {
                item.Status = ImportItemStatus.Failed;
                item.Error = ex.Message;
                failed++;
                errors.Add($"{item.ItemType} \"{item.Title}\": {ex.Message}");
                _logger.LogWarning(ex, "Import item {Id} ({Type}) failed", item.Id, item.ItemType);
            }
            await _imports.UpdateItemAsync(item);
        }

        await _imports.RecomputeJobCountsAsync(job.Id);
        var hasMore = (await _imports.GetPendingBatchAsync(job.Id, 1)).Count > 0;
        return new ImportBatchResult(items.Count, imported, skipped, failed, hasMore, errors);
    }

    private async Task<(ImportItemStatus status, Guid? targetId, string? error)> ProcessItemAsync(
        ImportItem item, ImportOptions opts, ImportJob job)
    {
        var ownerId = job.OwnerId;
        var jobId = job.Id;
        switch (item.ItemType)
        {
            case ImportItemType.Author:
                // Phase 1: imported posts are attributed to the site owner (see below), so we don't
                // create dormant author-login accounts. Author reassignment is a future enhancement.
                return (ImportItemStatus.Skipped, null, null);

            case ImportItemType.Category:
            {
                if (!opts.ImportCategories) return (ImportItemStatus.Skipped, null, null);
                var t = Deserialize<ParsedTerm>(item.DataJson);
                var existing = await _categories.GetBySlugAsync(t.Slug, ownerId);
                if (existing != null) return (ImportItemStatus.Imported, existing.Id, null);
                var id = await _categories.CreateAsync(new Category { Name = t.Name, Slug = t.Slug, AuthorId = ownerId });
                return (ImportItemStatus.Imported, id, null);
            }

            case ImportItemType.Tag:
            {
                if (!opts.ImportTags) return (ImportItemStatus.Skipped, null, null);
                var t = Deserialize<ParsedTerm>(item.DataJson);
                var existing = await _tags.GetBySlugAsync(t.Slug, ownerId);
                if (existing != null) return (ImportItemStatus.Imported, existing.Id, null);
                var id = await _tags.CreateAsync(new Tag { Name = t.Name, Slug = t.Slug, AuthorId = ownerId });
                return (ImportItemStatus.Imported, id, null);
            }

            case ImportItemType.Image:
            {
                if (!opts.ImportImages) return (ImportItemStatus.Skipped, null, null);
                var img = Deserialize<ParsedImage>(item.DataJson);
                var res = await ImportImageAsync(img.Url, job, opts);
                return res == null
                    ? (ImportItemStatus.Failed, null, "Image could not be downloaded (blocked, missing, or not an image).")
                    : (ImportItemStatus.Imported, res.Value.MediaId, null);
            }

            case ImportItemType.Post:
            case ImportItemType.Page:
            {
                var isPage = item.ItemType == ImportItemType.Page;
                if (isPage && !opts.ImportPages) return (ImportItemStatus.Skipped, null, null);
                if (!isPage && !opts.ImportPosts) return (ImportItemStatus.Skipped, null, null);
                return await ProcessPostAsync(Deserialize<ParsedPost>(item.DataJson), opts, job, isPage);
            }

            case ImportItemType.Comment:
            {
                if (!opts.ImportComments) return (ImportItemStatus.Skipped, null, null);
                var c = Deserialize<ParsedComment>(item.DataJson);
                var postId = await _imports.FindImportedTargetAsync(jobId, ImportItemType.Post, c.PostSourceId);
                if (postId == null) return (ImportItemStatus.Skipped, null, "Parent post was not imported.");
                var id = await _comments.CreateAsync(new Comment
                {
                    PostId = postId.Value,
                    AuthorName = c.AuthorName,
                    AuthorEmail = c.AuthorEmail,
                    AuthorUrl = c.AuthorUrl,
                    Content = c.Content,
                    Status = c.Approved ? CommentStatus.Approved : CommentStatus.Pending,
                    CreatedAt = c.Date ?? DateTime.UtcNow
                });
                return (ImportItemStatus.Imported, id, null);
            }

            default:
                return (ImportItemStatus.Skipped, null, null);
        }
    }

    private async Task<(ImportItemStatus, Guid?, string?)> ProcessPostAsync(
        ParsedPost p, ImportOptions opts, ImportJob job, bool isPage)
    {
        var ownerId = job.OwnerId;
        var jobId = job.Id;
        // Status mapping.
        var wpStatus = (p.Status ?? "publish").ToLowerInvariant();
        var isPublished = wpStatus == "publish";
        if (opts.PublishedOnly && !isPublished) return (ImportItemStatus.Skipped, null, null);

        // Body: WordPress paragraph normalisation → image remap → sanitize.
        var html = _rewriter.WpAutoParagraph(p.Html);
        var imageUrls = _rewriter.ExtractImageUrls(html);
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in imageUrls)
            if (!map.ContainsKey(u)) map[u] = await ResolveImageUrlAsync(job, u, opts);
        html = _rewriter.RewriteImageUrls(html, old => map.TryGetValue(old, out var nu) ? nu : null);
        if (opts.SanitizeHtml) html = _rewriter.Sanitize(html);

        // Feature image.
        string? featureUrl = null;
        if (!string.IsNullOrWhiteSpace(p.FeatureImageUrl))
            featureUrl = await ResolveImageUrlAsync(job, p.FeatureImageUrl!, opts);

        var authorId = opts.DefaultAuthorId ?? ownerId;
        var baseSlug = !string.IsNullOrWhiteSpace(p.Slug) ? p.Slug!.Trim() : Slugify(p.Title);
        if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = Slugify(p.Title);

        if (isPage)
        {
            var (slug, overwriteId, skip) = await ResolvePageSlugAsync(baseSlug, ownerId, opts);
            if (skip) return (ImportItemStatus.Skipped, null, "Slug already exists (skipped).");
            var page = new Page
            {
                Title = p.Title, Slug = slug, Content = html, AuthorId = authorId,
                IsPublished = isPublished, IsInNav = false,
                PublishedAt = p.PublishedAt, CreatedAt = p.PublishedAt ?? DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                MetaTitle = p.MetaTitle, MetaDescription = p.MetaDescription, FeaturedImageUrl = featureUrl
            };
            Guid pageId;
            if (overwriteId.HasValue) { page.Id = overwriteId.Value; await _pages.UpdateAsync(page); pageId = overwriteId.Value; }
            else pageId = await _pages.CreateAsync(page);
            await MaybeCreateRedirectAsync(opts, p.OriginalLink, slug);
            return (ImportItemStatus.Imported, pageId, null);
        }

        // Post
        var (postSlug, postOverwriteId, postSkip) = await ResolvePostSlugAsync(baseSlug, opts);
        if (postSkip) return (ImportItemStatus.Skipped, null, "Slug already exists (skipped).");

        var status = wpStatus switch
        {
            "publish" => PostStatus.Published,
            "future" => PostStatus.Scheduled,
            _ => PostStatus.Draft
        };
        var post = new Post
        {
            Id = postOverwriteId ?? Guid.Empty,
            Title = p.Title,
            Slug = postSlug,
            Html = html,
            Plaintext = StripTags(html),
            Status = status,
            PublishedAt = p.PublishedAt,
            ScheduledAt = wpStatus == "future" ? p.PublishedAt : null,
            AuthorId = authorId,
            Type = "post",
            FeatureImage = featureUrl,
            MetaTitle = p.MetaTitle,
            MetaDescription = string.IsNullOrWhiteSpace(p.MetaDescription) ? Truncate(StripTags(p.Excerpt), 300) : p.MetaDescription,
            CreatedAt = p.PublishedAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            AllowComments = true
        };

        Guid postId;
        if (postOverwriteId.HasValue) { await _posts.UpdateAsync(post); postId = postOverwriteId.Value; }
        else postId = await _posts.CreateAsync(post);

        // Categories & tags (only those actually imported, respecting the toggles).
        var catIds = await ResolveTermIdsAsync(jobId, ImportItemType.Category, p.CategorySlugs);
        if (catIds.Count > 0) await _posts.AssignCategoriesAsync(postId, catIds);
        var tagIds = await ResolveTermIdsAsync(jobId, ImportItemType.Tag, p.TagSlugs);
        if (tagIds.Count > 0) await _posts.AssignTagsAsync(postId, tagIds);

        await MaybeCreateRedirectAsync(opts, p.OriginalLink, postSlug);
        return (ImportItemStatus.Imported, postId, null);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<string?> ResolveImageUrlAsync(ImportJob job, string oldUrl, ImportOptions opts)
    {
        var target = await _imports.FindImportedTargetAsync(job.Id, ImportItemType.Image, oldUrl);
        if (target == null)
        {
            var stripped = HtmlRewriter.StripSizeSuffix(oldUrl);
            if (!string.Equals(stripped, oldUrl, StringComparison.OrdinalIgnoreCase))
                target = await _imports.FindImportedTargetAsync(job.Id, ImportItemType.Image, stripped);
        }
        if (target != null)
        {
            var m = await _media.GetByIdAsync(target.Value);
            if (m != null) return m.Url;
        }
        // Inline image not already imported as an attachment — bring it in on demand.
        if (opts.ImportImages)
        {
            var res = await ImportImageAsync(oldUrl, job, opts);
            return res?.Url;
        }
        return null;
    }

    /// <summary>
    /// Imports one image, preferring the uploaded export archive for Ghost <c>.zip</c> imports
    /// (<c>content/images/**</c>) so images migrate even when the source site is offline; falls back
    /// to an SSRF-guarded URL download.
    /// </summary>
    private async Task<(Guid MediaId, string Url)?> ImportImageAsync(string url, ImportJob job, ImportOptions opts)
    {
        if (job.Source == ImportSource.Ghost && !string.IsNullOrEmpty(job.FilePath) &&
            job.FilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var key = ImageImportService.ZipImageKey(url);
            if (key != null)
            {
                var fromZip = await _imageImport.ImportFromZipEntryAsync(job.FilePath!, key, job.OwnerId, opts.ConvertToWebP);
                if (fromZip != null) return fromZip;
            }
        }
        return await _imageImport.ImportFromUrlAsync(url, job.OwnerId, opts.ConvertToWebP);
    }

    /// <summary>
    /// Reverts an import: deletes the posts, pages, images and comments it created (newest content
    /// first). Deliberately leaves categories/tags (they may be shared with pre-existing content) and
    /// is refused by the caller for "Overwrite" jobs (where targets may be pre-existing). Returns the
    /// number of entities removed.
    /// </summary>
    public async Task<int> UndoAsync(ImportJob job)
    {
        var items = await _imports.GetItemsAsync(job.Id, ImportItemStatus.Imported);
        var removed = 0;
        foreach (var it in items.Where(i => i.ItemType == ImportItemType.Comment && i.TargetId.HasValue))
        { await _comments.DeleteAsync(it.TargetId!.Value); removed++; }
        foreach (var it in items.Where(i => i.ItemType == ImportItemType.Post && i.TargetId.HasValue))
        { await _posts.DeleteAsync(it.TargetId!.Value); removed++; }
        foreach (var it in items.Where(i => i.ItemType == ImportItemType.Page && i.TargetId.HasValue))
        { await _pages.DeleteAsync(it.TargetId!.Value, job.OwnerId); removed++; }
        foreach (var it in items.Where(i => i.ItemType == ImportItemType.Image && i.TargetId.HasValue))
        { await _media.DeleteAsync(it.TargetId!.Value, job.OwnerId); removed++; }
        return removed;
    }

    private async Task<List<Guid>> ResolveTermIdsAsync(Guid jobId, ImportItemType type, List<string> slugs)
    {
        var ids = new List<Guid>();
        foreach (var slug in slugs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var t = await _imports.FindImportedTargetAsync(jobId, type, slug);
            if (t != null) ids.Add(t.Value);
        }
        return ids;
    }

    private async Task<(string slug, Guid? overwriteId, bool skip)> ResolvePostSlugAsync(string baseSlug, ImportOptions opts)
    {
        if (!await _posts.SlugExistsAsync(baseSlug)) return (baseSlug, null, false);
        switch (opts.SlugConflict)
        {
            case "Skip": return (baseSlug, null, true);
            case "Overwrite":
                var existing = await _posts.GetBySlugAsync(baseSlug);
                return (baseSlug, existing?.Id, false);
            default:
                var n = 2;
                while (await _posts.SlugExistsAsync($"{baseSlug}-{n}")) n++;
                return ($"{baseSlug}-{n}", null, false);
        }
    }

    private async Task<(string slug, Guid? overwriteId, bool skip)> ResolvePageSlugAsync(string baseSlug, Guid ownerId, ImportOptions opts)
    {
        if (!await _pages.SlugExistsAsync(baseSlug, ownerId)) return (baseSlug, null, false);
        switch (opts.SlugConflict)
        {
            case "Skip": return (baseSlug, null, true);
            case "Overwrite":
                var existing = await _pages.GetBySlugAsync(baseSlug);
                return (baseSlug, existing?.Id, false);
            default:
                var n = 2;
                while (await _pages.SlugExistsAsync($"{baseSlug}-{n}", ownerId)) n++;
                return ($"{baseSlug}-{n}", null, false);
        }
    }

    private async Task MaybeCreateRedirectAsync(ImportOptions opts, string? originalLink, string newSlug)
    {
        if (!opts.CreateRedirects || string.IsNullOrWhiteSpace(originalLink)) return;
        try
        {
            if (!Uri.TryCreate(originalLink, UriKind.Absolute, out var uri)) return;
            var oldPath = uri.AbsolutePath.TrimEnd('/');
            if (string.IsNullOrEmpty(oldPath)) return;
            var newPath = "/" + newSlug;
            if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                await _redirects.UpsertAsync(oldPath, newPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redirect creation failed for {Link}", originalLink);
        }
    }

    private static T Deserialize<T>(string? json) where T : new() =>
        string.IsNullOrWhiteSpace(json) ? new T()
            : JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new T();

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "post-" + Guid.NewGuid().ToString("N")[..8];
        var s = value.ToLowerInvariant().Trim();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"[\s_]+", "-");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return string.IsNullOrEmpty(s) ? "post-" + Guid.NewGuid().ToString("N")[..8] : s;
    }

    private static string StripTags(string? html) =>
        string.IsNullOrEmpty(html) ? "" : System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")).Trim();

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
