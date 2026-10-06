using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Blog.Core.Domain;

namespace Blog.Core.Services.Export;

/// <summary>
/// Writes Ghost's import JSON (Settings → Import content), as documented at docs.ghost.org/migration/custom
/// and checked against Ghost's schema (<c>ghost/core/core/server/data/schema/schema.js</c>, main, 2026-10-05):
/// <list type="bullet">
/// <item>bodies as <c>html</c>, so Ghost does its own HTML → Lexical conversion;</item>
/// <item>search and social fields in <c>posts_meta</c> (where Ghost keeps them), <c>canonical_url</c> on the post;</item>
/// <item>categories become tags (Ghost has no categories) and series become internal <c>#series-…</c> tags;</item>
/// <item>images referenced as <c>/content/images/…</c>, the path the ZIP carries them under;</item>
/// <item>users only when emails are included: Ghost requires a valid, unique email per user.</item>
/// </list>
/// Lengths Ghost validates (title 255, custom excerpt 300, meta title 300, meta description 500,
/// og/twitter title 300 and description 500, image alt 191) are cut at a word and counted.
/// Ghost has no comment import, so comments are never written here.
/// </summary>
public static class GhostExportWriter
{
    /// <summary>The Ghost version the file declares; the importer reads the shape of this major.</summary>
    public const string DeclaredGhostVersion = "6.0.0";

    private static readonly JsonWriterOptions Options = new() { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static async Task<ExportResult> WriteAsync(Stream output, ExportSite site, ExportCatalog catalog,
        IAsyncEnumerable<ExportPost> posts, IAsyncEnumerable<ExportPage>? pages, ExportOptions options, CancellationToken ct)
    {
        var result = new ExportResult();
        var origin = site.BaseUrl.TrimEnd('/');
        var truncated = 0;

        // Tags: categories and tags merged by slug (a category and a tag with the same slug become one tag).
        var tagIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tagRows = new List<(int Id, string Name, string Slug)>();
        int TagFor(string name, string slug)
        {
            if (tagIds.TryGetValue(slug, out var id)) return id;
            id = tagRows.Count + 1;
            tagIds[slug] = id;
            tagRows.Add((id, name, slug));
            return id;
        }
        var bySlug = new Dictionary<Guid, int>();
        foreach (var c in catalog.Categories) bySlug[c.Id] = TagFor(c.Name, c.Slug);
        foreach (var t in catalog.Tags) bySlug[t.Id] = TagFor(t.Name, t.Slug);

        var includeUsers = options.IncludeEmails;
        var users = includeUsers
            ? catalog.Authors.Where(a => IsEmail(a.Email)).Select((a, i) => (Author: a, Id: i + 1)).ToDictionary(x => x.Author.Id, x => x)
            : new Dictionary<Guid, (ExportAuthor Author, int Id)>();

        var postsMeta = new List<(int PostId, ExportSeo Seo, string? Alt, string? Caption)>();
        var postsTags = new List<(int PostId, int TagId, int Order)>();
        var linked = new HashSet<(int, int)>();
        var postsAuthors = new List<(int PostId, int AuthorId)>();
        var nextPost = 0;

        await using var w = new Utf8JsonWriter(output, Options);
        w.WriteStartObject();
        w.WriteStartArray("db");
        w.WriteStartObject();
        w.WriteStartObject("meta");
        w.WriteNumber("exported_on", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        w.WriteString("version", DeclaredGhostVersion);
        w.WriteEndObject();
        w.WriteStartObject("data");
        w.WriteStartArray("posts");

        string Rewrite(string html, string? feature, out string? featureOut)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            var body = ExportHtml.RewriteUploads(html, origin, ExportHtml.GhostImagePath, found);
            var f = ExportHtml.UploadPathOf(feature, origin);
            if (f is not null) found.Add(f);
            foreach (var p in found) result.UploadPaths.Add(p);
            featureOut = f is not null ? ExportHtml.GhostImagePath(f) : (string.IsNullOrWhiteSpace(feature) ? null : feature);
            return body;
        }

        await foreach (var p in posts.WithCancellation(ct))
        {
            var id = ++nextPost;
            var html = Rewrite(ExportHtml.PostBody(p), p.FeatureImage, out var feature);
            var (status, published) = p.Status switch
            {
                PostStatus.Published => ("published", p.PublishedAtUtc ?? p.CreatedAtUtc),
                PostStatus.Scheduled => ("scheduled", p.ScheduledAtUtc ?? p.PublishedAtUtc ?? (DateTime?)null),
                _ => ("draft", (DateTime?)null),
            };
            // A schedule Ghost cannot honour (no time, or one already past) imports as a draft rather than invent a date.
            if (status == "scheduled" && (published is null || published <= DateTime.UtcNow)) { status = "draft"; published = null; }

            WritePost(w, id, p.Title, p.Slug, html, "post", status, p.CreatedAtUtc, p.UpdatedAtUtc, published, feature, p.Seo.CanonicalUrl, ref truncated);
            Media(catalog, p.FeatureImage, origin, out var alt, out var caption);
            postsMeta.Add((id, p.Seo, alt, caption));

            var order = 0;
            foreach (var tagId in p.CategoryIds.Concat(p.TagIds).Select(g => bySlug.TryGetValue(g, out var t) ? t : 0)
                         .Concat(p.Series.Select(s => TagFor("#series-" + s.Slug, "hash-series-" + s.Slug))))
                if (tagId > 0 && linked.Add((id, tagId))) postsTags.Add((id, tagId, order++));
            if (users.TryGetValue(p.AuthorId, out var u)) postsAuthors.Add((id, u.Id));

            result.Posts++;
            if (id % 50 == 0) await w.FlushAsync(ct);
        }

        if (pages is not null)
        {
            await foreach (var pg in pages.WithCancellation(ct))
            {
                var id = ++nextPost;
                var html = Rewrite(pg.Html, pg.FeatureImage, out var feature);
                WritePost(w, id, pg.Title, pg.Slug, html, "page", pg.IsPublished ? "published" : "draft",
                    pg.CreatedAtUtc, pg.UpdatedAtUtc, pg.IsPublished ? pg.PublishedAtUtc ?? pg.CreatedAtUtc : null,
                    feature, pg.Seo.CanonicalUrl, ref truncated);
                Media(catalog, pg.FeatureImage, origin, out var alt, out var caption);
                postsMeta.Add((id, pg.Seo, alt, caption));
                if (users.TryGetValue(pg.AuthorId, out var u)) postsAuthors.Add((id, u.Id));
                result.Pages++;
            }
        }
        w.WriteEndArray();   // posts

        w.WriteStartArray("posts_meta");
        foreach (var (postId, seo, alt, caption) in postsMeta)
        {
            if (seo == EmptySeo && alt is null && caption is null) continue;
            w.WriteStartObject();
            w.WriteNumber("post_id", postId);
            Str(w, "meta_title", ExportHtml.Limit(seo.MetaTitle, 300, ref truncated));
            Str(w, "meta_description", ExportHtml.Limit(seo.MetaDescription, 500, ref truncated));
            Str(w, "og_title", ExportHtml.Limit(seo.OgTitle, 300, ref truncated));
            Str(w, "og_description", ExportHtml.Limit(seo.OgDescription, 500, ref truncated));
            Str(w, "og_image", Image(seo.OgImage, origin, result));
            Str(w, "twitter_title", ExportHtml.Limit(seo.TwitterTitle, 300, ref truncated));
            Str(w, "twitter_description", ExportHtml.Limit(seo.TwitterDescription, 500, ref truncated));
            Str(w, "twitter_image", Image(seo.TwitterImage, origin, result));
            Str(w, "feature_image_alt", ExportHtml.Limit(alt, 191, ref truncated));
            Str(w, "feature_image_caption", caption);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("tags");
        foreach (var (id, name, slug) in tagRows)
        {
            w.WriteStartObject();
            w.WriteNumber("id", id);
            w.WriteString("name", ExportHtml.Limit(name, 191, ref truncated));
            w.WriteString("slug", ExportHtml.Limit(slug, 191, ref truncated));
            w.WriteString("visibility", name.StartsWith('#') ? "internal" : "public");
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("posts_tags");
        foreach (var (postId, tagId, order) in postsTags)
        {
            w.WriteStartObject();
            w.WriteNumber("post_id", postId);
            w.WriteNumber("tag_id", tagId);
            w.WriteNumber("sort_order", order);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        if (includeUsers)
        {
            w.WriteStartArray("users");
            foreach (var (a, id) in users.Values)
            {
                w.WriteStartObject();
                w.WriteNumber("id", id);
                w.WriteString("name", ExportHtml.Limit(a.DisplayName, 191, ref truncated));
                w.WriteString("slug", ExportHtml.Limit(a.Slug, 191, ref truncated));
                w.WriteString("email", a.Email);
                Str(w, "bio", a.Bio);
                Str(w, "website", a.Website);
                Str(w, "profile_image", Image(a.ProfileImage, origin, result));
                w.WriteStartArray("roles");
                w.WriteStringValue(GhostRole(a.Role));
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("posts_authors");
            foreach (var (postId, authorId) in postsAuthors)
            {
                w.WriteStartObject();
                w.WriteNumber("post_id", postId);
                w.WriteNumber("author_id", authorId);
                w.WriteNumber("sort_order", 0);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        w.WriteEndObject();   // data
        w.WriteEndObject();
        w.WriteEndArray();    // db
        w.WriteEndObject();
        await w.FlushAsync(ct);

        result.Truncated = truncated;
        return result;
    }

    private static readonly ExportSeo EmptySeo = new(null, null, null, null, null, null, null, null, null);

    private static void WritePost(Utf8JsonWriter w, int id, string title, string slug, string html, string type, string status,
        DateTime created, DateTime updated, DateTime? published, string? feature, string? canonical, ref int truncated)
    {
        w.WriteStartObject();
        w.WriteNumber("id", id);
        w.WriteString("title", ExportHtml.Limit(title, 255, ref truncated));
        w.WriteString("slug", ExportHtml.Limit(slug, 191, ref truncated));
        w.WriteString("html", html);
        w.WriteString("type", type);
        w.WriteString("status", status);
        w.WriteString("visibility", "public");
        Str(w, "feature_image", feature);
        Str(w, "canonical_url", canonical);
        w.WriteString("created_at", Iso(created));
        w.WriteString("updated_at", Iso(updated));
        if (published is DateTime p) w.WriteString("published_at", Iso(p)); else w.WriteNull("published_at");
        w.WriteEndObject();
    }

    private static void Media(ExportCatalog catalog, string? feature, string origin, out string? alt, out string? caption)
    {
        alt = caption = null;
        var path = ExportHtml.UploadPathOf(feature, origin);
        if (path is not null && catalog.MediaByUrl.TryGetValue(path, out var m))
        {
            alt = string.IsNullOrWhiteSpace(m.Alt) ? null : m.Alt;
            caption = string.IsNullOrWhiteSpace(m.Caption) ? null : m.Caption;
        }
    }

    private static string? Image(string? url, string origin, ExportResult result)
    {
        var path = ExportHtml.UploadPathOf(url, origin);
        if (path is null) return string.IsNullOrWhiteSpace(url) ? null : url;
        result.UploadPaths.Add(path);
        return ExportHtml.GhostImagePath(path);
    }

    private static void Str(Utf8JsonWriter w, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) w.WriteString(name, value);
    }

    private static string Iso(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    internal static string GhostRole(string role) => role switch
    {
        "Admin" => "Administrator",
        "Editor" => "Editor",
        "Contributor" => "Contributor",
        _ => "Author",
    };

    private static bool IsEmail(string? s) =>
        !string.IsNullOrWhiteSpace(s) && s.Contains('@') && s.IndexOf('@') > 0 && s.IndexOf('.', s.IndexOf('@')) > s.IndexOf('@') + 1;
}
