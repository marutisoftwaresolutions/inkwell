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

    [Theory]
    // Exactly at the 60-char budget → brand is kept (57 + 3 + 0 … see lengths below).
    [InlineData("Optical POS Systems for Eye Care Retail 2026", "Optical Sw", "Optical POS Systems for Eye Care Retail 2026 | Optical Sw")]
    // One character over → suffix dropped, title survives intact.
    [InlineData("Optical POS Systems for Eye Care Retail: 2026 Guide", "Optical Software", "Optical POS Systems for Eye Care Retail: 2026 Guide")]
    // A long site name costs every title on that tenant — the guard protects them too.
    [InlineData("Best Optometry Practice Management Software 2026", "The Independent Optometry Technology Review", "Best Optometry Practice Management Software 2026")]
    public void BrandTitle_DropsSuffixWhenItWouldOverflow(string raw, string site, string expected)
    {
        Assert.Equal(expected, TextHelper.BrandTitle(raw, site));
    }

    [Fact]
    public void BrandTitle_NeverReturnsMoreThanBudget_UnlessTitleAloneExceedsIt()
    {
        // A branded result must always fit the budget. When the raw title alone is already over,
        // the method cannot help — but it must not make it worse by appending a brand.
        var site = "Optical Software";
        foreach (var raw in new[]
        {
            "Short",
            "Optical Retail Software: Increase Frame Sales",
            "Best AI Retinal Screening Software 2026 (FDA-Cleared)",
            new string('x', 80),
        })
        {
            var result = TextHelper.BrandTitle(raw, site);
            if (raw.Length <= TextHelper.MaxBrandedTitleLength)
                Assert.True(result.Length <= TextHelper.MaxBrandedTitleLength,
                    $"'{raw}' produced {result.Length} chars: {result}");
            Assert.StartsWith(raw, result); // the title itself is never truncated or altered
        }
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
        Assert.Contains("Disallow: /search", _robots);   // site search is noindex; keep crawl budget off it
        Assert.Contains("Disallow: /preview/", _robots); // tokenised draft previews must never be crawled
    }

    [Fact]
    public void RobotsTxt_ReferencesAbsoluteSitemap()
    {
        Assert.Contains("Sitemap: https://example.com/sitemap.xml", _robots);
    }
}
