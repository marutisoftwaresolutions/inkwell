using System.Runtime.CompilerServices;
using Blog.Core.Domain;
using Blog.Core.Services.Export;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Blog.Infrastructure.Data.Repositories;

/// <summary>
/// Reads what a WordPress or Ghost export writes (ADR-0020). Relations (taxonomy, series, comments) are
/// loaded once as small maps; post and page bodies are streamed with an unbuffered reader so a large
/// site never sits in memory. Install-wide: the export screen is not offered in cloud mode.
/// </summary>
public sealed class ExportRepository : IExportSource
{
    private readonly DapperContext _ctx;
    public ExportRepository(DapperContext ctx) => _ctx = ctx;

    private static string[] Statuses(ExportOptions o) => o.IncludeDrafts
        ? new[] { nameof(PostStatus.Published), nameof(PostStatus.Scheduled), nameof(PostStatus.Draft), nameof(PostStatus.InReview), nameof(PostStatus.NeedsChanges) }
        : new[] { nameof(PostStatus.Published), nameof(PostStatus.Scheduled) };

    private const string PageFilter = "(@IncludePages = 1 AND (@IncludeDrafts = 1 OR IsPublished = 1))";

    private static object Args(ExportOptions o) => new { Statuses = Statuses(o), o.IncludePages, o.IncludeDrafts };

    public async Task<ExportCounts> CountAsync(ExportOptions o)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.QuerySingleAsync<ExportCounts>($@"
            WITH P AS (SELECT * FROM Posts WHERE Status IN @Statuses)
            SELECT
              (SELECT COUNT(*) FROM P) AS Posts,
              (SELECT COUNT(*) FROM Pages WHERE {PageFilter}) AS Pages,
              (SELECT COUNT(*) FROM Categories) AS Categories,
              (SELECT COUNT(*) FROM Tags) AS Tags,
              (SELECT COUNT(DISTINCT A) FROM (SELECT AuthorId AS A FROM P UNION SELECT AuthorId FROM Pages WHERE {PageFilter}) x) AS Authors,
              (SELECT COUNT(*) FROM Comments c JOIN P ON P.Id = c.PostId) AS Comments,
              (SELECT COUNT(*) FROM P WHERE NULLIF(LTRIM(RoundupJson), '') IS NOT NULL) AS WithRoundup,
              (SELECT COUNT(*) FROM P WHERE NULLIF(LTRIM(FaqJson), '') IS NOT NULL AND FaqJson <> '[]') AS WithFaq,
              (SELECT COUNT(*) FROM P WHERE NULLIF(LTRIM(HowToJson), '') IS NOT NULL AND HowToJson <> '[]') AS WithHowTo,
              (SELECT COUNT(*) FROM P WHERE NULLIF(LTRIM(KeyFactsJson), '') IS NOT NULL AND KeyFactsJson <> '[]') AS WithKeyFacts,
              (SELECT COUNT(*) FROM P WHERE NULLIF(LTRIM(AnswerCapsule), '') IS NOT NULL) AS WithAnswerCapsule,
              (SELECT COUNT(DISTINCT sp.PostId) FROM SeriesPosts sp JOIN P ON P.Id = sp.PostId) AS InSeries,
              (SELECT COUNT(*) FROM P WHERE COALESCE(MetaTitle, MetaDescription, CanonicalUrl, OgTitle, OgDescription, OgImage, TwitterTitle, TwitterDescription, TwitterImage) IS NOT NULL)
                + (SELECT COUNT(*) FROM Pages WHERE {PageFilter} AND COALESCE(MetaTitle, MetaDescription, CanonicalUrl, OgTitle, OgDescription, OgImage, TwitterTitle, TwitterDescription, TwitterImage) IS NOT NULL) AS WithSeo,
              (SELECT COUNT(*) FROM P WHERE Status IN ('Draft', 'NeedsChanges')) AS Drafts,
              (SELECT COUNT(*) FROM P WHERE Status = 'InReview') AS InReview,
              (SELECT COUNT(*) FROM P WHERE NULLIF(LTRIM(CustomFieldsJson), '') IS NOT NULL)
                + (SELECT COUNT(*) FROM Pages WHERE {PageFilter} AND NULLIF(LTRIM(CustomFieldsJson), '') IS NOT NULL) AS WithCustomFields,
              (SELECT COUNT(*) FROM Pages WHERE {PageFilter} AND Content LIKE '%comp-form%') AS WithForms", Args(o));
    }

    public async Task<ExportCatalog> LoadCatalogAsync(ExportOptions o)
    {
        using var conn = _ctx.CreateConnection();
        var categories = (await conn.QueryAsync<ExportTerm>("SELECT Id, Name, Slug FROM Categories ORDER BY Name")).ToList();
        var tags = (await conn.QueryAsync<ExportTerm>("SELECT Id, Name, Slug FROM Tags ORDER BY Name")).ToList();
        // Only people who authored something in the export: other staff never leave the site.
        var authors = (await conn.QueryAsync<AuthorRow>($@"
            SELECT u.Id, u.Username, u.DisplayName, u.Slug, u.Email, u.Bio, u.Website,
                   COALESCE(NULLIF(u.ProfileImage, ''), u.AvatarUrl) AS ProfileImage, u.Role
            FROM Users u
            WHERE u.Id IN (SELECT AuthorId FROM Posts WHERE Status IN @Statuses
                           UNION SELECT AuthorId FROM Pages WHERE {PageFilter})
            ORDER BY u.DisplayName", Args(o)))
            .Select(a => new ExportAuthor(a.Id, string.IsNullOrWhiteSpace(a.Username) ? a.Slug ?? a.Id.ToString("N") : a.Username,
                string.IsNullOrWhiteSpace(a.DisplayName) ? a.Username ?? "" : a.DisplayName,
                string.IsNullOrWhiteSpace(a.Slug) ? a.Id.ToString("N")[..8] : a.Slug,
                o.IncludeEmails ? a.Email : null, a.Bio, a.Website, a.ProfileImage, a.Role ?? "Author"))
            .ToList();
        var media = (await conn.QueryAsync<(string Url, string? AltText, string? Caption)>(
                "SELECT Url, AltText, Caption FROM Media WHERE Url IS NOT NULL"))
            .GroupBy(m => m.Url, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (g.First().AltText, g.First().Caption), StringComparer.OrdinalIgnoreCase);
        return new ExportCatalog(categories, tags, authors, media);
    }

    public async IAsyncEnumerable<ExportPost> StreamPostsAsync(ExportOptions o, [EnumeratorCancellation] CancellationToken ct)
    {
        // Relations first, as maps keyed by post id: small next to the bodies.
        Dictionary<Guid, List<Guid>> cats, tags;
        Dictionary<Guid, List<ExportSeriesRef>> series;
        Dictionary<Guid, List<ExportComment>> comments;
        using (var conn = _ctx.CreateConnection())
        {
            cats = (await conn.QueryAsync<(Guid PostId, Guid Id)>("SELECT PostId, CategoryId FROM PostCategories"))
                .GroupBy(x => x.PostId).ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());
            tags = (await conn.QueryAsync<(Guid PostId, Guid Id)>("SELECT PostId, TagId FROM PostTags ORDER BY PostId, SortOrder"))
                .GroupBy(x => x.PostId).ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());
            series = (await conn.QueryAsync<(Guid PostId, string Title, string Slug, long Part)>(@"
                    SELECT sp.PostId, s.Title, s.Slug,
                           ROW_NUMBER() OVER (PARTITION BY sp.SeriesId ORDER BY sp.SortOrder) AS Part
                    FROM SeriesPosts sp JOIN Series s ON s.Id = sp.SeriesId"))
                .GroupBy(x => x.PostId).ToDictionary(g => g.Key, g => g.Select(x => new ExportSeriesRef(x.Title, x.Slug, (int)x.Part)).ToList());
            comments = o.IncludeComments
                ? (await conn.QueryAsync<CommentRow>(@"
                        SELECT Id, PostId, ParentId, AuthorName, AuthorEmail, AuthorUrl, Content, Html, Status, CreatedAt
                        FROM Comments ORDER BY CreatedAt"))
                    .GroupBy(c => c.PostId).ToDictionary(g => g.Key, g => g.Select(c => new ExportComment(c.Id, c.ParentId,
                        string.IsNullOrWhiteSpace(c.AuthorName) ? "Anonymous" : c.AuthorName!,
                        o.IncludeEmails ? c.AuthorEmail : null, c.AuthorUrl,
                        !string.IsNullOrWhiteSpace(c.Html) ? c.Html! : System.Net.WebUtility.HtmlEncode(c.Content ?? ""),
                        string.Equals(c.Status, nameof(CommentStatus.Approved), StringComparison.OrdinalIgnoreCase),
                        c.CreatedAt)).ToList())
                : new();
        }

        await using var sql = _ctx.CreateSqlConnection();
        await sql.OpenAsync(ct);
        var rows = sql.QueryUnbufferedAsync<PostRow>(@"
            SELECT Id, Title, Slug, Html, Status, CreatedAt, UpdatedAt, PublishedAt, ScheduledAt, AuthorId, FeatureImage,
                   AllowComments, MetaTitle, MetaDescription, CanonicalUrl, OgTitle, OgDescription, OgImage,
                   TwitterTitle, TwitterDescription, TwitterImage, AnswerCapsule, KeyFactsJson, FaqJson, HowToJson, RoundupJson, CustomFieldsJson
            FROM Posts WHERE Status IN @Statuses
            ORDER BY COALESCE(PublishedAt, CreatedAt), Id", Args(o));

        await foreach (var r in rows.WithCancellation(ct))
        {
            yield return new ExportPost
            {
                Id = r.Id, Title = r.Title ?? "", Slug = r.Slug ?? "", Html = r.Html ?? "",
                Status = Enum.TryParse<PostStatus>(r.Status, true, out var s) ? s : PostStatus.Draft,
                CreatedAtUtc = r.CreatedAt, UpdatedAtUtc = r.UpdatedAt, PublishedAtUtc = r.PublishedAt, ScheduledAtUtc = r.ScheduledAt,
                AuthorId = r.AuthorId, FeatureImage = r.FeatureImage, AllowComments = r.AllowComments ?? true,
                Seo = new ExportSeo(r.MetaTitle, r.MetaDescription, r.CanonicalUrl, r.OgTitle, r.OgDescription, r.OgImage,
                    r.TwitterTitle, r.TwitterDescription, r.TwitterImage),
                AnswerCapsule = r.AnswerCapsule, KeyFactsJson = r.KeyFactsJson, FaqJson = r.FaqJson,
                HowToJson = r.HowToJson, RoundupJson = r.RoundupJson, CustomFieldsJson = r.CustomFieldsJson,
                CategoryIds = cats.GetValueOrDefault(r.Id) ?? new(),
                TagIds = tags.GetValueOrDefault(r.Id) ?? new(),
                Series = series.GetValueOrDefault(r.Id) ?? new(),
                Comments = comments.GetValueOrDefault(r.Id) ?? new(),
            };
        }
    }

    public async IAsyncEnumerable<ExportPage> StreamPagesAsync(ExportOptions o, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!o.IncludePages) yield break;
        await using var sql = _ctx.CreateSqlConnection();
        await sql.OpenAsync(ct);
        var rows = sql.QueryUnbufferedAsync<PageRow>($@"
            SELECT p.Id, p.ParentId, p.Title, p.Slug, p.Content, p.IsPublished, p.SortOrder, p.CreatedAt, p.UpdatedAt, p.PublishedAt,
                   p.AuthorId, m.Url AS FeatureImage, p.MetaTitle, p.MetaDescription, p.CanonicalUrl, p.OgTitle, p.OgDescription,
                   p.OgImage, p.TwitterTitle, p.TwitterDescription, p.TwitterImage, p.CustomFieldsJson
            FROM Pages p LEFT JOIN Media m ON m.Id = p.FeaturedImageId
            WHERE {PageFilter.Replace("IsPublished", "p.IsPublished")}
            ORDER BY CASE WHEN p.ParentId IS NULL THEN 0 ELSE 1 END, p.SortOrder, p.Title", Args(o));
        await foreach (var r in rows.WithCancellation(ct))
            yield return new ExportPage
            {
                Id = r.Id, ParentId = r.ParentId, Title = r.Title ?? "", Slug = r.Slug ?? "", Html = r.Content ?? "",
                IsPublished = r.IsPublished, SortOrder = r.SortOrder, CreatedAtUtc = r.CreatedAt, UpdatedAtUtc = r.UpdatedAt,
                PublishedAtUtc = r.PublishedAt, AuthorId = r.AuthorId, FeatureImage = r.FeatureImage,
                Seo = new ExportSeo(r.MetaTitle, r.MetaDescription, r.CanonicalUrl, r.OgTitle, r.OgDescription, r.OgImage,
                    r.TwitterTitle, r.TwitterDescription, r.TwitterImage),
                CustomFieldsJson = r.CustomFieldsJson,
            };
    }

    // ── Single items, for a push (ADR-0029) ──────────────────────────────────

    public async Task<IReadOnlyList<Blog.Core.Services.Export.Push.ExportItemRef>> ListItemsAsync(ExportOptions o)
    {
        using var conn = _ctx.CreateConnection();
        var pages = o.IncludePages
            ? await conn.QueryAsync<(Guid Id, Guid? ParentId, string? Title, string? Slug)>($@"
                SELECT Id, ParentId, Title, Slug FROM Pages WHERE {PageFilter}
                ORDER BY CASE WHEN ParentId IS NULL THEN 0 ELSE 1 END, SortOrder, Title", Args(o))
            : Enumerable.Empty<(Guid Id, Guid? ParentId, string? Title, string? Slug)>();
        var posts = await conn.QueryAsync<(Guid Id, string? Title, string? Slug)>(@"
            SELECT Id, Title, Slug FROM Posts WHERE Status IN @Statuses
            ORDER BY COALESCE(PublishedAt, CreatedAt), Id", Args(o));
        return pages.Select(p => new Blog.Core.Services.Export.Push.ExportItemRef(
                    Blog.Core.Services.Export.Push.PushItemKind.Page, p.Id, p.ParentId, p.Title ?? "", p.Slug ?? ""))
            .Concat(posts.Select(p => new Blog.Core.Services.Export.Push.ExportItemRef(
                    Blog.Core.Services.Export.Push.PushItemKind.Post, p.Id, null, p.Title ?? "", p.Slug ?? "")))
            .ToList();
    }

    public async Task<ExportPost?> GetPostAsync(Guid id)
    {
        using var conn = _ctx.CreateConnection();
        var r = await conn.QuerySingleOrDefaultAsync<PostRow>(@"
            SELECT Id, Title, Slug, Html, Status, CreatedAt, UpdatedAt, PublishedAt, ScheduledAt, AuthorId, FeatureImage,
                   AllowComments, MetaTitle, MetaDescription, CanonicalUrl, OgTitle, OgDescription, OgImage,
                   TwitterTitle, TwitterDescription, TwitterImage, AnswerCapsule, KeyFactsJson, FaqJson, HowToJson, RoundupJson, CustomFieldsJson
            FROM Posts WHERE Id = @id", new { id });
        if (r is null) return null;
        var cats = (await conn.QueryAsync<Guid>("SELECT CategoryId FROM PostCategories WHERE PostId = @id", new { id })).ToList();
        var tags = (await conn.QueryAsync<Guid>("SELECT TagId FROM PostTags WHERE PostId = @id ORDER BY SortOrder", new { id })).ToList();
        var series = (await conn.QueryAsync<(string Title, string Slug, long Part)>(@"
            SELECT x.Title, x.Slug, x.Part FROM (
                SELECT sp.PostId, s.Title, s.Slug, ROW_NUMBER() OVER (PARTITION BY sp.SeriesId ORDER BY sp.SortOrder) AS Part
                FROM SeriesPosts sp JOIN Series s ON s.Id = sp.SeriesId
                WHERE sp.SeriesId IN (SELECT SeriesId FROM SeriesPosts WHERE PostId = @id)) x
            WHERE x.PostId = @id", new { id }))
            .Select(x => new ExportSeriesRef(x.Title, x.Slug, (int)x.Part)).ToList();
        return new ExportPost
        {
            Id = r.Id, Title = r.Title ?? "", Slug = r.Slug ?? "", Html = r.Html ?? "",
            Status = Enum.TryParse<PostStatus>(r.Status, true, out var s) ? s : PostStatus.Draft,
            CreatedAtUtc = r.CreatedAt, UpdatedAtUtc = r.UpdatedAt, PublishedAtUtc = r.PublishedAt, ScheduledAtUtc = r.ScheduledAt,
            AuthorId = r.AuthorId, FeatureImage = r.FeatureImage, AllowComments = r.AllowComments ?? true,
            Seo = new ExportSeo(r.MetaTitle, r.MetaDescription, r.CanonicalUrl, r.OgTitle, r.OgDescription, r.OgImage,
                r.TwitterTitle, r.TwitterDescription, r.TwitterImage),
            AnswerCapsule = r.AnswerCapsule, KeyFactsJson = r.KeyFactsJson, FaqJson = r.FaqJson,
            HowToJson = r.HowToJson, RoundupJson = r.RoundupJson, CustomFieldsJson = r.CustomFieldsJson,
            CategoryIds = cats, TagIds = tags, Series = series,
        };
    }

    public async Task<ExportPage?> GetPageAsync(Guid id)
    {
        using var conn = _ctx.CreateConnection();
        var r = await conn.QuerySingleOrDefaultAsync<PageRow>(@"
            SELECT p.Id, p.ParentId, p.Title, p.Slug, p.Content, p.IsPublished, p.SortOrder, p.CreatedAt, p.UpdatedAt, p.PublishedAt,
                   p.AuthorId, m.Url AS FeatureImage, p.MetaTitle, p.MetaDescription, p.CanonicalUrl, p.OgTitle, p.OgDescription,
                   p.OgImage, p.TwitterTitle, p.TwitterDescription, p.TwitterImage, p.CustomFieldsJson
            FROM Pages p LEFT JOIN Media m ON m.Id = p.FeaturedImageId
            WHERE p.Id = @id", new { id });
        return r is null ? null : new ExportPage
        {
            Id = r.Id, ParentId = r.ParentId, Title = r.Title ?? "", Slug = r.Slug ?? "", Html = r.Content ?? "",
            IsPublished = r.IsPublished, SortOrder = r.SortOrder, CreatedAtUtc = r.CreatedAt, UpdatedAtUtc = r.UpdatedAt,
            PublishedAtUtc = r.PublishedAt, AuthorId = r.AuthorId, FeatureImage = r.FeatureImage,
            Seo = new ExportSeo(r.MetaTitle, r.MetaDescription, r.CanonicalUrl, r.OgTitle, r.OgDescription, r.OgImage,
                r.TwitterTitle, r.TwitterDescription, r.TwitterImage),
            CustomFieldsJson = r.CustomFieldsJson,
        };
    }

    private sealed class AuthorRow
    {
        public Guid Id { get; set; }
        public string? Username { get; set; }
        public string? DisplayName { get; set; }
        public string? Slug { get; set; }
        public string? Email { get; set; }
        public string? Bio { get; set; }
        public string? Website { get; set; }
        public string? ProfileImage { get; set; }
        public string? Role { get; set; }
    }

    private sealed class CommentRow
    {
        public Guid Id { get; set; }
        public Guid PostId { get; set; }
        public Guid? ParentId { get; set; }
        public string? AuthorName { get; set; }
        public string? AuthorEmail { get; set; }
        public string? AuthorUrl { get; set; }
        public string? Content { get; set; }
        public string? Html { get; set; }
        public string? Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private class SeoRow
    {
        public string? MetaTitle { get; set; }
        public string? MetaDescription { get; set; }
        public string? CanonicalUrl { get; set; }
        public string? OgTitle { get; set; }
        public string? OgDescription { get; set; }
        public string? OgImage { get; set; }
        public string? TwitterTitle { get; set; }
        public string? TwitterDescription { get; set; }
        public string? TwitterImage { get; set; }
        public string? CustomFieldsJson { get; set; }
    }

    private sealed class PostRow : SeoRow
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? Html { get; set; }
        public string? Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }
        public DateTime? ScheduledAt { get; set; }
        public Guid AuthorId { get; set; }
        public string? FeatureImage { get; set; }
        public bool? AllowComments { get; set; }
        public string? AnswerCapsule { get; set; }
        public string? KeyFactsJson { get; set; }
        public string? FaqJson { get; set; }
        public string? HowToJson { get; set; }
        public string? RoundupJson { get; set; }
    }

    private sealed class PageRow : SeoRow
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? Content { get; set; }
        public bool IsPublished { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }
        public Guid AuthorId { get; set; }
        public string? FeatureImage { get; set; }
    }
}
