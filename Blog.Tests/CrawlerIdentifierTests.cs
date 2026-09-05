using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Naming the crawler drives the whole report, and the ordering traps are real: several operators
/// ship two bots whose tokens are prefixes of each other.
/// </summary>
public class CrawlerIdentifierTests
{
    [Theory]
    [InlineData("Mozilla/5.0 AppleWebKit/537.36 (compatible; GPTBot/1.1; +https://openai.com/gptbot)", "GPTBot", "OpenAI")]
    [InlineData("Mozilla/5.0 (compatible; ClaudeBot/1.0; +claudebot@anthropic.com)", "ClaudeBot", "Anthropic")]
    [InlineData("Mozilla/5.0 (compatible; PerplexityBot/1.0; +https://perplexity.ai/bot)", "PerplexityBot", "Perplexity")]
    [InlineData("Mozilla/5.0 (compatible; CCBot/2.0; +https://commoncrawl.org/faq/)", "CCBot", "Common Crawl")]
    [InlineData("Mozilla/5.0 (compatible; Bytespider; https://zhanzhang.toutiao.com/)", "Bytespider", "ByteDance")]
    public void Ai_crawlers_are_named_with_their_operator(string ua, string name, string op)
    {
        var id = CrawlerIdentifier.Identify(ua);

        Assert.NotNull(id);
        Assert.Equal(name, id!.Name);
        Assert.Equal(op, id.Operator);
        Assert.True(id.IsAi);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", "Googlebot")]
    [InlineData("Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)", "Bingbot")]
    public void Search_crawlers_are_named_but_not_marked_as_ai(string ua, string name)
    {
        var id = CrawlerIdentifier.Identify(ua);

        Assert.Equal(name, id!.Name);
        Assert.False(id.IsAi);
        Assert.False(CrawlerIdentifier.IsAiCrawler(ua));
    }

    // ── Prefix collisions: the ordering that matters ──────────────────────────

    [Fact]
    public void ChatGPT_User_is_not_mistaken_for_GPTBot()
    {
        var id = CrawlerIdentifier.Identify("Mozilla/5.0 (compatible; ChatGPT-User/1.0; +https://openai.com/bot)");

        Assert.Equal("ChatGPT-User", id!.Name);
    }

    [Fact]
    public void Applebot_Extended_is_not_mistaken_for_Applebot()
    {
        // One is AI training, the other is Siri indexing — conflating them misreports both.
        var extended = CrawlerIdentifier.Identify("Mozilla/5.0 (compatible; Applebot-Extended/1.0)");
        var plain = CrawlerIdentifier.Identify("Mozilla/5.0 (compatible; Applebot/0.1)");

        Assert.Equal("Applebot-Extended", extended!.Name);
        Assert.True(extended.IsAi);

        Assert.Equal("Applebot", plain!.Name);
        Assert.False(plain.IsAi);
    }

    [Fact]
    public void Google_Extended_is_not_mistaken_for_Googlebot()
    {
        var extended = CrawlerIdentifier.Identify("Mozilla/5.0 (compatible; Google-Extended)");

        Assert.Equal("Google-Extended", extended!.Name);
        Assert.True(extended.IsAi);
    }

    [Fact]
    public void Perplexity_User_is_not_mistaken_for_PerplexityBot()
    {
        Assert.Equal("Perplexity-User",
            CrawlerIdentifier.Identify("Mozilla/5.0 (compatible; Perplexity-User/1.0)")!.Name);
    }

    // ── Everything else ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/126.0 Safari/537.36")]
    [InlineData("python-requests/2.31.0")]
    [InlineData("curl/8.4.0")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_unrecognised_is_left_unnamed(string? ua)
    {
        // Guessing would put fabricated names in a report an operator makes decisions from.
        Assert.Null(CrawlerIdentifier.Identify(ua));
        Assert.False(CrawlerIdentifier.IsAiCrawler(ua));
    }

    [Fact]
    public void Matching_ignores_case() =>
        Assert.Equal("GPTBot", CrawlerIdentifier.Identify("mozilla/5.0 (compatible; gptbot/1.1)")!.Name);

    [Fact]
    public void The_known_list_is_exposed_so_a_report_can_show_zero_honestly()
    {
        // A crawler that has not visited should be shown as zero, not omitted — absence is a finding.
        Assert.Contains(CrawlerIdentifier.Known, c => c.Name == "GPTBot");
        Assert.Contains(CrawlerIdentifier.Known, c => c.Name == "ClaudeBot");
        Assert.True(CrawlerIdentifier.Known.Count(c => c.IsAi) >= 10);
    }

    [Fact]
    public void Every_known_crawler_states_a_purpose() =>
        Assert.All(CrawlerIdentifier.Known, c => Assert.False(string.IsNullOrWhiteSpace(c.Purpose)));
}
