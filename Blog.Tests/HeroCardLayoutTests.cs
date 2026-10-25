using System.Text.RegularExpressions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The Classic and Neutral home heroes overlay a text card on the feature image. On 2026-10-13 the live card
/// was locked to a 2:1 ratio with overflow hidden, so a three-line title cut the excerpt and hid Read More.
/// </summary>
public class HeroCardLayoutTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Theory]
    [InlineData("_Index_Classic.cshtml")]
    [InlineData("_Index_Neutral.cshtml")]
    public void Hero_card_sizes_to_its_content_and_is_never_clipped(string file)
    {
        var view = File.ReadAllText(Path.Combine(Root(), "Blog.Web", "Views", "Blog", file));
        var start = view.IndexOf("<!-- Hero -->", StringComparison.Ordinal);
        Assert.True(start >= 0, file + " has no hero block");
        var hero = view[start..view.IndexOf(">Read More</a>", start, StringComparison.Ordinal)];

        // Every box from the frame down to the card text: none may clip, and only the frame may fix a ratio.
        var boxes = Regex.Matches(hero, @"<div\b[^>]*class=""([^""]*)""").Select(m => m.Groups[1].Value).ToList();
        Assert.True(boxes.Count >= 3, file + ": expected frame, card and card body");
        Assert.All(boxes, c => Assert.DoesNotContain("overflow-hidden", c));
        Assert.All(boxes.Skip(2), c => Assert.DoesNotContain("aspect-", c));
        Assert.Contains("line-clamp-3", hero); // a long excerpt is shortened, not cut mid-line
    }
}
