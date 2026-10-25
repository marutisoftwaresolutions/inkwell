using System.Net;
using Blog.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// The running version is visible to staff in the Desk (sidebar and dashboard) and never sent to readers: a version
/// number on public pages only helps scanners match known vulnerabilities.
/// </summary>
public class AppVersionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public AppVersionTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    private static string Repo(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void The_version_is_the_stamped_one_without_build_metadata()
    {
        var props = Repo("Directory.Build.props");
        var stamped = System.Text.RegularExpressions.Regex.Match(props, "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.Equal(stamped, AppVersion.Current);
        Assert.DoesNotContain('+', AppVersion.Current);
    }

    [Fact]
    public void The_desk_shows_it_and_public_views_do_not()
    {
        Assert.Contains("AppVersion.Current", Repo("Blog.Web", "Views", "Shared", "_AdminLayout.cshtml"));
        Assert.Contains("AppVersion.Current", Repo("Blog.Web", "Views", "Dashboard", "Index.cshtml"));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        foreach (var file in Directory.GetFiles(Path.Combine(root!.FullName, "Blog.Web", "Views"), "*.cshtml", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root.FullName, file).Replace('\\', '/');
            if (rel.EndsWith("_AdminLayout.cshtml") || rel.EndsWith("Dashboard/Index.cshtml")) continue;
            Assert.False(File.ReadAllText(file).Contains("AppVersion.Current"), rel + " shows the running version");   // a backup's recorded version is fine
        }
    }

    [Fact]
    public async Task No_public_response_carries_the_version()
    {
        try
        {
            var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            await conn.OpenAsync();
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return; }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var url in new[] { "/", "/robots.txt", "/llms.txt", "/sitemap.xml", "/feed", "/no-such-page-zz9", "/account/login" })
        {
            var res = await client.GetAsync(url);
            var body = await res.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Inkwell " + AppVersion.Current, body);
            Assert.False(res.Headers.Contains("X-Powered-By") || res.Content.Headers.Contains("X-Powered-By"), url + " sends X-Powered-By");
        }
    }
}
