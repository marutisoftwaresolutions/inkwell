using Blog.Core.Domain;
using Blog.Web.Services.Import;
using Xunit;

namespace Blog.Tests;

public class GhostImporterTests : IDisposable
{
    private readonly string _file;

    // Minimal Ghost export: 1 site url, 1 tag, 1 internal (#) tag to skip, 1 published post (html) with
    // a feature image + inline __GHOST_URL__ image, 1 page, tagged, plus a lexical-only draft post.
    private const string SampleJson = """
{
  "db": [
    {
      "meta": { "exported_on": 1700000000000, "version": "5.0" },
      "data": {
        "posts": [
          {
            "id": "p1", "title": "Hello Ghost", "slug": "hello-ghost", "type": "post", "status": "published",
            "published_at": "2023-02-01T10:00:00.000Z", "feature_image": "__GHOST_URL__/content/images/2023/02/hero.jpg",
            "meta_title": "Hello Meta", "meta_description": "Hello desc",
            "html": "<p>Hi <img src=\"__GHOST_URL__/content/images/2023/02/inline.jpg\"></p>"
          },
          {
            "id": "p2", "title": "About", "slug": "about", "type": "page", "status": "published",
            "published_at": "2023-01-01T00:00:00.000Z", "html": "<p>About us</p>"
          },
          {
            "id": "p3", "title": "Draft One", "slug": "draft-one", "type": "post", "status": "draft",
            "lexical": "{\"root\":{\"children\":[{\"type\":\"paragraph\",\"children\":[{\"type\":\"text\",\"text\":\"Lexical body\"}]}]}}"
          }
        ],
        "tags": [
          { "id": "t1", "name": "News", "slug": "news" },
          { "id": "t2", "name": "#internal", "slug": "hash-internal" }
        ],
        "posts_tags": [ { "post_id": "p1", "tag_id": "t1", "sort_order": 0 } ],
        "users": [ { "id": "u1", "name": "Jane", "slug": "jane", "email": "jane@example.com" } ],
        "settings": [ { "key": "url", "value": "https://ghost.example.com" } ]
      }
    }
  ]
}
""";

    public GhostImporterTests()
    {
        _file = Path.Combine(Path.GetTempPath(), $"ghost-{Guid.NewGuid():N}.json");
        File.WriteAllText(_file, SampleJson);
    }

    public void Dispose()
    {
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    [Fact]
    public void Analyze_accepts_valid_ghost_json()
    {
        var m = new GhostImporter().Analyze(_file);
        Assert.Equal(ImportSource.Ghost, m.Source);
        Assert.Equal("Ghost JSON", m.DetectedFormat);
    }

    [Fact]
    public void ReadRecords_maps_types_tags_pages_and_resolves_ghost_urls()
    {
        var records = new GhostImporter().ReadRecords(_file).ToList();

        Assert.Equal(1, records.Count(r => r.Type == ImportItemType.Tag));            // internal #tag skipped
        Assert.Equal(1, records.Count(r => r.Type == ImportItemType.Author));
        Assert.Equal(2, records.Count(r => r.Type == ImportItemType.Post));           // published + draft
        Assert.Equal(1, records.Count(r => r.Type == ImportItemType.Page));
        Assert.Equal(2, records.Count(r => r.Type == ImportItemType.Image));          // feature + inline (deduped)

        // __GHOST_URL__ resolved to the site url in image records.
        Assert.Contains(records, r => r.Type == ImportItemType.Image && r.SourceId == "https://ghost.example.com/content/images/2023/02/hero.jpg");
        Assert.Contains(records, r => r.Type == ImportItemType.Image && r.SourceId == "https://ghost.example.com/content/images/2023/02/inline.jpg");

        var hello = Assert.IsType<ParsedPost>(records.First(r => r.Type == ImportItemType.Post && r.SourceId == "p1").Payload);
        Assert.Equal("hello-ghost", hello.Slug);
        Assert.Equal("publish", hello.Status);
        Assert.Contains("news", hello.TagSlugs);
        Assert.Equal("https://ghost.example.com/content/images/2023/02/hero.jpg", hello.FeatureImageUrl);
        Assert.Equal("https://ghost.example.com/hello-ghost/", hello.OriginalLink);
    }

    [Fact]
    public void ReadRecords_converts_lexical_body_when_no_html()
    {
        var records = new GhostImporter().ReadRecords(_file).ToList();
        var draft = Assert.IsType<ParsedPost>(records.First(r => r.Type == ImportItemType.Post && r.SourceId == "p3").Payload);
        Assert.Contains("Lexical body", draft.Html);
        Assert.Contains("<p>", draft.Html);
    }
}
