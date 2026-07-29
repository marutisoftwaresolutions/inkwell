using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// SEO regression guards for the pure builders behind the site's search/AEO output. These lock in
/// the behaviour shipped in the SEO-AEO plan (title branding, meta-description truncation, and the
/// crawler-permission tokens — including the Google-Extended fix) so a future edit can't silently
/// regress them.
/// </summary>
public class TextHelperTests
{
    [Fact]
    public void SmartTruncate_ShortText_ReturnedUnchanged()
    {
        const string s = "A concise summary well under the limit.";
        Assert.Equal(s, TextHelper.SmartTruncate(s));
    }

    [Fact]
    public void SmartTruncate_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TextHelper.SmartTruncate(null));
        Assert.Equal(string.Empty, TextHelper.SmartTruncate("   "));
    }

    [Fact]
    public void SmartTruncate_CollapsesWhitespace()
    {
        Assert.Equal("one two three", TextHelper.SmartTruncate("one   two\n\tthree"));
    }

    [Fact]
    public void SmartTruncate_LongText_CutsOnWordBoundaryWithinLimit()
    {
        var text = string.Join(" ", System.Linq.Enumerable.Repeat("optometry", 40)); // ~399 chars
        var result = TextHelper.SmartTruncate(text, 158);

        Assert.True(result.Length <= 158, $"length was {result.Length}");
        Assert.EndsWith("…", result);
        // No dangling space before the ellipsis, and the body is a real prefix of the source (no mid-word cut).
        var body = result[..^1];
        Assert.False(body.EndsWith(" "));
        Assert.StartsWith(body, text);
        Assert.EndsWith("optometry", body); // whole last word, not "optom…"
    }

    [Theory]
    [InlineData("My Post", "Acme", "My Post | Acme")]
    [InlineData("Acme – Daily Tagline", "Acme", "Acme – Daily Tagline")] // already contains brand → unchanged
    [InlineData("all about acme products", "Acme", "all about acme products")] // case-insensitive contains
    [InlineData("", "Acme", "Acme")]
    [InlineData("My Post", "", "My Post")]
    public void BrandTitle_AppliesSingleSuffix(string raw, string site, string expected)
    {
        Assert.Equal(expected, TextHelper.BrandTitle(raw, site));
    }
}

public class LocaleHelperTests
{
    [Theory]
    [InlineData("en", "en_US")]
    [InlineData("es", "es_ES")]
    [InlineData("hi", "hi_IN")]
    [InlineData("fr-CA", "fr_CA")]   // already regioned → just swap separator
    [InlineData("pt-BR", "pt_BR")]
    [InlineData("nl", "nl")]         // unknown bare code → returned as-is
    [InlineData(null, "en_US")]      // empty → safe default
    [InlineData("", "en_US")]
    public void OgLocale_MapsBcp47ToOpenGraphLocale(string? lang, string expected)
    {
        Assert.Equal(expected, LocaleHelper.OgLocale(lang));
    }
}

public class SeoDocumentsTests
{
    private readonly string _robots = SeoDocuments.RobotsTxt("https://example.com");

    [Fact]
    public void RobotsTxt_UsesCorrectGoogleAiToken()
    {
        Assert.Contains("User-agent: Google-Extended", _robots);
        Assert.DoesNotContain("Googlebot-Extended", _robots); // the bug we fixed must never come back
    }

    [Theory]
    [InlineData("User-agent: GPTBot")]
    [InlineData("User-agent: OAI-SearchBot")]
    [InlineData("User-agent: ClaudeBot")]
    [InlineData("User-agent: PerplexityBot")]
    [InlineData("User-agent: Bingbot")]
    public void RobotsTxt_AllowsKeyAiAndSearchBots(string token)
    {
        Assert.Contains(token, _robots);
    }

    [Fact]
    public void RobotsTxt_BlocksScrapersAndPrivateAreas()
    {
        Assert.Contains("User-agent: CCBot", _robots);
        Assert.Contains("User-agent: Bytespider", _robots);
        Assert.Contains("Disallow: /admin/", _robots);
        Assert.Contains("Disallow: /api/", _robots);
    }

    [Fact]
    public void RobotsTxt_ReferencesAbsoluteSitemap()
    {
        Assert.Contains("Sitemap: https://example.com/sitemap.xml", _robots);
    }
}
