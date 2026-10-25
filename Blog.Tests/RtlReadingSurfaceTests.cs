using System.Text.RegularExpressions;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Right-to-left reading surface (v1.0.9, ADR-0035): the direction follows the site language, and the reader views and
/// stylesheets stay on logical (start/end) properties so every layout mirrors. The Desk stays left to right.
/// </summary>
public class RtlReadingSurfaceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public RtlReadingSurfaceTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    [Theory]
    [InlineData("ar", "rtl")]
    [InlineData("ar-EG", "rtl")]
    [InlineData("he", "rtl")]
    [InlineData("fa-IR", "rtl")]
    [InlineData("ur", "rtl")]
    [InlineData("pa-Arab", "rtl")]
    [InlineData("ku-Arab", "rtl")]
    [InlineData("ku-Latn", "ltr")]
    [InlineData("az-Latn", "ltr")]
    [InlineData("en", "ltr")]
    [InlineData("en_US", "ltr")]
    [InlineData("hi", "ltr")]
    [InlineData("", "ltr")]
    [InlineData(null, "ltr")]
    [InlineData("---", "ltr")]
    public void Direction_follows_the_language(string? tag, string expected) => Assert.Equal(expected, TextDirection.For(tag));

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "Blog.Web");
    }

    private static IEnumerable<string> ReaderViews()
    {
        var root = Root();
        return Directory.GetFiles(Path.Combine(root, "Views", "Blog"), "*.cshtml")
            .Concat(Directory.GetFiles(Path.Combine(root, "Views", "Shared", "Components"), "*.cshtml", SearchOption.AllDirectories))
            .Concat(new[] { "_PublicLayout.cshtml", "_CustomFieldDetails.cshtml", "_NewsletterSignup.cshtml" }.Select(f => Path.Combine(root, "Views", "Shared", f)))
            .Where(File.Exists);
    }

    private static readonly Regex PhysicalClass = new(@"^(?:[a-z0-9]+:)*-?(m[lr]|p[lr]|left|right|border-[lr]|rounded-[lr]|rounded-[tb][lr]|text-left|text-right|float-left|float-right)(-|$)");
    private static readonly Regex PhysicalProperty = new(@"(?<![-\w])(margin|padding|border)-(left|right)(-\w+)?\s*:|text-align\s*:\s*(left|right)\b|float\s*:\s*(left|right)\b");

    [Fact]
    public void Reader_views_use_logical_classes_and_properties()
    {
        var problems = new List<string>();
        foreach (var file in ReaderViews())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, "class=\"([^\"]*)\""))
            {
                var cls = m.Groups[1].Value;
                var centred = Regex.IsMatch(cls, @"(^|\s)left-1/2(\s|$)") && cls.Contains("-translate-x-1/2");   // centring is direction-neutral
                foreach (var t in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (PhysicalClass.IsMatch(t) && !(centred && t == "left-1/2"))
                        problems.Add($"{Path.GetFileName(file)}: class {t}");
            }
            foreach (Match m in PhysicalProperty.Matches(text))
                problems.Add($"{Path.GetFileName(file)}: {m.Value.Trim()}");
        }
        Assert.True(problems.Count == 0, "Use start/end (ms-*, ps-*, start-*, border-s, text-start, margin-inline-start …) so RTL sites mirror:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Reader_stylesheets_use_logical_properties()
    {
        var problems = new List<string>();
        foreach (var name in new[] { "inkwell.css", "neutral-blog.css" })
        {
            var css = File.ReadAllText(Path.Combine(Root(), "wwwroot", "css", name));
            // The drop cap keeps physical values on purpose (Chromium's ::first-letter) and is switched off in RTL.
            css = Regex.Replace(css, @"\.dropcap::first-letter\s*\{[^}]*\}", "");
            foreach (Match m in PhysicalProperty.Matches(css)) problems.Add($"{name}: {m.Value.Trim()}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Only_the_public_layout_reads_the_direction_and_the_honeypots_never_scroll_an_rtl_page()
    {
        var layout = File.ReadAllText(Path.Combine(Root(), "Views", "Shared", "_PublicLayout.cshtml"));
        Assert.Contains("<html lang=\"@siteLang\" dir=\"@siteDir\"", layout);
        Assert.DoesNotContain("dir=", File.ReadAllText(Path.Combine(Root(), "Views", "Shared", "_AdminLayout.cshtml")).Split("<head>")[0]);
        // A field parked at left:-10000px would add a 10,000px sideways scroll to a right-to-left page.
        Assert.Contains("inset-inline-start:-10000px", File.ReadAllText(Path.Combine(Root(), "Views", "Blog", "_CommentSpamFields.cshtml")).Replace(" ", ""));
        Assert.Contains("inset-inline-start: -10000px", File.ReadAllText(Path.Combine(Root(), "wwwroot", "css", "neutral-blog.css")));
    }

    [Fact]
    public async Task An_arabic_site_renders_right_to_left()
    {
        try
        {
            var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            await probe.OpenAsync();
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISettingRepository>();
        var owner = (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync())!.Id;
        var settings = await repo.GetSettingsAsync(owner);
        var original = settings.SiteLanguage;
        try
        {
            settings.SiteLanguage = "ar";
            await repo.SaveSettingsAsync(owner, settings);
            Assert.Contains("<html lang=\"ar\" dir=\"rtl\"", await _factory.CreateClient().GetStringAsync("/"));
            settings.SiteLanguage = "en";
            await repo.SaveSettingsAsync(owner, settings);
            Assert.Contains("<html lang=\"en\" dir=\"ltr\"", await _factory.CreateClient().GetStringAsync("/"));
        }
        finally
        {
            settings.SiteLanguage = original;
            await repo.SaveSettingsAsync(owner, settings);
        }
    }
}
