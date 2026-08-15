using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Blog.Core.Domain;

namespace Blog.Web.Services.Import;

/// <summary>
/// Parses a WordPress eXtended RSS (WXR) export. Streams the file element-by-element (each
/// &lt;item&gt;/&lt;wp:author&gt; is read into a small XElement) so multi-hundred-MB exports don't
/// blow memory. Matches elements by local name so it works across WXR 1.1/1.2.
/// </summary>
public class WordPressImporter : IContentImporter
{
    public ImportSource Source => ImportSource.WordPress;

    public ImportManifest Analyze(string filePath)
    {
        // Validate root: an <rss> document declaring the WordPress export namespace.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true };
        using var reader = XmlReader.Create(filePath, settings);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (!string.Equals(reader.LocalName, "rss", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Not a WordPress WXR export (root element is not <rss>).");
                var wp = reader.LookupNamespace("wp");
                if (string.IsNullOrEmpty(wp))
                    throw new InvalidDataException("Not a WordPress WXR export (missing the wp: namespace).");
                break;
            }
        }
        return new ImportManifest { Source = ImportSource.WordPress, DetectedFormat = "WordPress WXR" };
    }

    public IEnumerable<ParsedRecord> ReadRecords(string filePath)
    {
        // Pass A — collect the attachment id→URL map and author definitions (both needed before posts).
        var attachmentUrls = new Dictionary<string, string>();
        var authors = new List<ParsedAuthor>();
        foreach (var el in StreamElements(filePath, "item", "author"))
        {
            if (string.Equals(el.Name.LocalName, "author", StringComparison.OrdinalIgnoreCase))
            {
                var login = Val(el, "author_login");
                if (!string.IsNullOrWhiteSpace(login))
                    authors.Add(new ParsedAuthor
                    {
                        Login = login!,
                        DisplayName = Val(el, "author_display_name") ?? login!,
                        Email = Val(el, "author_email")
                    });
                continue;
            }
            if (string.Equals(Val(el, "post_type"), "attachment", StringComparison.OrdinalIgnoreCase))
            {
                var id = Val(el, "post_id");
                var url = Val(el, "attachment_url");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(url))
                    attachmentUrls[id!] = url!;
            }
        }

        foreach (var a in authors)
            yield return new ParsedRecord { Type = ImportItemType.Author, Ordinal = 0, SourceId = a.Login, Title = a.DisplayName, Payload = a };

        // Pass B — emit terms (deduped), images, posts/pages and comments.
        var seenTerms = new HashSet<string>();
        foreach (var el in StreamElements(filePath, "item"))
        {
            var type = (Val(el, "post_type") ?? "post").ToLowerInvariant();

            if (type == "attachment")
            {
                var url = Val(el, "attachment_url");
                if (!string.IsNullOrWhiteSpace(url))
                    yield return new ParsedRecord
                    {
                        Type = ImportItemType.Image, Ordinal = 3, SourceId = url!,
                        Title = Val(el, "title"),
                        Payload = new ParsedImage { Url = url!, Title = Val(el, "title") }
                    };
                continue;
            }

            if (type != "post" && type != "page") continue; // skip nav_menu_item, revision, custom types

            // Terms from this item's <category domain="…"> elements.
            var cats = new List<string>();
            var tags = new List<string>();
            foreach (var c in el.Elements().Where(e => e.Name.LocalName == "category"))
            {
                var domain = (string?)c.Attribute("domain") ?? "";
                var slug = (string?)c.Attribute("nicename");
                var name = c.Value?.Trim();
                if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(name)) continue;
                var isTag = domain.Equals("post_tag", StringComparison.OrdinalIgnoreCase);
                if (isTag) tags.Add(slug!); else if (domain.Equals("category", StringComparison.OrdinalIgnoreCase)) cats.Add(slug!);

                var key = (isTag ? "t:" : "c:") + slug;
                if (seenTerms.Add(key))
                    yield return new ParsedRecord
                    {
                        Type = isTag ? ImportItemType.Tag : ImportItemType.Category,
                        Ordinal = isTag ? 2 : 1, SourceId = slug!, Title = name,
                        Payload = new ParsedTerm { Name = name!, Slug = slug!, IsTag = isTag }
                    };
            }

            var postId = Val(el, "post_id") ?? Guid.NewGuid().ToString();
            var isPage = type == "page";

            // Resolve featured image via _thumbnail_id postmeta → attachment URL; grab Yoast SEO meta.
            string? thumbUrl = null, metaTitle = null, metaDesc = null;
            foreach (var pm in el.Elements().Where(e => e.Name.LocalName == "postmeta"))
            {
                var key = Val(pm, "meta_key");
                var value = Val(pm, "meta_value");
                if (string.IsNullOrEmpty(key)) continue;
                if (key == "_thumbnail_id" && value != null && attachmentUrls.TryGetValue(value, out var u)) thumbUrl = u;
                else if (key == "_yoast_wpseo_title") metaTitle = value;
                else if (key == "_yoast_wpseo_metadesc") metaDesc = value;
            }

            var post = new ParsedPost
            {
                Title = Val(el, "title") ?? "(untitled)",
                Slug = Val(el, "post_name"),
                Html = Val(el, "encoded", "content"),
                Excerpt = Val(el, "encoded", "excerpt"),
                Status = (Val(el, "status") ?? "publish").ToLowerInvariant(),
                PublishedAt = ParseWpDate(Val(el, "post_date_gmt") ?? Val(el, "post_date")),
                AuthorLogin = Val(el, "creator"),
                OriginalLink = Val(el, "link"),
                CategorySlugs = cats,
                TagSlugs = tags,
                FeatureImageUrl = thumbUrl,
                MetaTitle = metaTitle,
                MetaDescription = metaDesc,
                IsPage = isPage
            };
            yield return new ParsedRecord
            {
                Type = isPage ? ImportItemType.Page : ImportItemType.Post,
                Ordinal = isPage ? 5 : 4, SourceId = postId, Title = post.Title, Payload = post
            };

            // Comments (Ordinal 6 → processed after posts so the parent post exists).
            var ci = 0;
            foreach (var cm in el.Elements().Where(e => e.Name.LocalName == "comment"))
            {
                var content = Val(cm, "comment_content");
                if (string.IsNullOrWhiteSpace(content)) continue;
                var commentId = Val(cm, "comment_id") ?? $"{postId}-{ci++}";
                yield return new ParsedRecord
                {
                    Type = ImportItemType.Comment, Ordinal = 6, SourceId = $"{postId}:{commentId}",
                    Title = Val(cm, "comment_author"),
                    Payload = new ParsedComment
                    {
                        PostSourceId = postId,
                        AuthorName = Val(cm, "comment_author"),
                        AuthorEmail = Val(cm, "comment_author_email"),
                        AuthorUrl = Val(cm, "comment_author_url"),
                        Content = content,
                        Date = ParseWpDate(Val(cm, "comment_date_gmt") ?? Val(cm, "comment_date")),
                        Approved = Val(cm, "comment_approved") == "1"
                    }
                };
            }
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<XElement> StreamElements(string filePath, params string[] localNames)
    {
        var wanted = new HashSet<string>(localNames, StringComparer.OrdinalIgnoreCase);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true, IgnoreWhitespace = true };
        using var reader = XmlReader.Create(filePath, settings);
        reader.MoveToContent();
        while (!reader.EOF)
        {
            // XNode.ReadFrom already advances past the matched element, so only Read() when we didn't
            // consume one — otherwise we'd skip the following sibling.
            if (reader.NodeType == XmlNodeType.Element && wanted.Contains(reader.LocalName))
            {
                if (XNode.ReadFrom(reader) is XElement el)
                    yield return el;
            }
            else
            {
                reader.Read();
            }
        }
    }

    /// <summary>First child element with the given local name (optionally whose namespace URI contains <paramref name="nsContains"/>).</summary>
    private static string? Val(XElement parent, string localName, string? nsContains = null)
    {
        var match = parent.Elements().FirstOrDefault(e =>
            e.Name.LocalName == localName &&
            (nsContains == null || e.Name.NamespaceName.Contains(nsContains, StringComparison.OrdinalIgnoreCase)));
        return match?.Value;
    }

    private static DateTime? ParseWpDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("0000")) return null;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return dt;
        if (DateTime.TryParseExact(raw, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dt))
            return dt;
        return null;
    }
}
