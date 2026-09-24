using System.Text.RegularExpressions;

namespace Blog.Web.Services.Security;

/// <summary>The threat weight assigned to one request, with a human-readable justification.</summary>
public readonly record struct ThreatVerdict(int Score, string Reason)
{
    public static readonly ThreatVerdict None = new(0, "");
    public bool IsThreat => Score > 0;
}

/// <summary>
/// Pure scoring rules for the IP firewall. Every request that fails is weighed here; the caller
/// accumulates the score per IP over a sliding window and blocks once it crosses the threshold.
///
/// Deliberately tiered so a single hostile fingerprint counts far more than ordinary noise:
/// two exploit probes block an address, while it takes ten harmless 404s to do the same.
/// </summary>
public static class ThreatScorer
{
    /// <summary>Unmistakable exploit/scanner fingerprint (PHP shell, wp-login, traversal, .env …).</summary>
    public const int ProbeScore = 5;
    /// <summary>A request for something that does not exist — ordinary crawl noise on its own.</summary>
    public const int MissScore = 1;
    /// <summary>Hitting a protected surface without credentials.</summary>
    public const int ForbiddenScore = 2;
    /// <summary>A rejected sign-in — brute-force signal (recorded by AccountController).</summary>
    public const int FailedLoginScore = 3;
    /// <summary>
    /// A comment submission the spam filter discarded (recorded by BlogController). Weighted like a
    /// failed sign-in: one is noise, a burst from one address is a bot, and it should block itself.
    /// </summary>
    public const int CommentSpamScore = 3;

    // Paths no legitimate visitor or search engine ever requests on a .NET blog: PHP/JSP/CGI
    // handlers, WordPress and phpMyAdmin surfaces, VCS and secret files, traversal, and the
    // usual app-server consoles. A single hit here is strong evidence of an automated scanner.
    private static readonly Regex _probePath = new(@"
          \.(php\d?|phtml|phar|asp|aspx|jsp|jspx|cgi|pl|py|sh|bak|old|orig|save|swp|sql|env|ini|htaccess|htpasswd)(/|$)
        | /(wp-admin|wp-login|wp-content|wp-includes|wp-json|wp-config|wordpress|xmlrpc)
        | /wp(/|$)
        | /(phpmyadmin|phpmyadm|pma|myadmin|mysqladmin|adminer|dbadmin|sqlmanager)
        | /\.(git|svn|hg|bzr|env|aws|ssh|npmrc|docker|vscode|idea)(/|$)
        | (\.\./|\.\.%2f|%2e%2e|%252e%252e)
        | /(etc/passwd|proc/self|windows/win\.ini|boot\.ini)
        | /(actuator|solr|jenkins|druid|telescope|struts|jmx-console|invoker|manager/html|console/login)
        | /(shell|webshell|c99|r57|alfa|eval-stdin|cmd\.exe|bash_history)
        | /(owa|autodiscover|ecp|exchange)/
        | /(id_rsa|credentials|config\.json\.bak|backup\.(zip|tar|gz|sql))
        | /(cgi-bin|vendor/phpunit|fckeditor|kcfinder|tiny_mce)
        ", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace);

    // Injection payloads in the query string. Not applied to /search, where a visitor may
    // legitimately type any of these words.
    private static readonly Regex _probeQuery = new(@"
          union(\s|%20|\+)+select
        | information_schema
        | (sleep|benchmark|waitfor(\s|%20|\+)+delay)\s*\(
        | <script | onerror\s*= | javascript:
        | base64_decode | php://|data://|file://
        | /etc/passwd
        | (\bexec\b|\bsystem\b|passthru|shell_exec)\s*\(
        ", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace);

    // Search and AI crawlers we must never lock out — losing Googlebot costs far more than the
    // marginal risk that an attacker spoofs the header. Spoofing only exempts an address from
    // *miss-based* scoring; probe paths still score, because real crawlers never request them.
    private static readonly string[] _protectedCrawlers =
    [
        "googlebot", "google-inspectiontool", "adsbot-google", "mediapartners-google",
        "bingbot", "adidxbot", "msnbot", "duckduckbot", "yandexbot", "baiduspider",
        "slurp", "applebot", "sogou", "seznambot", "naver", "petalbot",
        "gptbot", "oai-searchbot", "chatgpt-user", "claudebot", "claude-web", "anthropic-ai",
        "perplexitybot", "ccbot", "google-extended",
        "facebookexternalhit", "twitterbot", "linkedinbot", "slackbot", "discordbot", "telegrambot"
    ];

    public static bool IsProtectedCrawler(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return false;
        var ua = userAgent.ToLowerInvariant();
        foreach (var token in _protectedCrawlers)
            if (ua.Contains(token, StringComparison.Ordinal)) return true;
        return false;
    }

    public static bool IsProbe(string path, string? queryString = null)
    {
        if (!string.IsNullOrEmpty(path) && _probePath.IsMatch(path)) return true;
        return !string.IsNullOrEmpty(queryString) && _probeQuery.IsMatch(queryString);
    }

    /// <summary>
    /// Weigh one completed request.
    /// </summary>
    /// <param name="path">Request path, e.g. <c>/wp-login.php</c>.</param>
    /// <param name="queryString">Raw query string including '?', or null.</param>
    /// <param name="statusCode">Final response status code.</param>
    /// <param name="isProtectedCrawler">
    /// True when the User-Agent is a search/AI crawler we protect. Suppresses miss/forbidden
    /// scoring only — probe fingerprints still count.
    /// </param>
    public static ThreatVerdict Score(string path, string? queryString, int statusCode, bool isProtectedCrawler)
    {
        path ??= "/";

        // /search legitimately carries arbitrary user text — never read it as an injection payload.
        var scanQuery = !path.StartsWith("/search", StringComparison.OrdinalIgnoreCase);

        if (_probePath.IsMatch(path))
            return new ThreatVerdict(ProbeScore, $"Exploit probe: {Truncate(path, 120)}");

        if (scanQuery && !string.IsNullOrEmpty(queryString) && _probeQuery.IsMatch(queryString))
            return new ThreatVerdict(ProbeScore, $"Injection attempt in query: {Truncate(path, 80)}");

        // Everything below is ambiguous on its own, so a protected crawler is never penalised
        // for it — stale links and 404s are a normal part of crawling.
        if (isProtectedCrawler) return ThreatVerdict.None;

        if (statusCode is 401 or 403)
            return new ThreatVerdict(ForbiddenScore, $"Unauthorized access attempt: {Truncate(path, 120)}");

        // 405 is deliberately absent: this deployment answers *every* HEAD request with 405,
        // including HEAD / and HEAD /robots.txt (verified on production 2026-08-15). Scoring it
        // would block uptime monitors and link checkers that legitimately poll with HEAD, while
        // catching nothing — a HEAD-based scanner still trips the probe rules above.
        if (statusCode is 404 or 410 or 414 or 400)
            return new ThreatVerdict(MissScore, $"{statusCode} on {Truncate(path, 120)}");

        return ThreatVerdict.None;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
