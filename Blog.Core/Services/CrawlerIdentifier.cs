namespace Blog.Core.Services;

/// <summary>A crawler we can name, and who operates it.</summary>
/// <param name="Name">Canonical bot name as its operator publishes it, e.g. "GPTBot".</param>
/// <param name="Operator">Who runs it, e.g. "OpenAI" — the useful grouping for a report.</param>
/// <param name="IsAi">True for AI answer engines and training crawlers; false for classic search.</param>
/// <param name="Purpose">What the operator says it is for, so an operator can decide about robots.txt.</param>
public record CrawlerIdentity(string Name, string Operator, bool IsAi, string Purpose);

/// <summary>
/// Names the crawler behind a request from its user-agent.
///
/// Deliberately conservative: it only reports crawlers whose tokens are published by their operator,
/// and returns null for anything else rather than guessing. A user-agent is self-asserted and can be
/// forged, so this identifies *claimed* identity — fine for a visibility report, and never used for
/// an access decision (the firewall makes those on behaviour, not on a name).
///
/// Tokens change. Re-verify against each operator's published documentation rather than trusting
/// this list indefinitely — the SEO/AEO Rule requires it, and `Googlebot-Extended` was already wrong
/// here once before being corrected to `Google-Extended`.
/// </summary>
public static class CrawlerIdentifier
{
    // Longest, most specific tokens first: "ChatGPT-User" must win over a bare "chatgpt", and
    // "Applebot-Extended" over "Applebot".
    private static readonly (string Token, CrawlerIdentity Identity)[] _known =
    [
        // ── AI answer engines and training crawlers ──────────────────────────
        ("oai-searchbot",      new("OAI-SearchBot", "OpenAI",      true,  "Surfacing pages in ChatGPT search results")),
        ("chatgpt-user",       new("ChatGPT-User",  "OpenAI",      true,  "Fetching a page because a user asked ChatGPT about it")),
        ("gptbot",             new("GPTBot",        "OpenAI",      true,  "Training data collection")),
        ("claudebot",          new("ClaudeBot",     "Anthropic",   true,  "Training data collection")),
        ("claude-web",         new("Claude-Web",    "Anthropic",   true,  "Fetching a page for a Claude answer")),
        ("anthropic-ai",       new("anthropic-ai",  "Anthropic",   true,  "Training data collection")),
        ("perplexity-user",    new("Perplexity-User","Perplexity", true,  "Fetching a page for a user's question")),
        ("perplexitybot",      new("PerplexityBot", "Perplexity",  true,  "Indexing for Perplexity answers")),
        ("google-extended",    new("Google-Extended","Google",     true,  "Gemini training and grounding")),
        ("applebot-extended",  new("Applebot-Extended","Apple",    true,  "Apple Intelligence training")),
        ("duckassistbot",      new("DuckAssistBot", "DuckDuckGo",  true,  "DuckAssist answers")),
        ("meta-externalagent", new("Meta-ExternalAgent","Meta",    true,  "Meta AI training")),
        ("bytespider",         new("Bytespider",    "ByteDance",   true,  "Training data collection")),
        ("ccbot",              new("CCBot",         "Common Crawl",true,  "Open crawl corpus used to train many models")),
        ("cohere-ai",          new("cohere-ai",     "Cohere",      true,  "Training data collection")),
        ("amazonbot",          new("Amazonbot",     "Amazon",      true,  "Alexa and Amazon services")),

        // ── Classic search, kept for contrast ────────────────────────────────
        ("googlebot",          new("Googlebot",     "Google",      false, "Google Search indexing")),
        ("bingbot",            new("Bingbot",       "Microsoft",   false, "Bing Search indexing")),
        ("duckduckbot",        new("DuckDuckBot",   "DuckDuckGo",  false, "DuckDuckGo indexing")),
        ("yandexbot",          new("YandexBot",     "Yandex",      false, "Yandex indexing")),
        ("baiduspider",        new("Baiduspider",   "Baidu",       false, "Baidu indexing")),
        ("applebot",           new("Applebot",      "Apple",       false, "Siri and Spotlight")),
        ("slurp",              new("Slurp",         "Yahoo",       false, "Yahoo indexing"))
    ];

    /// <summary>The crawler behind this user-agent, or null when it is not one we can name.</summary>
    public static CrawlerIdentity? Identify(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;

        var ua = userAgent.ToLowerInvariant();
        foreach (var (token, identity) in _known)
            if (ua.Contains(token, StringComparison.Ordinal)) return identity;

        return null;
    }

    /// <summary>True when the request came from an AI engine rather than a classic search crawler.</summary>
    public static bool IsAiCrawler(string? userAgent) => Identify(userAgent)?.IsAi == true;

    /// <summary>Every crawler this build can name — used by the report to show zero rows honestly.</summary>
    public static IReadOnlyList<CrawlerIdentity> Known =>
        _known.Select(k => k.Identity).ToList();
}
