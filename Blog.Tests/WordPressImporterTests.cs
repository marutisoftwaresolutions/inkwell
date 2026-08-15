using Blog.Core.Domain;
using Blog.Web.Services.Import;
using Xunit;

namespace Blog.Tests;

public class WordPressImporterTests : IDisposable
{
    private readonly string _file;

    private const string SampleWxr = """
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"
  xmlns:content="http://purl.org/rss/1.0/modules/content/"
  xmlns:dc="http://purl.org/dc/elements/1.1/"
  xmlns:wp="http://wordpress.org/export/1.2/"
  xmlns:excerpt="http://wordpress.org/export/1.2/excerpt/">
<channel>
  <wp:author><wp:author_login>jane</wp:author_login><wp:author_display_name>Jane Doe</wp:author_display_name><wp:author_email>jane@example.com</wp:author_email></wp:author>
  <item>
    <title>Attachment Pic</title>
    <wp:post_id>10</wp:post_id>
    <wp:post_type>attachment</wp:post_type>
    <wp:attachment_url>https://old.example.com/wp-content/uploads/2020/01/pic.jpg</wp:attachment_url>
  </item>
  <item>
    <title>Hello World</title>
    <link>https://old.example.com/2020/01/05/hello-world/</link>
    <dc:creator>jane</dc:creator>
    <content:encoded><![CDATA[<p>Hello <img src="https://old.example.com/wp-content/uploads/2020/01/pic.jpg" /></p>]]></content:encoded>
    <excerpt:encoded><![CDATA[An intro]]></excerpt:encoded>
    <wp:post_id>1</wp:post_id>
    <wp:post_date_gmt>2020-01-05 10:00:00</wp:post_date_gmt>
    <wp:post_name>hello-world</wp:post_name>
    <wp:status>publish</wp:status>
    <wp:post_type>post</wp:post_type>
    <category domain="category" nicename="news">News</category>
    <category domain="post_tag" nicename="intro">Intro</category>
    <wp:postmeta><wp:meta_key>_thumbnail_id</wp:meta_key><wp:meta_value>10</wp:meta_value></wp:postmeta>
    <wp:comment><wp:comment_id>100</wp:comment_id><wp:comment_author>Bob</wp:comment_author><wp:comment_content>Nice!</wp:comment_content><wp:comment_approved>1</wp:comment_approved></wp:comment>
  </item>
  <item>
    <title>Draft Post</title>
    <wp:post_id>2</wp:post_id>
    <wp:post_name>draft-post</wp:post_name>
    <wp:status>draft</wp:status>
    <wp:post_type>post</wp:post_type>
    <content:encoded><![CDATA[Draft body]]></content:encoded>
  </item>
  <item>
    <title>About</title>
    <wp:post_id>3</wp:post_id>
    <wp:post_name>about</wp:post_name>
    <wp:status>publish</wp:status>
    <wp:post_type>page</wp:post_type>
    <content:encoded><![CDATA[<p>About us</p>]]></content:encoded>
  </item>
</channel>
</rss>
""";

    public WordPressImporterTests()
    {
        _file = Path.Combine(Path.GetTempPath(), $"wxr-{Guid.NewGuid():N}.xml");
        File.WriteAllText(_file, SampleWxr);
    }

    public void Dispose()
    {
        try { if (File.Exists(_file)) File.Delete(_file); } catch { /* ignore */ }
    }

    [Fact]
    public void Analyze_accepts_valid_wxr()
    {
        var manifest = new WordPressImporter().Analyze(_file);
        Assert.Equal(ImportSource.WordPress, manifest.Source);
        Assert.Equal("WordPress WXR", manifest.DetectedFormat);
    }

    [Fact]
    public void Analyze_rejects_non_wxr()
    {
        var bad = Path.Combine(Path.GetTempPath(), $"bad-{Guid.NewGuid():N}.xml");
        File.WriteAllText(bad, "<rss version=\"2.0\"><channel></channel></rss>"); // no wp: namespace
        try
        {
            Assert.Throws<InvalidDataException>(() => new WordPressImporter().Analyze(bad));
        }
        finally { File.Delete(bad); }
    }

    [Fact]
    public void ReadRecords_emits_expected_types_and_order()
    {
        var records = new WordPressImporter().ReadRecords(_file).ToList();

        Assert.Equal(1, records.Count(r => r.Type == ImportItemType.Author));
        Assert.Equal(1, records.Count(r => r.Type == ImportItemType.Image));
        Assert.Equal(1, records.Count(r => r.Type == ImportItemType.Category));
        Assert.Equal(1, records.Count(r => r.Type == ImportItemType.Tag));
        Assert.Equal(2, records.Count(r => r.Type == ImportItemType.Post));
        Assert.Equal(1, records.Count(r => r.Type == ImportItemType.Page));
        Assert.Equal(1, records.Count(r => r.Type == ImportItemType.Comment));

        // Ordinals put dependencies first: author(0) < category(1) < tag(2) < image(3) < post(4) < page(5) < comment(6).
        Assert.Equal(0, records.First(r => r.Type == ImportItemType.Author).Ordinal);
        Assert.Equal(4, records.First(r => r.Type == ImportItemType.Post).Ordinal);
        Assert.Equal(6, records.First(r => r.Type == ImportItemType.Comment).Ordinal);
    }

    [Fact]
    public void ReadRecords_parses_post_fields_and_resolves_feature_image()
    {
        var records = new WordPressImporter().ReadRecords(_file).ToList();
        var helloRec = records.First(r => r.Type == ImportItemType.Post && r.SourceId == "1");
        var post = Assert.IsType<ParsedPost>(helloRec.Payload);

        Assert.Equal("Hello World", post.Title);
        Assert.Equal("hello-world", post.Slug);
        Assert.Equal("publish", post.Status);
        Assert.Equal("jane", post.AuthorLogin);
        Assert.Contains("news", post.CategorySlugs);
        Assert.Contains("intro", post.TagSlugs);
        Assert.Equal("https://old.example.com/wp-content/uploads/2020/01/pic.jpg", post.FeatureImageUrl);
        Assert.NotNull(post.PublishedAt);

        // Comment is linked to its post's source id.
        var comment = Assert.IsType<ParsedComment>(records.First(r => r.Type == ImportItemType.Comment).Payload);
        Assert.Equal("1", comment.PostSourceId);
        Assert.True(comment.Approved);
    }
}
