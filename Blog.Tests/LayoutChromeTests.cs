using System.Text.RegularExpressions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Guards the "HideLayoutChrome" contract between a self-shelled index layout and _PublicLayout.
/// A partial rendered with &lt;partial&gt; receives a copy of ViewData, so a flag set inside
/// _Index_Magazine.cshtml never reached the layout and the home page rendered two mastheads and
/// two footers (issue #2, "theming doesn't work at all"). The flag must be set in the view that
/// shares ViewData with the layout (Index.cshtml), and only there.
/// </summary>
public class LayoutChromeTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Views(string rel) =>
        Path.Combine(Root(), "Blog.Web", "Views", rel.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void Public_layout_honours_HideLayoutChrome_for_both_navbar_and_footer()
    {
        var layout = File.ReadAllText(Views("Shared/_PublicLayout.cshtml"));
        var guards = Regex.Matches(layout, @"ViewData\[""HideLayoutChrome""\]\s*==\s*null").Count;
        Assert.True(guards >= 2, "_PublicLayout must guard both the Navbar and the Footer component with HideLayoutChrome.");
    }

    [Fact]
    public void Index_view_sets_HideLayoutChrome_for_the_Magazine_layout()
    {
        var index = File.ReadAllText(Views("Blog/Index.cshtml"));
        var set = Regex.Match(index,
            @"if\s*\(\s*blogLayout\s*==\s*""Magazine""\s*\)\s*\{\s*ViewData\[""HideLayoutChrome""\]\s*=\s*true;",
            RegexOptions.Singleline);
        Assert.True(set.Success, "Index.cshtml must set ViewData[\"HideLayoutChrome\"] = true when blogLayout is Magazine, because _Index_Magazine draws its own header and footer.");
    }

    [Fact]
    public void No_partial_view_tries_to_set_HideLayoutChrome()
    {
        // A partial's ViewData is a copy: setting the flag there is a no-op that reads as if it worked.
        var offenders = Directory.EnumerateFiles(Views(""), "_*.cshtml", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("_PublicLayout.cshtml", StringComparison.OrdinalIgnoreCase)
                     && !f.EndsWith("_AdminLayout.cshtml", StringComparison.OrdinalIgnoreCase))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"ViewData\[""HideLayoutChrome""\]\s*="))
            .Select(f => Path.GetRelativePath(Root(), f))
            .ToList();
        Assert.True(offenders.Count == 0, "HideLayoutChrome is set inside a partial, where it never reaches the layout: " + string.Join(", ", offenders));
    }
}
