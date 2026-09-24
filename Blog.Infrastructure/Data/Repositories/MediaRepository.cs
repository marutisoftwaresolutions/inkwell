using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class MediaRepository : IMediaRepository
{
    private readonly DapperContext _ctx;
    public MediaRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<List<Media>> GetAllAsync(Guid uploadedBy, int page = 1, int pageSize = 50)
    {
        using var conn = _ctx.CreateConnection();
        var offset = (page - 1) * pageSize;
        return (await conn.QueryAsync<Media>(@"
            SELECT * FROM Media
            WHERE UploadedBy = @UploadedBy
            ORDER BY CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            new { UploadedBy = uploadedBy, Offset = offset, PageSize = pageSize })).ToList();
    }

    public async Task<Media?> GetByIdAsync(Guid id, Guid? uploadedBy = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = "SELECT * FROM Media WHERE Id = @Id";
        if (uploadedBy.HasValue) sql += " AND UploadedBy = @UploadedBy";
        return await conn.QueryFirstOrDefaultAsync<Media>(sql, new { Id = id, UploadedBy = uploadedBy });
    }

    public async Task<Guid> CreateAsync(Media media)
    {
        using var conn = _ctx.CreateConnection();
        if (media.Id == Guid.Empty) media.Id = Guid.NewGuid();
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO Media (Id, FileName, OriginalFileName, FilePath, Url, ContentType, FileSize, Width, Height, AltText, Caption, UploadedBy, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@Id, @FileName, @OriginalFileName, @FilePath, @Url, @ContentType, @FileSize, @Width, @Height, @AltText, @Caption, @UploadedBy, @CreatedAt)",
            new { media.Id, media.FileName, media.OriginalFileName, media.FilePath, media.Url, media.ContentType,
                  media.FileSize, media.Width, media.Height, media.AltText, media.Caption, media.UploadedBy,
                  CreatedAt = DateTime.UtcNow });
    }

    public async Task DeleteAsync(Guid id, Guid? uploadedBy = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = "DELETE FROM Media WHERE Id = @Id";
        if (uploadedBy.HasValue) sql += " AND UploadedBy = @UploadedBy";
        await conn.ExecuteAsync(sql, new { Id = id, UploadedBy = uploadedBy });
    }

    public async Task<int> GetTotalCountAsync(Guid? uploadedBy = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = "SELECT COUNT(*) FROM Media";
        if (uploadedBy.HasValue) sql += " WHERE UploadedBy = @UploadedBy";
        return await conn.ExecuteScalarAsync<int>(sql, new { UploadedBy = uploadedBy });
    }

    public async Task<List<Media>> SearchAsync(Guid uploadedBy, string q, int page, int pageSize, string? folder = null)
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Media>($@"
            SELECT * FROM Media
            WHERE {SearchWhere(q, folder)}
            ORDER BY CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            new { UploadedBy = uploadedBy, Like = LikePattern(q), Offset = (Math.Max(1, page) - 1) * pageSize, PageSize = pageSize })).ToList();
    }

    public async Task<int> CountSearchAsync(Guid uploadedBy, string q, string? folder = null)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM Media WHERE {SearchWhere(q, folder)}",
            new { UploadedBy = uploadedBy, Like = LikePattern(q) });
    }

    public async Task<Dictionary<Guid, List<MediaReference>>> FindReferencesAsync(IReadOnlyCollection<Media> items)
    {
        var result = new Dictionary<Guid, List<MediaReference>>();
        var candidates = items.Where(m => m.Id != Guid.Empty && !string.IsNullOrWhiteSpace(m.Url)).ToList();
        if (candidates.Count == 0) return result;

        // One VALUES row per file: its id (pages store FeaturedImageId), its URL (image fields hold the exact
        // URL) and a LIKE pattern for the body HTML. Bounded by the caller to one page of files, so this stays
        // far below SQL Server's 2,100-parameter ceiling.
        var p = new DynamicParameters();
        var rows = new List<string>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            p.Add($"i{i}", candidates[i].Id);
            p.Add($"u{i}", candidates[i].Url);
            p.Add($"l{i}", LikePattern(candidates[i].Url));
            rows.Add($"(@i{i}, @u{i}, @l{i})");
        }

        var sql = $@"
            WITH v (MediaId, Url, Pattern) AS (SELECT * FROM (VALUES {string.Join(", ", rows)}) AS t(MediaId, Url, Pattern))
            SELECT v.MediaId, 'post' AS Kind, p.Id, p.Title, p.Slug
            FROM v
            JOIN Posts p ON p.Html LIKE v.Pattern ESCAPE '\'
                         OR p.FeatureImage = v.Url OR p.OgImage = v.Url OR p.TwitterImage = v.Url
            UNION ALL
            SELECT v.MediaId, 'page' AS Kind, g.Id, g.Title, g.Slug
            FROM v
            JOIN Pages g ON g.Content LIKE v.Pattern ESCAPE '\'
                         OR g.OgImage = v.Url OR g.TwitterImage = v.Url OR g.FeaturedImageId = v.MediaId
            ORDER BY Kind DESC, Title";

        using var conn = _ctx.CreateConnection();
        var found = await conn.QueryAsync<ReferenceRow>(sql, p);
        foreach (var row in found)
        {
            if (!result.TryGetValue(row.MediaId, out var list))
                result[row.MediaId] = list = new List<MediaReference>();
            if (list.Any(r => r.Kind == row.Kind && r.Id == row.Id)) continue;
            list.Add(new MediaReference(row.Kind, row.Id, row.Title ?? string.Empty, row.Slug ?? string.Empty));
        }
        return result;
    }

    private sealed class ReferenceRow
    {
        public Guid MediaId { get; set; }
        public string Kind { get; set; } = string.Empty;
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? Slug { get; set; }
    }

    // The folder is decided in code (the controller whitelists it), never taken from the request, so the
    // clause is a literal. The classification matches the upload path shape /uploads/{folder}/{yyyy-MM}/.
    private static string SearchWhere(string q, string? folder)
    {
        var where = "UploadedBy = @UploadedBy";
        if (!string.IsNullOrWhiteSpace(q))
            where += @" AND (FileName LIKE @Like ESCAPE '\' OR OriginalFileName LIKE @Like ESCAPE '\')";
        where += (folder ?? string.Empty).ToLowerInvariant() switch
        {
            "og" => " AND FilePath LIKE '%/og/%'",
            "twitter" => " AND FilePath LIKE '%/twitter/%'",
            "images" => " AND FilePath NOT LIKE '%/og/%' AND FilePath NOT LIKE '%/twitter/%'",
            _ => string.Empty
        };
        return where;
    }

    private static string LikePattern(string s) => "%" + EscapeLike(s ?? string.Empty) + "%";

    // A search term is data, never a pattern: escape the LIKE metacharacters so "100%" finds "100%".
    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}
