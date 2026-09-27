namespace Blog.Core.Services;

/// <summary>
/// Decides whether a User-Agent is non-human for page-view counting. Token-based and deliberately
/// broad: a missed human costs one view, a counted bot inflates every report. Note that this cannot
/// catch a client that sends an ordinary browser User-Agent — server-side counting will always
/// over-count such traffic compared with a JavaScript-based analytics tool.
/// </summary>
public static class BotUserAgent
{
    // Checked with Contains() on the lower-cased UA — order does not matter.
    private static readonly string[] Tokens =
    [
        // Generic crawler signals
        "bot", "crawler", "spider", "scraper", "scan", "check", "monitor",
        // CLI / scripting tools
        "curl", "wget", "python", "ruby", "java", "go-http", "axios", "node-fetch",
        "libwww", "okhttp", "requests", "httpx", "aiohttp", "scrapy",
        // Headless / automation
        "headless", "puppeteer", "playwright", "selenium", "phantomjs", "nightmare",
        // Audit / perf tools
        "lighthouse", "pagespeed", "gtmetrix", "pingdom", "uptimerobot", "statuscake",
        "newrelic", "datadog", "dynatrace",
        // SEO / link tools
        "prerender", "semrush", "ahrefs", "moz.com", "majestic", "screaming frog",
        // Feed readers / aggregators
        "feedfetcher", "feedburner", "feedly", "rss", "podcast",
        // AI / LLM crawlers
        "gptbot", "claudebot", "anthropic", "perplexity", "bytespider",
        "amazonbot", "applebot", "facebookexternalhit",
        // Google fetchers whose User-Agent carries no "bot" token (they present a Nexus 5X /
        // desktop Chrome string plus the product name)
        "googleother", "google-inspectiontool", "google-read-aloud", "google-safety",
        "mediapartners-google", "apis-google", "google-pagerenderer", "google-site-verification",
    ];

    public static bool IsBot(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return true;
        var ua = userAgent.ToLowerInvariant();
        foreach (var token in Tokens)
            if (ua.Contains(token)) return true;
        return false;
    }
}
