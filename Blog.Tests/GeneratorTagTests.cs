using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Public pages name the software in a generator meta tag, once, without a version (a version helps
/// an attacker pick an exploit and helps nobody else), and an operator can leave it out.
/// </summary>
public class GeneratorTagTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public GeneratorTagTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task The_home_page_carries_one_generator_tag_with_no_version()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        var html = await _factory.CreateClient().GetStringAsync("/");
        Assert.Equal(1, html.Split("name=\"generator\"").Length - 1);
        Assert.Contains("<meta name=\"generator\" content=\"Inkwell\" />", html);
    }

    [Fact]
    public async Task It_can_be_switched_off()
    {
        if (!TestDatabase.Available(_factory)) return;
        using var off = _factory.WithWebHostBuilder(b => b.UseSetting("Site:GeneratorTag", "false"));
        var html = await off.CreateClient().GetStringAsync("/");
        Assert.DoesNotContain("name=\"generator\"", html);
    }

    [Fact]
    public void The_layout_never_prints_a_version_in_the_tag()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        var layout = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Views", "Shared", "_PublicLayout.cshtml"));
        Assert.Contains("<meta name=\"generator\" content=\"Inkwell\" />", layout);
    }
}
