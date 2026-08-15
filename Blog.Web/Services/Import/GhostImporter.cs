using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Blog.Core.Domain;

namespace Blog.Web.Services.Import;

/// <summary>
/// Parses a Ghost JSON export (<c>db[0].data.{posts,tags,posts_tags,users,posts_authors,settings}</c>).
/// Accepts a raw <c>.json</c> or a <c>.zip</c> (the JSON entry is extracted). Post bodies use the
/// rendered <c>html</c> when present, otherwise a best-effort Lexical/Mobiledoc → HTML conversion
/// (rich formatting may be simplified — surfaced as a warning). Ghost <c>__GHOST_URL__</c> image
/// references are resolved to the export's site URL so they can be downloaded and localised.
///
/// LIMITATION: images that only exist in a separate <c>content/images</c> export folder (not fetchable
/// by URL) are not yet ingested — that (images-zip) is a follow-up. Ghost exports carry no comments.
/// </summary>
public class GhostImporter : IContentImporter
{
    public ImportSource Source => ImportSource.Ghost;

    public ImportManifest Analyze(string filePath)
    {
        using var doc = LoadDocument(filePath);
        if (!TryGetData(doc, out var data) || !data.TryGetProperty("posts", out _))
            throw new InvalidDataException("Not a Ghost export (missing db[0].data.posts).");

        var manifest = new ImportManifest { Source = ImportSource.Ghost, DetectedFormat = "Ghost JSON" };
        // Flag the content format so the operator knows fidelity may vary.
        if (data.TryGetProperty("posts", out var posts) && posts.ValueKind == JsonValueKind.Array)
        {
            var first = posts.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object)
            {
                var hasHtml = first.TryGetProperty("html", out var h) && h.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(h.GetString());
                if (!hasHtml) manifest.Warnings.Add("Posts have no rendered HTML — body converted from Lexical/Mobiledoc (formatting may be simplified).");
            }
        }
        return manifest;
    }

    public IEnumerable<ParsedRecord> ReadRecords(string filePath)
    {
        using var doc = LoadDocument(filePath);
        if (!TryGetData(doc, out var data)) yield break;

        var baseUrl = ResolveSiteUrl(data);

        // ── Tags (→ Inkwell tags), skipping Ghost internal tags (#hash / slug hash-*). ──
        var tagSlugById = new Dictionary<string, string>();
        if (data.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tags.EnumerateArray())
            {
                var id = Str(t, "id");
                var slug = Str(t, "slug");
                var name = Str(t, "name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(name)) continue;
                if (name!.StartsWith("#") || slug!.StartsWith("hash-")) continue; // internal tag
                tagSlugById[id!] = slug!;
                yield return new ParsedRecord
                {
                    Type = ImportItemType.Tag, Ordinal = 2, SourceId = slug!, Title = name,
                    Payload = new ParsedTerm { Name = name!, Slug = slug!, IsTag = true }
                };
            }
        }

        // post_id → [tag slug] (ordered by sort_order).
        var postTagSlugs = new Dictionary<string, List<string>>();
        if (data.TryGetProperty("posts_tags", out var pt) && pt.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in pt.EnumerateArray())
            {
                var postId = Str(row, "post_id");
                var tagId = Str(row, "tag_id");
                if (postId == null || tagId == null || !tagSlugById.TryGetValue(tagId, out var slug)) continue;
                if (!postTagSlugs.TryGetValue(postId, out var list)) postTagSlugs[postId] = list = new();
                list.Add(slug);
            }
        }

        // ── Authors (parsed for completeness; posts are attributed to the importing admin). ──
        if (data.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Array)
        {
            foreach (var u in users.EnumerateArray())
            {
                var slug = Str(u, "slug") ?? Str(u, "id");
                var name = Str(u, "name");
                if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(name)) continue;
                yield return new ParsedRecord
                {
                    Type = ImportItemType.Author, Ordinal = 0, SourceId = slug!, Title = name,
                    Payload = new ParsedAuthor { Login = slug!, DisplayName = name!, Email = Str(u, "email") }
                };
            }
        }

        // ── Posts / pages + their images. ──
        var seenImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (data.TryGetProperty("posts", out var posts) && posts.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in posts.EnumerateArray())
            {
                var postId = Str(p, "id") ?? Guid.NewGuid().ToString();
                var title = Str(p, "title") ?? "(untitled)";
                var slug = Str(p, "slug");
                var isPage = (Str(p, "type") ?? "").Equals("page", StringComparison.OrdinalIgnoreCase)
                             || (p.TryGetProperty("page", out var pg) && pg.ValueKind == JsonValueKind.True);
                var ghostStatus = (Str(p, "status") ?? "published").ToLowerInvariant();
                var status = ghostStatus switch { "published" => "publish", "scheduled" => "future", _ => "draft" };

                var html = ResolveGhostUrls(GhostContent.ToHtml(p), baseUrl);
                var feature = ResolveGhostUrls(Str(p, "feature_image"), baseUrl);

                // Emit image records (feature + inline), deduped across the whole export.
                foreach (var url in EnumerateImageUrls(feature, html))
                    if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && seenImages.Add(url))
                        yield return new ParsedRecord
                        {
                            Type = ImportItemType.Image, Ordinal = 3, SourceId = url,
                            Title = Path.GetFileName(new Uri(url).LocalPath),
                            Payload = new ParsedImage { Url = url }
                        };

                var post = new ParsedPost
                {
                    Title = title,
                    Slug = slug,
                    Html = html,
                    Excerpt = Str(p, "custom_excerpt"),
                    Status = status,
                    PublishedAt = ParseDate(Str(p, "published_at")),
                    AuthorLogin = null,
                    OriginalLink = !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(slug) ? $"{baseUrl!.TrimEnd('/')}/{slug}/" : null,
                    TagSlugs = postTagSlugs.TryGetValue(postId, out var ts) ? ts : new(),
                    FeatureImageUrl = feature,
                    MetaTitle = Str(p, "meta_title"),
                    MetaDescription = Str(p, "meta_description"),
                    IsPage = isPage
                };
                yield return new ParsedRecord
                {
                    Type = isPage ? ImportItemType.Page : ImportItemType.Post,
                    Ordinal = isPage ? 5 : 4, SourceId = postId, Title = title, Payload = post
                };
            }
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<string> EnumerateImageUrls(string? feature, string? html)
    {
        if (!string.IsNullOrWhiteSpace(feature)) yield return feature!;
        foreach (var u in new HtmlRewriter().ExtractImageUrls(html)) yield return u;
    }

    private static JsonDocument LoadDocument(string filePath)
    {
        if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(filePath);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("Zip contains no .json export.");
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            return JsonDocument.Parse(ms);
        }
        using var fs = File.OpenRead(filePath);
        return JsonDocument.Parse(fs);
    }

    private static bool TryGetData(JsonDocument doc, out JsonElement data)
    {
        data = default;
        if (doc.RootElement.TryGetProperty("db", out var db) && db.ValueKind == JsonValueKind.Array)
        {
            var first = db.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("data", out data))
                return data.ValueKind == JsonValueKind.Object;
        }
        // Some exports put "data" at the root.
        if (doc.RootElement.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Object)
            return true;
        return false;
    }

    private static string? ResolveSiteUrl(JsonElement data)
    {
        if (!data.TryGetProperty("settings", out var settings)) return null;
        if (settings.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in settings.EnumerateArray())
                if (string.Equals(Str(s, "key"), "url", StringComparison.OrdinalIgnoreCase))
                    return Str(s, "value");
        }
        else if (settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("url", out var u))
        {
            return u.GetString();
        }
        return null;
    }

    private static string? ResolveGhostUrls(string? value, string? baseUrl)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (!string.IsNullOrWhiteSpace(baseUrl))
            value = value.Replace("__GHOST_URL__", baseUrl!.TrimEnd('/'));
        return value;
    }

    private static string? Str(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static DateTime? ParseDate(string? raw) =>
        DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt : null;
}

/// <summary>Best-effort Ghost body → HTML: prefers rendered <c>html</c>, else converts Lexical/Mobiledoc.</summary>
internal static class GhostContent
{
    public static string ToHtml(JsonElement post)
    {
        var html = post.TryGetProperty("html", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
        if (!string.IsNullOrWhiteSpace(html)) return html!;

        try
        {
            if (post.TryGetProperty("lexical", out var lex) && lex.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(lex.GetString()))
                return FromLexical(lex.GetString()!);
            if (post.TryGetProperty("mobiledoc", out var mob) && mob.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(mob.GetString()))
                return FromMobiledoc(mob.GetString()!);
        }
        catch { /* fall through */ }
        return "";
    }

    private static string FromLexical(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("root", out var root) || !root.TryGetProperty("children", out var children))
            return "";
        var sb = new StringBuilder();
        foreach (var node in children.EnumerateArray())
        {
            var type = node.TryGetProperty("type", out var t) ? t.GetString() : null;
            var text = LexicalText(node);
            switch (type)
            {
                case "heading":
                    var tag = node.TryGetProperty("tag", out var hg) ? hg.GetString() : "h2";
                    sb.Append($"<{tag}>{text}</{tag}>"); break;
                case "quote": sb.Append($"<blockquote>{text}</blockquote>"); break;
                case "listitem": sb.Append($"<li>{text}</li>"); break;
                case "list":
                    var lt = node.TryGetProperty("listType", out var l) && l.GetString() == "number" ? "ol" : "ul";
                    sb.Append($"<{lt}>{LexicalText(node)}</{lt}>"); break;
                default:
                    if (!string.IsNullOrWhiteSpace(text)) sb.Append($"<p>{text}</p>"); break;
            }
        }
        return sb.ToString();
    }

    private static string LexicalText(JsonElement node)
    {
        var sb = new StringBuilder();
        if (node.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
            sb.Append(System.Net.WebUtility.HtmlEncode(txt.GetString()));
        if (node.TryGetProperty("children", out var kids) && kids.ValueKind == JsonValueKind.Array)
            foreach (var k in kids.EnumerateArray()) sb.Append(LexicalText(k));
        return sb.ToString();
    }

    private static string FromMobiledoc(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array)
            return "";
        var sb = new StringBuilder();
        foreach (var section in sections.EnumerateArray())
        {
            // Markup section: [1, "p", [ [openMarkups, close, "text"], ... ]]
            if (section.ValueKind != JsonValueKind.Array) continue;
            var arr = section.EnumerateArray().ToArray();
            if (arr.Length < 3 || arr[0].GetInt32() != 1) continue; // only markup sections
            var tag = arr[1].GetString() ?? "p";
            var text = new StringBuilder();
            foreach (var marker in arr[2].EnumerateArray())
            {
                var m = marker.EnumerateArray().ToArray();
                if (m.Length >= 3 && m[2].ValueKind == JsonValueKind.String)
                    text.Append(System.Net.WebUtility.HtmlEncode(m[2].GetString()));
            }
            if (text.Length > 0) sb.Append($"<{tag}>{text}</{tag}>");
        }
        return sb.ToString();
    }
}
