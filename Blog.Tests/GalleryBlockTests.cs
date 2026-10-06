using System.Text.RegularExpressions;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The gallery block: one markup written by every editor (the post and page builders, the new-post
/// editor, the Markdown toolbar), styled and enhanced by the reader without JavaScript being required.
/// </summary>
public class GalleryBlockTests
{
    // What InkwellGallery.html() writes (wwwroot/js/inkwell-gallery-editor.js).
    private const string Gallery =
        "<div class=\"comp-gallery\" data-columns=\"3\">\n" +
        "<figure class=\"comp-gallery-item\"><a href=\"/uploads/images/a.webp\"><img src=\"/uploads/images/a.webp\" alt=\"Phoropter on a stand\" width=\"1200\" height=\"800\" loading=\"lazy\" decoding=\"async\"></a><figcaption>Exam room — 2026</figcaption></figure>\n" +
        "<figure class=\"comp-gallery-item\"><a href=\"/uploads/images/b.webp\"><img src=\"/uploads/images/b.webp\" alt=\"Frame wall\" loading=\"lazy\" decoding=\"async\"></a></figure>\n" +
        "</div>";

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void A_gallery_inside_markdown_renders_unchanged()
    {
        var md = "Intro paragraph.\n\n" + Gallery + "\n\nAfter the gallery.";
        var html = MarkdownRenderer.ToHtml(md);
        Assert.Contains(Gallery, html);
        Assert.True(MarkdownRenderer.RendersUnchanged(Gallery));       // a post holding one can switch to Markdown
    }

    [Fact]
    public void Every_editor_writes_the_same_gallery()
    {
        var shared = RepoFile("Blog.Web", "wwwroot", "js", "inkwell-gallery-editor.js");
        Assert.Contains("class=\"comp-gallery\" data-columns=", shared);
        Assert.Contains("class=\"comp-gallery-item\"", shared);
        Assert.Contains("loading=\"lazy\"", shared);
        Assert.Contains("'media-selected-many'", shared);
        Assert.Contains("e.preventDefault()", shared);                  // an editor click never follows a gallery link

        // The builders: palette button, template, settings panel, and (for saved content) reading it back
        // before the generic image rule, which would otherwise swallow it.
        foreach (var view in new[] { new[] { "Posts", "Edit.cshtml" }, new[] { "Pages", "Edit.cshtml" }, new[] { "Pages", "Create.cshtml" } })
        {
            var src = RepoFile(new[] { "Blog.Web", "Views" }.Concat(view).ToArray());
            var name = string.Join("/", view);
            Assert.True(src.Contains("data-component=\"gallery\""), name + ": palette");
            Assert.True(src.Contains("gallery: () => InkwellGallery.pick()"), name + ": template");
            Assert.True(src.Contains("gallery: (b) => InkwellGallery.settings(b)"), name + ": settings");
            Assert.True(src.Contains("gallery:'Gallery'"), name + ": panel title");
            Assert.Matches(new Regex("<button[^>]*data-component=\"gallery\"[^>]*>\\s*<svg[^>]*>.*?</svg>\\s*</button>", RegexOptions.Singleline), src);
            if (view[1] == "Edit.cshtml")
            {
                var gallery = src.IndexOf("contains('comp-gallery')", StringComparison.Ordinal);
                var image = src.IndexOf("contains('comp-image')", StringComparison.Ordinal);
                Assert.True(gallery > 0 && gallery < image, name + ": a saved gallery must be recognised before images");
            }
        }

        var quill = RepoFile("Blog.Web", "Views", "Shared", "_QuillEditorScripts.cshtml");
        Assert.Contains("GalleryBlot.className = 'comp-gallery'", quill);
        Assert.Contains("label:'Gallery'", RepoFile("Blog.Web", "Views", "Shared", "_SlashCommandMenu.cshtml"));
        Assert.Contains("btn('gallery'", RepoFile("Blog.Web", "Views", "Posts", "_MarkdownEditorScripts.cshtml"));
        Assert.Contains("inkwell-gallery-editor.js", RepoFile("Blog.Web", "Views", "Shared", "_MediaSelectorModal.cshtml"));
        Assert.Contains("multiple: true", shared);
        Assert.Contains("'media-selected-many'", RepoFile("Blog.Web", "Views", "Shared", "_MediaSelectorModal.cshtml"));
    }

    [Fact]
    public void Editor_scripts_have_no_line_break_inside_a_string()
    {
        // A '\n' written as a real line break inside a quoted string is a syntax error that silently
        // disables the whole script block — it once took out every slash command in the rich-text editor.
        var broken = new Regex(@"'\r?\n'\s*,");
        foreach (var file in new[] { "_SlashCommandMenu.cshtml", "_QuillEditorScripts.cshtml", "_MediaSelectorModal.cshtml" })
            Assert.DoesNotMatch(broken, RepoFile("Blog.Web", "Views", "Shared", file));
        Assert.DoesNotMatch(broken, RepoFile("Blog.Web", "wwwroot", "js", "inkwell-gallery-editor.js"));
        Assert.Contains("q.insertText(at+1,'\\n','user');", RepoFile("Blog.Web", "Views", "Shared", "_SlashCommandMenu.cshtml"));
    }

    [Fact]
    public void Readers_get_a_grid_and_a_self_hosted_lightbox_only_where_needed()
    {
        var css = RepoFile("Blog.Web", "wwwroot", "css", "neutral-blog.css");
        Assert.Contains(".comp-gallery {", css);
        Assert.Contains(".comp-gallery-item figcaption", css);
        Assert.Contains(".ink-lightbox::backdrop", css);

        var js = RepoFile("Blog.Web", "wwwroot", "js", "inkwell-gallery.js");
        Assert.Contains("'.comp-gallery .comp-gallery-item a'", js);
        Assert.Contains("showModal()", js);                             // native dialog: Escape and focus come free
        Assert.Contains("opener.focus()", js);                          // focus returns to the image
        Assert.DoesNotMatch(new Regex(@"https?://"), js);               // nothing fetched from elsewhere

        var layout = RepoFile("Blog.Web", "Views", "Shared", "_PublicLayout.cshtml");
        Assert.Contains("ViewData[\"HasGallery\"] is true", layout);
        Assert.Contains("ViewData[\"HasGallery\"]", RepoFile("Blog.Web", "Views", "Blog", "Post.cshtml"));
        Assert.Contains("ViewData[\"HasGallery\"]", RepoFile("Blog.Web", "Views", "Blog", "Page.cshtml"));
    }

    [Fact]
    public void Exports_rewrite_gallery_links_and_images()
    {
        var found = new HashSet<string>();
        var html = Blog.Core.Services.Export.ExportHtml.RewriteUploads(Gallery, "https://blog.example.com", p => "https://new.example" + p, found);
        Assert.DoesNotContain("\"/uploads/", html);
        Assert.Contains("href=\"https://new.example/uploads/images/a.webp\"", html);
        Assert.Equal(2, found.Count);
    }
}
