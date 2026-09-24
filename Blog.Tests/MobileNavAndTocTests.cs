using System.Text.RegularExpressions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Two regressions found by the 2026-09-17 UX review, pinned so they cannot return:
/// the admin sidebar was <c>hidden</c> below 768 px (no navigation at all on a phone), and post
/// pages rendered a script-built table of contents next to the server-rendered one.
/// </summary>
public class MobileNavAndTocTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
    private static string Web(string rel) => File.ReadAllText(Path.Combine(Root(), "Blog.Web", rel.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Admin_sidebar_is_never_display_none_and_its_toggles_are_named()
    {
        var layout = Web("Views/Shared/_AdminLayout.cshtml");
        var aside = Regex.Match(layout, "<aside id=\"sidebar\"[^>]*>").Value;
        Assert.NotEmpty(aside);
        Assert.DoesNotMatch("class=\"[^\"]*\\bhidden\\b", aside);      // slid off-screen, never display:none
        Assert.Contains("-translate-x-full", aside);
        Assert.Contains("md:translate-x-0", aside);
        Assert.Contains("aria-label=\"Admin navigation\"", aside);

        var opener = Regex.Match(layout, "<button[^>]*data-nav-open[^>]*>").Value;
        Assert.Contains("aria-label=\"Open menu\"", opener);
        Assert.Contains("aria-controls=\"sidebar\"", opener);
        Assert.Contains("aria-expanded=", opener);
        Assert.Contains("data-nav-close", layout);
        Assert.DoesNotContain("classList.add('-translate-x-full')", layout); // no inline toggles left behind

        var desk = Web("wwwroot/js/inkwell-desk.js");
        Assert.Contains("data-nav-open", desk);
        Assert.Contains("e.key === 'Escape'", desk);
    }

    [Fact]
    public void Post_page_has_one_table_of_contents_and_one_progress_bar()
    {
        var post = Web("Views/Blog/Post.cshtml");
        Assert.DoesNotContain("ink-toc-progress", post);
        Assert.DoesNotContain("id=\"ink-toc\"", post);
        Assert.DoesNotContain("querySelectorAll('h2, h3, h4')", post);
        Assert.Contains("blog-table-wrap", post); // the table enhancement stays

        foreach (var template in new[] { "Views/Blog/_Post_Magazine.cshtml", "Views/Blog/_Post_Neutral.cshtml" })
        {
            var html = Web(template);
            Assert.Equal(1, Regex.Matches(html, "_TableOfContents").Count);
            Assert.Equal(1, Regex.Matches(html, "_ReadingProgress").Count);
        }
    }
}
