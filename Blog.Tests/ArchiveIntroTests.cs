using Blog.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The optional description of a category or tag is plain text with one recognised construct, a link. Nothing the
/// author types may reach the page as markup.
/// </summary>
public class ArchiveIntroTests
{
    [Fact]
    public void Empty_or_whitespace_is_no_description()
    {
        Assert.Null(ArchiveIntro.Normalize(null));
        Assert.Null(ArchiveIntro.Normalize("  \r\n "));
        Assert.Equal(string.Empty, ArchiveIntro.ToHtml(null));
        Assert.Equal(string.Empty, ArchiveIntro.ToPlainText("   "));
        Assert.Null(ArchiveIntro.ToMetaDescription(""));
    }

    [Fact]
    public void Paragraphs_come_from_blank_lines_and_single_breaks_are_spaces()
    {
        var html = ArchiveIntro.ToHtml("First line\r\nsame paragraph.\r\n\r\nSecond paragraph.");
        Assert.Equal("<p>First line same paragraph.</p><p>Second paragraph.</p>", html);
    }

    [Fact]
    public void An_internal_link_becomes_a_plain_anchor()
    {
        var html = ArchiveIntro.ToHtml("Start with [our comparison](/best-tools-compared).");
        Assert.Equal("<p>Start with <a href=\"/best-tools-compared\">our comparison</a>.</p>", html);
    }

    [Fact]
    public void An_https_link_is_marked_nofollow_noopener()
    {
        var html = ArchiveIntro.ToHtml("See [the standard](https://example.org/a?b=1&c=2).");
        Assert.Equal("<p>See <a href=\"https://example.org/a?b=1&amp;c=2\" rel=\"nofollow noopener\">the standard</a>.</p>", html);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<a href=\"/x\" onclick=\"y()\">x</a>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    public void Markup_typed_by_the_author_is_shown_as_text(string input)
    {
        var html = ArchiveIntro.ToHtml(input);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<a ", html);
        Assert.Contains("&lt;", html);
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](http://insecure.example/)")]
    [InlineData("[x](//evil.example/)")]
    [InlineData("[x](data:text/html,hi)")]
    [InlineData("[x](/a\" onmouseover=\"y)")]
    [InlineData("[x](/a b)")]
    public void A_link_that_is_not_site_relative_or_https_stays_text(string input)
    {
        var html = ArchiveIntro.ToHtml(input);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("href=", html);
    }

    [Fact]
    public void Link_text_cannot_carry_markup()
    {
        var html = ArchiveIntro.ToHtml("[<b>bold</b>](/page)");
        Assert.Equal("<p><a href=\"/page\">&lt;b&gt;bold&lt;/b&gt;</a></p>", html);
    }

    [Fact]
    public void Plain_text_and_meta_description_drop_the_link_syntax()
    {
        const string text = "Reviews of [practice systems](/best-systems) and how to choose.\n\nUpdated often.";
        Assert.Equal("Reviews of practice systems and how to choose. Updated often.", ArchiveIntro.ToPlainText(text));
        Assert.Equal("Reviews of practice systems and how to choose. Updated often.", ArchiveIntro.ToMetaDescription(text));
    }

    [Fact]
    public void A_long_description_is_cut_for_the_meta_description_only()
    {
        var text = string.Join(" ", Enumerable.Repeat("words", 80));
        var meta = ArchiveIntro.ToMetaDescription(text)!;
        Assert.True(meta.Length <= ArchiveIntro.MetaLength);
        Assert.Contains(text, ArchiveIntro.ToHtml(text));
    }

    [Fact]
    public void Length_is_validated_and_capped()
    {
        var tooLong = new string('a', ArchiveIntro.MaxLength + 1);
        Assert.NotNull(ArchiveIntro.Validate(tooLong));
        Assert.Null(ArchiveIntro.Validate(new string('a', ArchiveIntro.MaxLength)));
        Assert.Null(ArchiveIntro.Validate(null));
        Assert.Equal(ArchiveIntro.MaxLength, ArchiveIntro.Normalize(tooLong)!.Length);
    }
}

/// <summary>The wiring a grep can check: schema in all three places, audit on save, safe rendering.</summary>
public class ArchiveDescriptionSourceTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { ListingHeadingSourceTests.RepoRoot() }.Concat(parts).ToArray()));

    [Theory]
    [InlineData("Categories")]
    [InlineData("Tags")]
    public void The_description_column_is_in_all_three_schema_places(string table)
    {
        var guard = $"IF COL_LENGTH('{table}', 'Description') IS NULL";
        Assert.Contains(guard, Read("DBScripts", "2026-11-21_add-description-to-categories-and-tags.sql"));
        Assert.Contains(guard, Read("Blog.Infrastructure", "Data", "MigrationService.cs"));
        Assert.Contains(guard, Read("DBScripts", "init.sql"));
    }

    [Theory]
    [InlineData("CategoriesController.cs", "AuditActions.CategoryUpdated")]
    [InlineData("TagsController.cs", "AuditActions.TagUpdated")]
    public void Saving_a_description_is_validated_normalised_and_audited(string controller, string action)
    {
        var source = Read("Blog.Web", "Controllers", controller);
        Assert.Contains("ArchiveIntro.Validate(description)", source);
        Assert.Contains("ArchiveIntro.Normalize(description)", source);
        Assert.Contains(action, source);
        Assert.Contains("[HttpPost(\"describe/{id}\")]\r\n    [ValidateAntiForgeryToken]".Replace("\r\n", Environment.NewLine), source.Replace("\r\n", Environment.NewLine));
    }

    [Fact]
    public void The_public_page_renders_only_what_ArchiveIntro_produced()
    {
        var controller = Read("Blog.Web", "Controllers", "BlogController.cs");
        Assert.Contains("ViewBag.ArchiveIntroHtml = Blog.Core.Services.ArchiveIntro.ToHtml(categoryEntity.Description)", controller);
        Assert.Contains("ViewBag.ArchiveIntroHtml = Blog.Core.Services.ArchiveIntro.ToHtml(tagEntity.Description)", controller);
        // Nowhere else may fill it, because the view prints it raw.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(controller, "ViewBag\\.ArchiveIntroHtml\\s*=").Count);

        var view = Read("Blog.Web", "Views", "Blog", "Index.cshtml");
        Assert.Contains("<div class=\"ink-archive-head__intro\">@Html.Raw(archiveIntro)</div>", view);
        Assert.Contains(".ink-archive-head__intro", Read("Blog.Web", "wwwroot", "css", "inkwell.css"));
    }

    [Fact]
    public void The_editor_is_tracked_and_labelled()
    {
        var view = Read("Blog.Web", "Views", "Shared", "ArchiveDescription.cshtml");
        Assert.Contains("data-track-changes", view);
        Assert.Contains("data-dirty-indicator", view);
        Assert.Contains("<label for=\"archiveDescription\"", view);
    }
}

/// <summary>The whole path on the dev database: Desk list, editor, save, public page, clear. Skips without a database.</summary>
public class ArchiveDescriptionRenderTests : IClassFixture<Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>>
{
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory;
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public ArchiveDescriptionRenderTests(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, Xunit.Abstractions.ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    private HttpClient AdminClient() =>
        _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddAuthentication(AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName)
             .AddScheme<AuthenticationSchemeOptions, AdminPagesSmokeTests.TestAdminAuthHandler>(AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName, _ => { });
            s.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName;
                o.DefaultChallengeScheme    = AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName;
            });
        })).CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("categories", "category")]
    [InlineData("tags", "tag")]
    public async Task A_description_saved_in_the_Desk_appears_on_the_public_page_and_can_be_removed(string desk, string archive)
    {
        if (!TestDatabase.Available(_factory)) { _out.WriteLine("Dev database unavailable; skipping."); return; }

        // The test handler signs in as whoever this holds; the smoke tests fill it, but this class may run alone.
        if (AdminPagesSmokeTests.TestAdminAuthHandler.UserId == Guid.Empty)
        {
            using var scope = _factory.Services.CreateScope();
            var admin = await scope.ServiceProvider.GetRequiredService<Blog.Core.Interfaces.IUserRepository>().GetFirstAdminAsync();
            if (admin is null) { _out.WriteLine("No admin on this database; skipping."); return; }
            AdminPagesSmokeTests.TestAdminAuthHandler.UserId = admin.Id;
        }
        var client = AdminClient();
        var list = await client.GetStringAsync($"/admin/{desk}");
        // The first one whose public page exists: an archive with no published post answers 404.
        string? editorPath = null, editor = null, publicPath = null;
        foreach (System.Text.RegularExpressions.Match row in System.Text.RegularExpressions.Regex.Matches(list, $"/admin/{desk}/describe/([0-9a-fA-F-]{{36}})"))
        {
            var candidate = $"/admin/{desk}/describe/{row.Groups[1].Value}";
            var response = await client.GetAsync(candidate);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            var path = System.Text.RegularExpressions.Regex.Match(html, $"href=\"(/{archive}/[a-z0-9-]+)\"").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(path));
            if ((await client.GetAsync(path)).StatusCode != System.Net.HttpStatusCode.OK) continue;
            editorPath = candidate; editor = html; publicPath = path;
            break;
        }
        if (editorPath is null || editor is null || publicPath is null) { _out.WriteLine($"No {desk} with a public page on this database; skipping."); return; }
        Assert.Contains("data-track-changes", editor);
        var original = System.Net.WebUtility.HtmlDecode(
            System.Text.RegularExpressions.Regex.Match(editor, "<textarea[^>]*name=\"description\"[^>]*>(.*?)</textarea>", System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value).Trim();

        async Task Save(string text)
        {
            var page = await client.GetStringAsync(editorPath);
            var token = System.Text.RegularExpressions.Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            var response = await client.PostAsync(editorPath, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["description"] = text
            }));
            Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        }

        const string marker = "Zz render check [a link](/zz-render-check) and <b>no markup</b>.";
        try
        {
            await Save(marker);
            var page = await client.GetStringAsync(publicPath);
            Assert.Contains("ink-archive-head__intro", page);
            Assert.Contains("<a href=\"/zz-render-check\">a link</a>", page);
            Assert.Contains("&lt;b&gt;no markup&lt;/b&gt;", page);
            Assert.Contains("<meta name=\"description\" content=\"Zz render check a link and", page);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, "<h1[\\s>]"));

            var tooLong = await client.GetStringAsync(editorPath);
            var token = System.Text.RegularExpressions.Regex.Match(tooLong, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            var rejected = await client.PostAsync(editorPath, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["description"] = new string('a', Blog.Core.Services.ArchiveIntro.MaxLength + 5)
            }));
            Assert.Equal(System.Net.HttpStatusCode.OK, rejected.StatusCode);
            Assert.Contains("desk-error", await rejected.Content.ReadAsStringAsync());
        }
        finally
        {
            await Save(original);
        }

        var after = await client.GetStringAsync(publicPath);
        Assert.DoesNotContain("Zz render check", after);
        if (original.Length == 0) Assert.DoesNotContain("ink-archive-head__intro", after);
    }
}
