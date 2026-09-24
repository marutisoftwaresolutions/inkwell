using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

public class ShadowedRoutesTests
{
    [Fact]
    public void A_file_at_a_literal_route_path_is_reported()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "llms.txt", "robots.txt" };
        var found = ShadowedRoutes.Find(
            new[] { "llms.txt", "llms-full.txt", "robots.txt", "sitemap.xml", "{slug}", "category/{slug}", "" },
            files.Contains);

        Assert.Equal(new[] { "llms.txt", "robots.txt" }, found);
    }

    [Fact]
    public void Parameterised_and_root_routes_are_never_probed()
    {
        var probed = new List<string>();
        ShadowedRoutes.Find(new[] { "/", "{slug}", "tag/{slug}", "admin/posts/edit/{id}", "feed" }, p => { probed.Add(p); return false; });
        Assert.Equal(new[] { "feed" }, probed);
    }

    [Theory]
    [InlineData("/llms.txt", "llms.txt")]
    [InlineData("llms.txt", "llms.txt")]
    [InlineData("  /sitemap.xml ", "sitemap.xml")]
    [InlineData("/", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("{key}.txt", null)]
    [InlineData("../secret", null)]
    [InlineData("series/", null)]
    public void Normalise_keeps_only_literal_file_like_paths(string? pattern, string? expected)
    {
        Assert.Equal(expected, ShadowedRoutes.Normalise(pattern));
    }

    [Fact]
    public void Results_are_deduplicated_and_sorted()
    {
        var found = ShadowedRoutes.Find(new[] { "/feed", "feed", "/robots.txt" }, _ => true);
        Assert.Equal(new[] { "feed", "robots.txt" }, found);
    }
}
