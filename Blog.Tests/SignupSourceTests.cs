using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Sign-up attribution keeps only the path of a page on this site: no query string, nothing from
/// another site, nothing that identifies the reader.
/// </summary>
public class SignupSourceTests
{
    [Theory]
    [InlineData("https://example.org/best-lenses?utm_source=x#top", "example.org", "/best-lenses")]
    [InlineData("https://example.org/", "example.org", "/")]
    [InlineData("https://example.org/Category/News/", "example.org", "/category/news")]
    [InlineData("http://localhost:5099/a-post", "localhost:5099", "/a-post")]
    public void A_page_of_this_site_is_kept_as_its_path(string referer, string host, string expected) =>
        Assert.Equal(expected, SignupSource.FromReferer(referer, host));

    [Theory]
    [InlineData("https://other.example/best-lenses", "example.org")]     // another site
    [InlineData("https://example.org.evil.example/x", "example.org")]   // a look-alike host
    [InlineData("https://example.org/newsletter/subscribe", "example.org")]   // the form's own page says nothing
    [InlineData("https://example.org/admin/posts", "example.org")]
    [InlineData("javascript:alert(1)", "example.org")]
    [InlineData("not a url", "example.org")]
    [InlineData("", "example.org")]
    [InlineData(null, "example.org")]
    [InlineData("https://example.org/x", "")]
    public void Anything_else_is_unknown(string? referer, string host) =>
        Assert.Null(SignupSource.FromReferer(referer, host));

    [Fact]
    public void A_very_long_path_is_cut_to_fit_the_column()
    {
        var path = SignupSource.FromReferer("https://example.org/" + new string('a', 500), "example.org");
        Assert.Equal(SignupSource.MaxLength, path!.Length);
    }

    [Fact]
    public void The_subscribe_action_records_it_and_the_report_counts_confirmed_people_only()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        string Read(params string[] p) => File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(p).ToArray()));
        Assert.Contains("SignupSource.FromReferer(Request.Headers.Referer.ToString(), Request.Host.Value)", Read("Blog.Web", "Controllers", "NewsletterController.cs"));
        var repo = Read("Blog.Infrastructure", "Data", "Repositories", "MemberRepository.cs");
        Assert.Contains("WHERE Status = 'confirmed' AND DeletedAt IS NULL AND SourcePath IS NOT NULL", repo);
        Assert.Contains("Pages that brought subscribers", Read("Blog.Web", "Views", "Subscribers", "Index.cshtml"));
    }
}
