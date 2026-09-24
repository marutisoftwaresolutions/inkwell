namespace Blog.Web.Services;

/// <summary>
/// The on-disk cache behind <c>/og-image/{slug}.png</c> (<c>wwwroot/og-cache</c>). The controller writes a
/// PNG the first time a card is requested and served it forever, so a renamed post, a changed title or a
/// deleted post kept its stale card and the folder only ever grew. Two bounded behaviours live here:
/// <see cref="Invalidate"/> drops one post's card, and <see cref="Prune"/> (run nightly by
/// <c>OgCachePruneJob</c>) drops cards older than <see cref="MaxAge"/> so a regenerated card picks up the
/// current title. Everything fails soft: a missing folder or a locked file never breaks a save.
/// </summary>
public static class OgImageCache
{
    public const string FolderName = "og-cache";

    /// <summary>Cards older than this are regenerated on next request. Thirty days matches the social crawlers' own refresh cadence.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    /// <summary>Full path of the cache folder for the given web root, or null when there is no web root.</summary>
    public static string? FolderFor(string? webRootPath)
        => string.IsNullOrEmpty(webRootPath) ? null : Path.Combine(webRootPath, FolderName);

    /// <summary>Delete the cached card for <paramref name="slug"/>. Call it after a post is updated or deleted.</summary>
    /// <returns>True when a file was removed.</returns>
    public static bool Invalidate(IWebHostEnvironment env, string? slug)
        => Invalidate(env?.WebRootPath, slug);

    public static bool Invalidate(string? webRootPath, string? slug)
    {
        var folder = FolderFor(webRootPath);
        if (folder is null || string.IsNullOrWhiteSpace(slug)) return false;
        // A slug is a single path segment; anything else could only be an attempt to leave the folder.
        var name = Path.GetFileName(slug.Trim());
        if (name.Length == 0 || name != slug.Trim()) return false;
        try
        {
            var path = Path.Combine(folder, name + ".png");
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>The age rule on its own, so it can be tested without a file system.</summary>
    public static bool IsExpired(DateTime lastWriteUtc, DateTime nowUtc, TimeSpan maxAge)
        => maxAge > TimeSpan.Zero && nowUtc - lastWriteUtc >= maxAge;

    /// <summary>Delete every cached PNG whose last write is at least <paramref name="maxAge"/> before <paramref name="nowUtc"/>.</summary>
    /// <returns>How many files were removed.</returns>
    public static int Prune(string? webRootPath, DateTime nowUtc, TimeSpan maxAge)
    {
        var folder = FolderFor(webRootPath);
        if (folder is null || !Directory.Exists(folder)) return 0;

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (!IsExpired(File.GetLastWriteTimeUtc(file), nowUtc, maxAge)) continue;
                File.Delete(file);
                removed++;
            }
            catch (IOException) { /* in use or already gone — next night */ }
            catch (UnauthorizedAccessException) { /* same */ }
        }
        return removed;
    }
}
