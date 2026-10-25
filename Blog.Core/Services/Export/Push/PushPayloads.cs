using System.Globalization;
using System.Text.Json.Nodes;
using Blog.Core.Domain;

namespace Blog.Core.Services.Export.Push;

/// <summary>A term a WordPress push needs: taxonomy (<c>categories</c> or <c>tags</c>), name and slug.</summary>
public sealed record PushTerm(string Taxonomy, string Name, string Slug)
{
    /// <summary>The push item key that records this term's id on the target.</summary>
    public string Key => (Taxonomy == "categories" ? "category:" : "tag:") + Slug;
}

/// <summary>A Ghost tag sent inline with a post: Ghost reuses a tag with the same slug and creates the rest.</summary>
public sealed record GhostTag(string Name, string Slug);

/// <summary>
/// The JSON bodies a push sends (ADR-0029), built from the same export records the file export writes, so
/// both paths carry the same content: Inkwell's blocks rendered by <see cref="ExportHtml.PostBody"/>,
/// categories as Ghost tags, series as a WordPress tag or a Ghost internal tag. Ghost's field lengths are
/// enforced exactly as <see cref="GhostExportWriter"/> does.
/// </summary>
public static class PushPayloads
{
    // ── Status ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The target status and date for a post. A schedule the target cannot honour (no time, or a time already
    /// past) becomes a draft rather than being published now or given an invented date.
    /// </summary>
    public static (string Status, DateTime? DateUtc) PostStatusFor(ExportPost p, ExportTarget target, DateTime nowUtc)
    {
        var wp = target == ExportTarget.WordPress;
        switch (p.Status)
        {
            case PostStatus.Published:
                return (wp ? "publish" : "published", p.PublishedAtUtc ?? p.CreatedAtUtc);
            case PostStatus.Scheduled:
                var at = p.ScheduledAtUtc ?? p.PublishedAtUtc;
                return at is DateTime t && t > nowUtc ? (wp ? "future" : "scheduled", t) : ("draft", null);
            case PostStatus.InReview when wp:
                return ("pending", null);
            default:
                return ("draft", null);
        }
    }

    /// <summary>The WordPress terms a post needs: its categories and tags, plus one tag per series it belongs to.</summary>
    public static IReadOnlyList<PushTerm> WordPressTerms(ExportPost p, ExportCatalog catalog)
    {
        var terms = new List<PushTerm>();
        var cats = catalog.Categories.ToDictionary(c => c.Id);
        var tags = catalog.Tags.ToDictionary(t => t.Id);
        foreach (var id in p.CategoryIds) if (cats.TryGetValue(id, out var c)) terms.Add(new PushTerm("categories", c.Name, c.Slug));
        foreach (var id in p.TagIds) if (tags.TryGetValue(id, out var t)) terms.Add(new PushTerm("tags", t.Name, t.Slug));
        foreach (var s in p.Series) terms.Add(new PushTerm("tags", "Series: " + s.Title, "series-" + s.Slug));
        return terms.DistinctBy(t => t.Key).ToList();
    }

    /// <summary>A post's Ghost tags: categories and tags merged by slug, then an internal <c>#series-…</c> tag per series.</summary>
    public static IReadOnlyList<GhostTag> GhostTags(ExportPost p, ExportCatalog catalog)
    {
        var tags = new List<GhostTag>();
        var cats = catalog.Categories.ToDictionary(c => c.Id);
        var tg = catalog.Tags.ToDictionary(t => t.Id);
        foreach (var id in p.CategoryIds) if (cats.TryGetValue(id, out var c)) tags.Add(new GhostTag(c.Name, c.Slug));
        foreach (var id in p.TagIds) if (tg.TryGetValue(id, out var t)) tags.Add(new GhostTag(t.Name, t.Slug));
        foreach (var s in p.Series) tags.Add(new GhostTag("#series-" + s.Slug, "hash-series-" + s.Slug));
        return tags.DistinctBy(t => t.Slug, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ── WordPress ────────────────────────────────────────────────────────────

    public static JsonObject WordPressPost(ExportPost p, string body, string? featuredMediaId,
        IEnumerable<string> categoryIds, IEnumerable<string> tagIds, DateTime nowUtc)
    {
        var (status, date) = PostStatusFor(p, ExportTarget.WordPress, nowUtc);
        var o = new JsonObject
        {
            ["title"] = p.Title,
            ["slug"] = p.Slug,
            ["content"] = body,
            ["status"] = status,
            ["comment_status"] = p.AllowComments ? "open" : "closed",
            ["categories"] = Ids(categoryIds),
            ["tags"] = Ids(tagIds),
        };
        if (date is DateTime d) o["date_gmt"] = WpDate(d);
        if (featuredMediaId is not null && int.TryParse(featuredMediaId, out var media)) o["featured_media"] = media;
        return o;
    }

    public static JsonObject WordPressPage(ExportPage pg, string body, string? featuredMediaId, string? parentId)
    {
        var o = new JsonObject
        {
            ["title"] = pg.Title,
            ["slug"] = pg.Slug,
            ["content"] = body,
            ["status"] = pg.IsPublished ? "publish" : "draft",
            ["menu_order"] = pg.SortOrder,
            ["parent"] = parentId is not null && int.TryParse(parentId, out var parent) ? parent : 0,
        };
        if (pg.IsPublished) o["date_gmt"] = WpDate(pg.PublishedAtUtc ?? pg.CreatedAtUtc);
        if (featuredMediaId is not null && int.TryParse(featuredMediaId, out var media)) o["featured_media"] = media;
        return o;
    }

    private static JsonArray Ids(IEnumerable<string> ids)
    {
        var a = new JsonArray();
        foreach (var id in ids.Distinct()) if (int.TryParse(id, out var n)) a.Add(n);
        return a;
    }

    /// <summary>WordPress reads <c>date_gmt</c> as a local-free ISO date and time without a zone suffix.</summary>
    public static string WpDate(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    // ── Ghost ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A Ghost post body (<c>{"posts":[…]}</c>) for the Admin API with <c>?source=html</c>. Pass
    /// <paramref name="updatedAt"/> when updating: Ghost refuses an update without the version it is replacing.
    /// </summary>
    public static JsonObject GhostPost(ExportPost p, string body, string? featureImage, string? featureAlt, string? featureCaption,
        IReadOnlyList<GhostTag> tags, string? ogImage, string? twitterImage, string? updatedAt, DateTime nowUtc, ref int truncated)
    {
        var (status, date) = PostStatusFor(p, ExportTarget.Ghost, nowUtc);
        var o = GhostCommon(p.Title, p.Slug, body, status, date, featureImage, featureAlt, featureCaption, p.Seo, ogImage, twitterImage, updatedAt, ref truncated);
        var t = new JsonArray();
        foreach (var tag in tags)
            t.Add(new JsonObject { ["name"] = ExportHtml.Limit(tag.Name, 191, ref truncated), ["slug"] = ExportHtml.Limit(tag.Slug, 191, ref truncated) });
        o["tags"] = t;
        return new JsonObject { ["posts"] = new JsonArray(o) };
    }

    /// <summary>A Ghost page body (<c>{"pages":[…]}</c>). Ghost pages have no parent, so the hierarchy is flattened.</summary>
    public static JsonObject GhostPage(ExportPage pg, string body, string? featureImage, string? featureAlt, string? featureCaption,
        string? ogImage, string? twitterImage, string? updatedAt, ref int truncated)
    {
        var o = GhostCommon(pg.Title, pg.Slug, body, pg.IsPublished ? "published" : "draft",
            pg.IsPublished ? pg.PublishedAtUtc ?? pg.CreatedAtUtc : null, featureImage, featureAlt, featureCaption, pg.Seo,
            ogImage, twitterImage, updatedAt, ref truncated);
        return new JsonObject { ["pages"] = new JsonArray(o) };
    }

    private static JsonObject GhostCommon(string title, string slug, string body, string status, DateTime? published,
        string? featureImage, string? featureAlt, string? featureCaption, ExportSeo seo, string? ogImage, string? twitterImage,
        string? updatedAt, ref int truncated)
    {
        var o = new JsonObject
        {
            ["title"] = ExportHtml.Limit(title, 255, ref truncated),
            ["slug"] = ExportHtml.Limit(slug, 191, ref truncated),
            ["html"] = body,
            ["status"] = status,
            ["visibility"] = "public",
        };
        if (published is DateTime d) o["published_at"] = DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        Set(o, "feature_image", featureImage);
        Set(o, "feature_image_alt", ExportHtml.Limit(featureAlt, 191, ref truncated));
        Set(o, "feature_image_caption", featureCaption);
        Set(o, "canonical_url", seo.CanonicalUrl);
        Set(o, "meta_title", ExportHtml.Limit(seo.MetaTitle, 300, ref truncated));
        Set(o, "meta_description", ExportHtml.Limit(seo.MetaDescription, 500, ref truncated));
        Set(o, "og_title", ExportHtml.Limit(seo.OgTitle, 300, ref truncated));
        Set(o, "og_description", ExportHtml.Limit(seo.OgDescription, 500, ref truncated));
        Set(o, "og_image", ogImage);
        Set(o, "twitter_title", ExportHtml.Limit(seo.TwitterTitle, 300, ref truncated));
        Set(o, "twitter_description", ExportHtml.Limit(seo.TwitterDescription, 500, ref truncated));
        Set(o, "twitter_image", twitterImage);
        Set(o, "updated_at", updatedAt);
        return o;
    }

    private static void Set(JsonObject o, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) o[name] = value;
    }
}
