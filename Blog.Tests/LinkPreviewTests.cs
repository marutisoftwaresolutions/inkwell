using Blog.Core.Services;
using Blog.Web.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The Bookmark block: what is read from a linked page, that nothing the page says can become markup
/// on this site, that the server never fetches a private or local address, and that only a signed-in
/// author can ask.
/// </summary>
public class LinkPreviewTests
{
    private static readonly Uri Page = new("https://www.example.org/guide");

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void Open_graph_values_win_over_the_plain_title_and_description()
    {
        var d = LinkPreview.Parse("""
            <html><head><title>Plain title</title>
            <meta name="description" content="Plain description">
            <meta property="og:title" content="A guide to &amp; around things">
            <meta content='Graph description' property='og:description'>
            <meta property="og:site_name" content="Example Site">
            </head></html>
            """, Page);
        Assert.Equal("A guide to & around things", d.Title);
        Assert.Equal("Graph description", d.Description);
        Assert.Equal("Example Site", d.Site);
        Assert.Equal("https://www.example.org/guide", d.Url);
    }

    [Fact]
    public void A_page_without_open_graph_falls_back_and_a_silent_page_still_gives_a_card()
    {
        var plain = LinkPreview.Parse("<title>\n  Plain   title </title><meta name=description content=Short>", Page);
        Assert.Equal("Plain title", plain.Title);
        Assert.Equal("Short", plain.Description);
        Assert.Equal("example.org", plain.Site);

        var silent = LinkPreview.Parse("", Page);
        Assert.Equal("example.org", silent.Title);
        Assert.Equal("", silent.Description);
        Assert.Equal("example.org", LinkPreview.Parse(null, Page).Site);
    }

    [Fact]
    public void Nothing_a_remote_page_says_becomes_markup_here()
    {
        var hostile = LinkPreview.Parse("""
            <meta property="og:title" content="&lt;script&gt;alert(1)&lt;/script&gt;Title &quot;quoted&quot;">
            <meta property="og:description" content="<img src=x onerror=alert(1)> text">
            <meta property="og:site_name" content="a&#0;b&#10;c">
            """, Page);
        Assert.DoesNotContain("<", hostile.Title);
        Assert.DoesNotContain("<", hostile.Description);
        Assert.DoesNotContain(hostile.Site, c => char.IsControl(c));

        var card = LinkPreview.CardHtml(hostile with { Title = "<b>x</b> \" ' &", Description = "</span><script>alert(1)</script>" });
        Assert.DoesNotContain("<script", card);
        Assert.DoesNotContain("<b>", card);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", card);
        Assert.StartsWith("<a class=\"comp-bookmark\" href=\"https://www.example.org/guide\" rel=\"nofollow noopener\">", card);
        Assert.EndsWith("</a>", card);
        Assert.Equal(1, card.Split("<a ").Length - 1);                  // one link, never a nested one
    }

    [Fact]
    public void Long_values_are_cut_and_an_empty_description_leaves_its_line_out()
    {
        var d = LinkPreview.Parse($"<title>{new string('t', 500)}</title><meta name=\"description\" content=\"{new string('d', 900)}\">", Page);
        Assert.Equal(LinkPreview.MaxTitleLength, d.Title.Length);
        Assert.Equal(LinkPreview.MaxDescriptionLength, d.Description.Length);
        Assert.DoesNotContain("comp-bookmark-text", LinkPreview.CardHtml(d with { Description = "" }));
    }

    [Theory]
    [InlineData("https://example.org/a", true)]
    [InlineData("http://example.org", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("https://user:pass@example.org/", false)]
    [InlineData("example.org", false)]
    [InlineData("", false)]
    public void Only_a_plain_web_address_is_accepted(string url, bool accepted) =>
        Assert.Equal(accepted, LinkPreview.CleanUrl(url) is not null);

    [Fact]
    public void A_card_never_links_to_anything_but_a_web_address()
    {
        var card = LinkPreview.CardHtml(new LinkPreview.Details("javascript:alert(1)", "t", "", "s"));
        Assert.Contains("href=\"#\"", card);
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost/admin")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]     // cloud metadata
    [InlineData("http://[::1]/")]
    [InlineData("http://intranet/")]
    public async Task The_server_never_fetches_a_local_or_private_address(string url)
    {
        var (details, error) = await new LinkPreviewFetcher().FetchAsync(url, CancellationToken.None);
        Assert.Null(details);
        Assert.Contains("not on the public internet", error);
    }

    [Fact]
    public async Task A_bad_address_gets_a_reason_instead_of_an_exception()
    {
        var (details, error) = await new LinkPreviewFetcher().FetchAsync("not a link", CancellationToken.None);
        Assert.Null(details);
        Assert.Contains("https://", error);
    }

    [Fact]
    public void The_fetch_is_guarded_at_the_socket_bounded_and_only_for_signed_in_authors()
    {
        var fetcher = Read("Blog.Web", "Services", "LinkPreviewFetcher.cs");
        Assert.Contains("new HttpClient(PublicNetwork.Handler())", fetcher);      // public addresses only, no automatic redirects
        Assert.Contains("WebhookRules.ValidateUrl(uri.AbsoluteUri)", fetcher);    // checked again on every redirect
        Assert.Contains("hop <= MaxRedirects", fetcher);
        Assert.Contains("new byte[MaxBytes]", fetcher);
        Assert.True(LinkPreviewFetcher.MaxBytes <= 1024 * 1024);

        var controller = Read("Blog.Web", "Controllers", "LinkPreviewController.cs");
        Assert.Contains("[Authorize(Policy = \"CanEditPosts\")]", controller);
        Assert.Contains("[ValidateAntiForgeryToken]", controller);
        Assert.Contains("> MaxPerMinute", controller);
        Assert.DoesNotContain("[AllowAnonymous]", controller);

        // The editor writes fetched text as text, never as markup, and the reader page loads nothing from the other site.
        var js = Read("Blog.Web", "wwwroot", "js", "inkwell-builder.js");
        var block = js[js.IndexOf("window.fetchBookmark", StringComparison.Ordinal)..];
        block = block[..block.IndexOf("};", block.IndexOf(".catch(", StringComparison.Ordinal), StringComparison.Ordinal)];
        Assert.Contains("span.textContent = text;", block);
        Assert.DoesNotContain("innerHTML", block);
        Assert.DoesNotContain("<img", js[js.IndexOf("register('bookmark'", StringComparison.Ordinal)..js.IndexOf("register('divider'", StringComparison.Ordinal)]);
    }
}
