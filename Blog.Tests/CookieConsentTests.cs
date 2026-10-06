using System.Text.Json;
using System.Text.RegularExpressions;
using Blog.Core.Domain;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Cookie consent (v1.0.8): on by default, including for stored settings that predate the field;
/// with it on, Google Analytics never appears in the server-rendered page and is loaded only by the
/// consent script after the reader accepts; Reject is as easy as Accept.
/// </summary>
public class CookieConsentTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void Banner_is_on_by_default_even_for_settings_saved_before_it_existed()
    {
        Assert.True(new UserSettings().CookieConsentEnabled);
        var old = JsonSerializer.Deserialize<UserSettings>("{\"SiteName\":\"Old site\",\"GoogleAnalyticsId\":\"G-ABC123XYZ9\"}")!;
        Assert.True(old.CookieConsentEnabled);
        var off = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(new UserSettings { CookieConsentEnabled = false }))!;
        Assert.False(off.CookieConsentEnabled);
    }

    [Theory]
    [InlineData(true,  "G-ABC123XYZ9", CookieConsentMode.AskForAnalytics)]
    [InlineData(true,  "",             CookieConsentMode.EssentialOnlyNotice)]
    [InlineData(true,  null,           CookieConsentMode.EssentialOnlyNotice)]
    [InlineData(true,  "not-an-id",    CookieConsentMode.EssentialOnlyNotice)]
    [InlineData(false, "G-ABC123XYZ9", CookieConsentMode.Off)]
    [InlineData(false, "",             CookieConsentMode.Off)]
    public void Mode_follows_the_setting_and_the_analytics_id(bool enabled, string? id, CookieConsentMode expected) =>
        Assert.Equal(expected, CookieConsent.Decide(enabled, id));

    [Theory]
    [InlineData("G-ABC123XYZ9", true)]
    [InlineData(" G-ABC123XYZ9 ", true)]
    [InlineData("UA-12345678-1", true)]
    [InlineData("GT-ABCD1234", true)]
    [InlineData("g-abc123", false)]
    [InlineData("G-ABC');alert(1)//", false)]
    [InlineData("G-\"><script>", false)]
    [InlineData("", false)]
    public void Only_real_google_tag_ids_are_emitted(string id, bool valid) =>
        Assert.Equal(valid, CookieConsent.IsValidAnalyticsId(id));

    [Fact]
    public void Public_layout_puts_analytics_in_the_page_only_when_the_banner_is_off()
    {
        var layout = RepoFile("Blog.Web", "Views", "Shared", "_PublicLayout.cshtml");
        Assert.DoesNotContain("globalSettings.GoogleAnalyticsId)", layout.Replace("CookieConsent.Decide(globalSettings.CookieConsentEnabled, globalSettings.GoogleAnalyticsId)", "")
                                                                         .Replace("IsValidAnalyticsId(globalSettings.GoogleAnalyticsId)", ""));
        Assert.Contains("var analyticsInHead = analyticsId is not null && consentMode == Blog.Core.Services.CookieConsentMode.Off;", layout);
        // Every googletagmanager reference (preconnect and script) sits inside an analyticsInHead block.
        var gtm = Regex.Matches(layout, "googletagmanager").Count;
        var guarded = Regex.Matches(layout, @"@if \(analyticsInHead\)\s*\{[^}]*googletagmanager", RegexOptions.Singleline).Count;
        Assert.Equal(2, gtm);
        Assert.Equal(2, guarded);
        Assert.Contains("<partial name=\"_CookieConsent\"", layout);
    }

    [Fact]
    public void Banner_offers_reject_as_plainly_as_accept_and_a_way_back()
    {
        var banner = RepoFile("Blog.Web", "Views", "Shared", "_CookieConsent.cshtml");
        var accept = Regex.Match(banner, "<button[^>]*data-consent=\"granted\"[^>]*>").Value;
        var reject = Regex.Match(banner, "<button[^>]*data-consent=\"denied\"[^>]*>").Value;
        Assert.NotEmpty(accept);
        Assert.Equal(accept.Replace("granted", "X"), reject.Replace("denied", "X"));      // same element, same classes
        Assert.Contains("data-cookie-settings", banner);
        Assert.Contains("aria-labelledby=\"ink-consent-title\"", banner);

        var js = RepoFile("Blog.Web", "wwwroot", "js", "inkwell-consent.js");
        Assert.Contains("SameSite=Lax", js);
        Assert.Contains("ga-disable-", js);                                                // withdrawing stops the tag
        Assert.DoesNotContain("localStorage", js);
    }
}
