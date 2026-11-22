using System.Net;
using System.Net.Http.Headers;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Share links, comment permalinks and the Markdown form of a post: what each produces, that the
/// share links load nothing from the networks they point at, that Markdown is given only to a client
/// that asks for it and never for an unpublished post, and that search engines keep one copy.
/// </summary>
public class ReaderAdditionsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ReaderAdditionsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    // ── Share links ──────────────────────────────────────────────────────

    [Fact]
    public void Share_links_carry_the_address_and_title_encoded_and_nothing_else()
    {
        var links = ShareLinks.For("https://blog.example.org/a-post?x=1&y=2", "Tea & coffee:\n a \"guide\"");
        Assert.Equal(new[] { "email", "linkedin", "bluesky", "x", "facebook", "whatsapp" }, links.Select(l => l.Key).ToArray());
        Assert.All(links, l => Assert.DoesNotContain(" ", l.Href));
        Assert.All(links, l => Assert.DoesNotContain("\"", l.Href));
        Assert.Contains("https%3A%2F%2Fblog.example.org%2Fa-post%3Fx%3D1%26y%3D2", links.Single(l => l.Key == "linkedin").Href);
        Assert.StartsWith("mailto:?subject=Tea%20%26%20coffee%3A%20a%20%22guide%22&body=", links.Single(l => l.Key == "email").Href);
        Assert.All(links.Where(l => l.Key != "email"), l => Assert.StartsWith("https://", l.Href));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/a-post")]
    [InlineData("javascript:alert(1)")]
    public void No_links_are_made_for_anything_but_a_full_web_address(string? url) => Assert.Empty(ShareLinks.For(url, "Title"));

    [Fact]
    public void The_share_strip_is_off_by_default_plain_links_and_loads_nothing_from_the_networks()
    {
        Assert.False(new UserSettings().ShareLinksEnabled);
        var partial = Read("Blog.Web", "Views", "Blog", "_ShareLinks.cshtml");
        Assert.Contains("rel=\"noopener nofollow\"", partial);
        Assert.DoesNotContain("<iframe", partial);
        Assert.DoesNotContain("<img", partial);
        Assert.Equal(1, partial.Split("<script").Length - 1);
        Assert.Contains("src=\"~/js/inkwell-share.js\"", partial);                     // the only script is the site's own
        var script = Read("Blog.Web", "wwwroot", "js", "inkwell-share.js");
        Assert.StartsWith("/*", script);
        Assert.DoesNotContain("http", script.Replace("location.href", ""));           // it names no outside address
        Assert.Contains("ViewBag.ShareLinks = userSettings.ShareLinksEnabled && !isPreview;", Read("Blog.Web", "Controllers", "BlogController.cs"));
        Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b|\brgb\(", Read("Blog.Web", "wwwroot", "css", "inkwell.css").Split("/* Share links under a post")[1].Split("/* Nested categories")[0]);
    }

    // ── Comment permalinks ───────────────────────────────────────────────────

    [Fact]
    public void Each_comment_has_its_own_address_that_works_without_script()
    {
        var thread = Read("Blog.Web", "Views", "Blog", "_CommentThread.cshtml");
        Assert.Contains("<div id=\"comment-@rid\"", thread);
        Assert.Contains("<a href=\"#comment-@rid\"", thread);                          // the date is the link
        Assert.Contains("data-comment-link=\"comment-@rid\" hidden", thread);          // the copy button appears only where it can work
        Assert.Contains("data-comment-link", Read("Blog.Web", "wwwroot", "js", "inkwell-comments.js"));
        Assert.Contains(".ink-comment:target", Read("Blog.Web", "wwwroot", "css", "inkwell.css"));
    }

    // ── Markdown ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("text/markdown", true)]
    [InlineData("text/markdown, text/html;q=0.8", true)]
    [InlineData("TEXT/MARKDOWN", true)]
    [InlineData("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8", false)]      // a browser
    [InlineData("text/html, text/markdown;q=0.5", false)]
    [InlineData("*/*", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Markdown_is_given_only_to_a_client_that_asks_for_it_first(string? accept, bool wanted) =>
        Assert.Equal(wanted, PostMarkdown.Wanted(accept));

    [Fact]
    public void Html_becomes_readable_markdown_with_absolute_links()
    {
        var md = HtmlToMarkdown.Convert("""
            <h2>Why it <em>matters</em></h2>
            <p>Read the <a href="/guide">full guide</a> &amp; the <strong>summary</strong>.<br>Second line.</p>
            <ul><li>One</li><li>Two <ul><li>Nested</li></ul></li></ul>
            <ol><li>First</li><li>Second</li></ol>
            <blockquote><p>A quoted line.</p></blockquote>
            <pre><code>var x = a * b;
            return x;</code></pre>
            <p>Use <code>a_b</code> here.</p>
            <table><thead><tr><th>Plan</th><th>Price</th></tr></thead><tbody><tr><td>Basic</td><td>$10 | month</td></tr></tbody></table>
            <img src="/uploads/a.png" alt="A chart">
            <hr>
            """, new Uri("https://blog.example.org/a-post"));

        Assert.Contains("## Why it *matters*", md);
        Assert.Contains("[full guide](https://blog.example.org/guide) & the **summary**.", md);
        Assert.Contains("- One\n- Two\n  - Nested", md);
        Assert.Contains("1. First\n2. Second", md);
        Assert.Contains("> A quoted line.", md);
        Assert.Contains("```\nvar x = a * b;", md);                                     // code is not escaped
        Assert.Contains("`a_b`", md);
        Assert.Contains("| Plan | Price |\n| --- | --- |\n| Basic | $10 \\| month |", md);
        Assert.Contains("![A chart](https://blog.example.org/uploads/a.png)", md);
        Assert.Contains("---", md);
        Assert.DoesNotContain("<", md);
    }

    [Fact]
    public void Scripts_forms_and_frames_are_left_out_and_unknown_markup_keeps_its_text()
    {
        var md = HtmlToMarkdown.Convert("<p>Before</p><script>alert('x')</script><style>p{}</style><form><input name=q><button>Go</button></form><iframe src=\"https://x.example\"></iframe><custom-thing>Kept text</custom-thing><p>2 * 3 [note]</p>");
        Assert.Contains("Before", md);
        Assert.Contains("Kept text", md);
        Assert.Contains("2 \\* 3 \\[note\\]", md);                                      // literal characters stay literal
        Assert.DoesNotContain("alert", md);
        Assert.DoesNotContain("x.example", md);
        Assert.DoesNotContain("Go", md);
    }

    [Fact]
    public void Broken_or_empty_html_never_throws_and_the_block_editors_own_blocks_read_well()
    {
        Assert.Equal("", HtmlToMarkdown.Convert(null));
        Assert.Equal("", HtmlToMarkdown.Convert("   "));
        Assert.Contains("unclosed", HtmlToMarkdown.Convert("<p>unclosed <strong>bold <em>both</p></div></li>"));

        var blocks = HtmlToMarkdown.Convert("<div class=\"comp-callout\" data-kind=\"warning\"><p>Mind the gap.</p></div><details class=\"comp-toggle\"><summary>A question</summary><p>Its answer.</p></details><a class=\"comp-bookmark\" href=\"https://example.com/\" rel=\"nofollow noopener\"><span class=\"comp-bookmark-title\">Example</span><span class=\"comp-bookmark-site\">example.com</span></a>");
        Assert.Contains("Mind the gap.", blocks);
        Assert.Contains("**A question**", blocks);
        Assert.Contains("Its answer.", blocks);
        Assert.Contains("(https://example.com/)", blocks);
    }

    [Fact]
    public void A_post_document_carries_what_a_reader_sees_and_a_markdown_post_is_given_as_written()
    {
        var post = new Post
        {
            Title = "Choosing\n a system", Slug = "choosing-a-system", Html = "<p>Body <a href=\"/other\">link</a>.</p>",
            MetaDescription = "A short description.", AnswerCapsule = "The short answer.",
            KeyFactsJson = "[{\"label\":\"Price\",\"value\":\"$10 a month\"}]",
            FaqJson = "[{\"question\":\"Is it hosted?\",\"answer\":\"<p>Yes.</p>\"}]",
            PublishedAt = new DateTime(2026, 11, 1), UpdatedAt = new DateTime(2026, 11, 10), ReviewNote = "internal: check price",
        };
        var md = PostMarkdown.Build(post, "https://blog.example.org/choosing-a-system", "Example Blog", "Jane Doe");
        Assert.StartsWith("# Choosing a system\n\n- Source: https://blog.example.org/choosing-a-system\n- Site: Example Blog\n- Author: Jane Doe\n- Published: 2026-11-01\n- Updated: 2026-11-10\n", md);
        Assert.Contains("> A short description.", md);
        Assert.Contains("## In short\n\nThe short answer.", md);
        Assert.Contains("- **Price:** $10 a month", md);
        Assert.Contains("[link](https://blog.example.org/other)", md);
        Assert.Contains("### Is it hosted?\n\nYes.", md);
        Assert.DoesNotContain("internal", md);                                          // nothing kept from readers

        var written = new Post { Title = "T", Slug = "t", EditorMode = EditorModes.Markdown, Markdown = "Some *source* text.", Html = "<p>Some <em>source</em> text.</p>" };
        Assert.Contains("Some *source* text.", PostMarkdown.Build(written, "https://blog.example.org/t", null, null));
    }

    [Fact]
    public async Task The_site_serves_markdown_on_request_keeps_one_indexed_copy_and_hides_unpublished_posts()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var scope = _factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        if (admin is null) return;
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var live = "zz-md-live-" + tag; var draft = "zz-md-draft-" + tag;
        try
        {
            foreach (var (slug, status) in new[] { (live, "Published"), (draft, "Draft") })
                await conn.ExecuteAsync(@"INSERT INTO Posts (Id, Title, Slug, Html, Plaintext, Status, Visibility, Type, AuthorId, PublishedAt, CreatedAt, UpdatedAt)
                                          VALUES (NEWID(), N'Markdown check', @slug, N'<h2>Heading</h2><p>Body text.</p>', N'Body text.', @status, N'public', N'post', @author, DATEADD(MINUTE, -5, SYSUTCDATETIME()), SYSUTCDATETIME(), SYSUTCDATETIME())",
                    new { slug, status, author = admin.Id });

            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // The .md address
            var file = await client.GetAsync($"/{live}.md");
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            Assert.Equal("text/markdown", file.Content.Headers.ContentType?.MediaType);
            Assert.Equal("noindex", string.Join(",", file.Headers.GetValues("X-Robots-Tag")));
            Assert.Contains($"/{live}>; rel=\"canonical\"", string.Join(",", file.Headers.GetValues("Link")));
            var text = await file.Content.ReadAsStringAsync();
            Assert.StartsWith("# Markdown check", text);
            Assert.Contains("## Heading\n\nBody text.", text);
            Assert.DoesNotContain("<", text);

            // Unchanged text revalidates to 304.
            var again = new HttpRequestMessage(HttpMethod.Get, $"/{live}.md");
            again.Headers.IfNoneMatch.Add(file.Headers.ETag!);
            Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);

            // The page's own address: Markdown only when asked for; a browser gets the page, which points at the Markdown.
            var asked = new HttpRequestMessage(HttpMethod.Get, "/" + live);
            asked.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
            var negotiated = await client.SendAsync(asked);
            Assert.Equal("text/markdown", negotiated.Content.Headers.ContentType?.MediaType);
            Assert.Contains("Accept", string.Join(",", negotiated.Headers.Vary));

            var browser = new HttpRequestMessage(HttpMethod.Get, "/" + live);
            browser.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            var page = await client.SendAsync(browser);
            Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
            Assert.Contains("Accept", string.Join(",", page.Headers.Vary));
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains($"href=\"/{live}.md\"", html);
            Assert.Contains("type=\"text/markdown\"", html);

            // A draft is not there in either form.
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/{draft}.md")).StatusCode);
            var draftAsked = new HttpRequestMessage(HttpMethod.Get, "/" + draft);
            draftAsked.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(draftAsked)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/zz-no-such-post-" + tag + ".md")).StatusCode);

            // llms.txt tells agents the form exists.
            Assert.Contains("Each post as Markdown", await client.GetStringAsync("/llms.txt"));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM Posts WHERE Slug IN @slugs", new { slugs = new[] { live, draft } });
        }
    }
}
