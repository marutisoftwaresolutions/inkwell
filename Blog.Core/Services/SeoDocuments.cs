namespace Blog.Core.Services;

/// <summary>
/// Pure builders for the site's plain-text SEO/AEO documents. Kept free of web/DB types so the
/// exact crawler-permission strings can be unit-tested (this is where the Google-Extended token
/// bug lived — a test now guards it).
/// </summary>
public static class SeoDocuments
{
    /// <summary>Builds the dynamic robots.txt body for the given absolute base URL.</summary>
    public static string RobotsTxt(string baseUrl)
    {
        return $@"User-agent: *
Allow: /
Disallow: /admin/
Disallow: /account/
Disallow: /setup/
Disallow: /api/
Disallow: /search
Disallow: /preview/

# AI answer engines & search assistants — explicitly permitted for citation and indexing
User-agent: GPTBot
Allow: /

User-agent: OAI-SearchBot
Allow: /

User-agent: ChatGPT-User
Allow: /

User-agent: ClaudeBot
Allow: /

User-agent: anthropic-ai
Allow: /

User-agent: Claude-Web
Allow: /

User-agent: PerplexityBot
Allow: /

User-agent: Perplexity-User
Allow: /

# Google's AI/Gemini training & grounding control token (must be exactly Google-Extended)
User-agent: Google-Extended
Allow: /

User-agent: Applebot-Extended
Allow: /

User-agent: Amazonbot
Allow: /

User-agent: cohere-ai
Allow: /

User-agent: DuckDuckBot
Allow: /

User-agent: Bingbot
Allow: /

User-agent: FacebookBot
Allow: /

User-agent: Meta-ExternalAgent
Allow: /

# AI mass-training / scraping crawlers with no citation value — blocked
User-agent: CCBot
Disallow: /

User-agent: Bytespider
Disallow: /

User-agent: omgili
Disallow: /

Sitemap: {baseUrl}/sitemap.xml
";
    }
}
