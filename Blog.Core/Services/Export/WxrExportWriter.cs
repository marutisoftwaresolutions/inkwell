using System.Globalization;
using System.Text;
using System.Xml;
using Blog.Core.Domain;

namespace Blog.Core.Services.Export;

/// <summary>
/// Writes a WordPress eXtended RSS (WXR 1.2) file: the format WordPress's own Tools → Import →
/// WordPress reads (wordpress-importer, <c>class-wxr-parser-simplexml.php</c>, checked 2026-10-05).
/// Streams through <see cref="XmlWriter"/>; only taxonomy and authors are held in memory.
///
/// Images become attachment items whose <c>wp:attachment_url</c> is the live URL on this site, and body
/// references are made absolute to match, so WordPress downloads each file and rewrites the body when
/// the operator ticks "Download and import file attachments". The feature image is linked through
/// <c>_thumbnail_id</c>, which the importer remaps.
/// </summary>
public static class WxrExportWriter
{
    public const string WxrVersion = "1.2";
    /// <summary>Attachment ids start here so they never collide with post, page, term or comment ids.</summary>
    public const int AttachmentIdBase = 1_000_000;
    private const string NsWp = "http://wordpress.org/export/1.2/";
    private const string NsExcerpt = "http://wordpress.org/export/1.2/excerpt/";
    private const string NsContent = "http://purl.org/rss/1.0/modules/content/";
    private const string NsWfw = "http://wellformedweb.org/CommentAPI/";
    private const string NsDc = "http://purl.org/dc/elements/1.1/";

    public static async Task<ExportResult> WriteAsync(Stream output, ExportSite site, ExportCatalog catalog,
        IAsyncEnumerable<ExportPost> posts, IAsyncEnumerable<ExportPage>? pages, ExportOptions options, CancellationToken ct)
    {
        var result = new ExportResult();
        var settings = new XmlWriterSettings
        {
            Async = true, Indent = true, Encoding = new UTF8Encoding(false), CheckCharacters = true,
        };
        var origin = site.BaseUrl.TrimEnd('/');
        var ids = new IdMap();
        var authors = catalog.Authors.ToDictionary(a => a.Id);
        var categories = catalog.Categories.ToDictionary(c => c.Id);
        var tags = catalog.Tags.ToDictionary(t => t.Id);
        // Attachment ids come from their own range and are assigned the first time an image is seen, so a
        // post can name its feature image (_thumbnail_id) before the attachment item is written at the end.
        var attachments = new Dictionary<string, (int Id, int Parent)>(StringComparer.Ordinal);
        var nextAttachment = AttachmentIdBase;
        int AttachmentFor(string path, int parent)
        {
            if (!attachments.TryGetValue(path, out var a)) attachments[path] = a = (++nextAttachment, parent);
            return a.Id;
        }

        await using var w = XmlWriter.Create(output, settings);
        await w.WriteStartDocumentAsync();
        w.WriteComment(" Exported from Inkwell. Import with Tools → Import → WordPress, and tick \"Download and import file attachments\". ");
        w.WriteStartElement("rss");
        w.WriteAttributeString("version", "2.0");
        w.WriteAttributeString("xmlns", "excerpt", null, NsExcerpt);
        w.WriteAttributeString("xmlns", "content", null, NsContent);
        w.WriteAttributeString("xmlns", "wfw", null, NsWfw);
        w.WriteAttributeString("xmlns", "dc", null, NsDc);
        w.WriteAttributeString("xmlns", "wp", null, NsWp);
        w.WriteStartElement("channel");
        w.WriteElementString("title", Clean(site.Title));
        w.WriteElementString("link", origin);
        w.WriteElementString("description", Clean(site.Description));
        w.WriteElementString("pubDate", DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture));
        w.WriteElementString("language", site.Language);
        w.WriteElementString("wxr_version", NsWp, WxrVersion);
        w.WriteElementString("base_site_url", NsWp, origin);
        w.WriteElementString("base_blog_url", NsWp, origin);

        foreach (var a in catalog.Authors)
        {
            w.WriteStartElement("wp", "author", NsWp);
            w.WriteElementString("author_id", NsWp, ids.For(a.Id).ToString(CultureInfo.InvariantCulture));
            Cdata(w, "author_login", a.Login);
            Cdata(w, "author_email", options.IncludeEmails ? a.Email ?? "" : "");
            Cdata(w, "author_display_name", a.DisplayName);
            Cdata(w, "author_first_name", "");
            Cdata(w, "author_last_name", "");
            w.WriteEndElement();
        }
        foreach (var c in catalog.Categories)
        {
            w.WriteStartElement("wp", "category", NsWp);
            w.WriteElementString("term_id", NsWp, ids.For(c.Id).ToString(CultureInfo.InvariantCulture));
            Cdata(w, "category_nicename", c.Slug);
            Cdata(w, "category_parent", "");
            Cdata(w, "cat_name", c.Name);
            w.WriteEndElement();
        }
        foreach (var t in catalog.Tags)
        {
            w.WriteStartElement("wp", "tag", NsWp);
            w.WriteElementString("term_id", NsWp, ids.For(t.Id).ToString(CultureInfo.InvariantCulture));
            Cdata(w, "tag_slug", t.Slug);
            Cdata(w, "tag_name", t.Name);
            w.WriteEndElement();
        }
        w.WriteElementString("generator", "Inkwell");

        await foreach (var p in posts.WithCancellation(ct))
        {
            var id = ids.For(p.Id);
            var found = new SortedSet<string>(StringComparer.Ordinal);
            var body = ExportHtml.RewriteUploads(ExportHtml.PostBody(p), origin, path => origin + path, found);
            var feature = ExportHtml.UploadPathOf(p.FeatureImage, origin);
            if (feature is not null) found.Add(feature);
            foreach (var path in found) { result.UploadPaths.Add(path); AttachmentFor(path, id); }

            var (status, dateUtc) = StatusOf(p);
            w.WriteStartElement("item");
            ItemHeader(w, p.Title, origin + "/" + p.Slug, authors.GetValueOrDefault(p.AuthorId)?.Login ?? "");
            Cdata(w, "encoded", NsContent, body);
            Cdata(w, "encoded", NsExcerpt, "");
            ItemCore(w, id, dateUtc, site.TimeZone, p.AllowComments ? "open" : "closed", p.Slug, status, 0, 0, "post");

            foreach (var cid in p.CategoryIds)
                if (categories.TryGetValue(cid, out var c)) Term(w, "category", c.Slug, c.Name);
            foreach (var tid in p.TagIds)
                if (tags.TryGetValue(tid, out var t)) Term(w, "post_tag", t.Slug, t.Name);
            foreach (var s in p.Series)
            {
                var slug = "series-" + s.Slug;
                Term(w, "post_tag", slug, "Series: " + s.Title);
            }

            if (feature is not null) Meta(w, "_thumbnail_id", AttachmentFor(feature, id).ToString(CultureInfo.InvariantCulture));
            SeoMeta(w, p.Seo, origin);
            RawMeta(w, "_inkwell_roundup", p.RoundupJson);
            RawMeta(w, "_inkwell_faq", p.FaqJson);
            RawMeta(w, "_inkwell_howto", p.HowToJson);
            RawMeta(w, "_inkwell_key_facts", p.KeyFactsJson);
            RawMeta(w, "_inkwell_answer_capsule", p.AnswerCapsule);
            CustomFieldMeta(w, p.CustomFieldsJson);
            if (p.Series.Count > 0)
                RawMeta(w, "_inkwell_series", string.Join(";", p.Series.Select(s => $"{s.Slug}#{s.Part}")));

            if (options.IncludeComments)
                foreach (var c in p.Comments)
                {
                    w.WriteStartElement("wp", "comment", NsWp);
                    w.WriteElementString("comment_id", NsWp, ids.For(c.Id).ToString(CultureInfo.InvariantCulture));
                    Cdata(w, "comment_author", c.AuthorName);
                    Cdata(w, "comment_author_email", options.IncludeEmails ? c.AuthorEmail ?? "" : "");
                    Cdata(w, "comment_author_url", c.AuthorUrl ?? "");
                    Cdata(w, "comment_author_IP", "");
                    w.WriteElementString("comment_date", NsWp, Local(c.CreatedAtUtc, site.TimeZone));
                    w.WriteElementString("comment_date_gmt", NsWp, Gmt(c.CreatedAtUtc));
                    Cdata(w, "comment_content", c.Html);
                    w.WriteElementString("comment_approved", NsWp, c.Approved ? "1" : "0");
                    Cdata(w, "comment_type", "comment");
                    w.WriteElementString("comment_parent", NsWp,
                        (c.ParentId is Guid pid ? ids.For(pid) : 0).ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("comment_user_id", NsWp, "0");
                    w.WriteEndElement();
                    result.Comments++;
                }

            w.WriteEndElement();   // item
            result.Posts++;
            await w.FlushAsync();
        }

        if (pages is not null)
        {
            await foreach (var pg in pages.WithCancellation(ct))
            {
                var id = ids.For(pg.Id);
                var found = new SortedSet<string>(StringComparer.Ordinal);
                var body = ExportHtml.RewriteUploads(pg.Html, origin, path => origin + path, found);
                var feature = ExportHtml.UploadPathOf(pg.FeatureImage, origin);
                if (feature is not null) found.Add(feature);
                foreach (var path in found) { result.UploadPaths.Add(path); AttachmentFor(path, id); }

                w.WriteStartElement("item");
                ItemHeader(w, pg.Title, origin + "/" + pg.Slug, authors.GetValueOrDefault(pg.AuthorId)?.Login ?? "");
                Cdata(w, "encoded", NsContent, body);
                Cdata(w, "encoded", NsExcerpt, "");
                ItemCore(w, id, pg.PublishedAtUtc ?? pg.CreatedAtUtc, site.TimeZone, "closed", pg.Slug,
                    pg.IsPublished ? "publish" : "draft", pg.ParentId is Guid parent ? ids.For(parent) : 0, pg.SortOrder, "page");
                if (feature is not null) Meta(w, "_thumbnail_id", AttachmentFor(feature, id).ToString(CultureInfo.InvariantCulture));
                SeoMeta(w, pg.Seo, origin);
                CustomFieldMeta(w, pg.CustomFieldsJson);
                w.WriteEndElement();
                result.Pages++;
                await w.FlushAsync();
            }
        }

        // One attachment per image, parented to the first item that uses it.
        foreach (var (path, (id, parent)) in attachments.OrderBy(a => a.Value.Id))
        {
            catalog.MediaByUrl.TryGetValue(path, out var media);
            var name = Path.GetFileNameWithoutExtension(path);
            w.WriteStartElement("item");
            ItemHeader(w, name, origin + path, "");
            Cdata(w, "encoded", NsContent, "");
            Cdata(w, "encoded", NsExcerpt, media.Caption ?? "");
            ItemCore(w, id, DateTime.UtcNow, site.TimeZone, "closed", Slugish(name), "inherit", parent, 0, "attachment");
            w.WriteElementString("attachment_url", NsWp, origin + path);
            if (!string.IsNullOrWhiteSpace(media.Alt)) Meta(w, "_wp_attachment_image_alt", media.Alt!);
            w.WriteEndElement();
            result.Attachments++;
        }

        w.WriteEndElement();   // channel
        w.WriteEndElement();   // rss
        await w.WriteEndDocumentAsync();
        await w.FlushAsync();

        return result;
    }

    // ── Pieces ──────────────────────────────────────────────────────────────────

    private static (string Status, DateTime DateUtc) StatusOf(ExportPost p) => p.Status switch
    {
        PostStatus.Published => ("publish", p.PublishedAtUtc ?? p.CreatedAtUtc),
        PostStatus.Scheduled => ("future", p.ScheduledAtUtc ?? p.PublishedAtUtc ?? p.CreatedAtUtc),
        PostStatus.InReview => ("pending", p.UpdatedAtUtc),
        _ => ("draft", p.UpdatedAtUtc),
    };

    private static void ItemHeader(XmlWriter w, string title, string link, string creatorLogin)
    {
        w.WriteElementString("title", Clean(title));
        w.WriteElementString("link", link);
        w.WriteElementString("pubDate", "");
        Cdata(w, "creator", NsDc, creatorLogin);
        w.WriteStartElement("guid");
        w.WriteAttributeString("isPermaLink", "false");
        w.WriteString(link);
        w.WriteEndElement();
        w.WriteElementString("description", "");
    }

    private static void ItemCore(XmlWriter w, int id, DateTime dateUtc, TimeZoneInfo tz, string commentStatus,
        string slug, string status, int parent, int menuOrder, string type)
    {
        w.WriteElementString("post_id", NsWp, id.ToString(CultureInfo.InvariantCulture));
        Cdata(w, "post_date", Local(dateUtc, tz));
        Cdata(w, "post_date_gmt", Gmt(dateUtc));
        Cdata(w, "comment_status", commentStatus);
        Cdata(w, "ping_status", "closed");
        Cdata(w, "post_name", slug);
        Cdata(w, "status", status);
        w.WriteElementString("post_parent", NsWp, parent.ToString(CultureInfo.InvariantCulture));
        w.WriteElementString("menu_order", NsWp, menuOrder.ToString(CultureInfo.InvariantCulture));
        Cdata(w, "post_type", type);
        Cdata(w, "post_password", "");
        w.WriteElementString("is_sticky", NsWp, "0");
    }

    private static void Term(XmlWriter w, string domain, string slug, string name)
    {
        w.WriteStartElement("category");
        w.WriteAttributeString("domain", domain);
        w.WriteAttributeString("nicename", slug);
        WriteCdataSafe(w, Clean(name));
        w.WriteEndElement();
    }

    /// <summary>
    /// Search and social fields for both common SEO plugins. Each plugin reads only its own keys, so
    /// emitting both is harmless; without either plugin they sit unused in post meta.
    /// </summary>
    private static void SeoMeta(XmlWriter w, ExportSeo s, string origin)
    {
        string? Abs(string? url) => string.IsNullOrWhiteSpace(url) ? url : url.StartsWith('/') ? origin + url : url;
        void Both(string yoast, string rankMath, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            Meta(w, yoast, value);
            Meta(w, rankMath, value);
        }
        Both("_yoast_wpseo_title", "rank_math_title", s.MetaTitle);
        Both("_yoast_wpseo_metadesc", "rank_math_description", s.MetaDescription);
        Both("_yoast_wpseo_canonical", "rank_math_canonical_url", Abs(s.CanonicalUrl));
        Both("_yoast_wpseo_opengraph-title", "rank_math_facebook_title", s.OgTitle);
        Both("_yoast_wpseo_opengraph-description", "rank_math_facebook_description", s.OgDescription);
        Both("_yoast_wpseo_opengraph-image", "rank_math_facebook_image", Abs(s.OgImage));
        Both("_yoast_wpseo_twitter-title", "rank_math_twitter_title", s.TwitterTitle);
        Both("_yoast_wpseo_twitter-description", "rank_math_twitter_description", s.TwitterDescription);
        Both("_yoast_wpseo_twitter-image", "rank_math_twitter_image", Abs(s.TwitterImage));
    }

    // Custom field values (ADR-0025) become WordPress custom fields under their own keys, which WordPress
    // lists in the editor's Custom Fields box. Keys never start with an underscore, so none is hidden.
    private static void CustomFieldMeta(XmlWriter w, string? json)
    {
        foreach (var (key, value) in CustomFields.Parse(json).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            Meta(w, key, value);
    }

    private static void RawMeta(XmlWriter w, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Meta(w, key, value);
    }

    private static void Meta(XmlWriter w, string key, string value)
    {
        w.WriteStartElement("wp", "postmeta", NsWp);
        Cdata(w, "meta_key", key);
        Cdata(w, "meta_value", value);
        w.WriteEndElement();
    }

    private static void Cdata(XmlWriter w, string localName, string? value) => Cdata(w, localName, NsWp, value);

    private static void Cdata(XmlWriter w, string localName, string ns, string? value)
    {
        var prefix = ns == NsWp ? "wp" : ns == NsContent ? "content" : ns == NsExcerpt ? "excerpt" : ns == NsDc ? "dc" : null;
        w.WriteStartElement(prefix, localName, ns);
        WriteCdataSafe(w, Clean(value ?? ""));
        w.WriteEndElement();
    }

    /// <summary>CDATA cannot contain <c>]]&gt;</c>; split it across sections, as WordPress's exporter does.</summary>
    internal static void WriteCdataSafe(XmlWriter w, string text)
    {
        var parts = text.Split("]]>");
        for (var i = 0; i < parts.Length; i++)
        {
            var seg = (i > 0 ? ">" : "") + parts[i] + (i < parts.Length - 1 ? "]]" : "");
            w.WriteCData(seg);
        }
    }

    /// <summary>Drops characters XML 1.0 cannot carry (control characters other than tab and newlines).</summary>
    internal static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[++i]); continue; }
            if (char.IsSurrogate(c)) continue;
            if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c != 0xFFFE && c != 0xFFFF)) sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Local(DateTime utc, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Gmt(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Slugish(string s) =>
        new string(s.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');

    /// <summary>File-local integer ids, minted in first-seen order.</summary>
    private sealed class IdMap
    {
        private readonly Dictionary<Guid, int> _ids = new();
        private int _next;
        public int For(Guid g) => _ids.TryGetValue(g, out var id) ? id : _ids[g] = ++_next;
    }
}
