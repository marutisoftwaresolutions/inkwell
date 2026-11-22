using System.Net;

namespace Blog.Core.Services;

/// <summary>
/// Maintenance mode: while it is on, readers get a holding page with status 503 and a Retry-After
/// header (which tells search engines the outage is temporary, so nothing is dropped from their
/// index), and signed-in staff see the site as usual. This type decides which addresses the holding
/// page covers and builds the page. Pure: the middleware supplies the setting and the signed-in state.
/// </summary>
public static class MaintenanceMode
{
    public const int MaxMessageLength = 500;

    /// <summary>What search engines are told to wait before trying again: one hour.</summary>
    public const int RetryAfterSeconds = 3600;

    public const string DefaultMessage = "We are making some changes and will be back shortly.";

    // Addresses that must keep working: the Desk and sign-in (or nobody could switch it off), the API
    // and MCP endpoint (they authenticate by key and answer for themselves), setup, health checks,
    // error pages, robots.txt (a 503 there stops crawling of everything), security.txt, the page-view
    // beacon, staff avatars, and the links in emails already sent (confirm, unsubscribe).
    private static readonly string[] OpenPrefixes =
    {
        "/admin", "/account", "/api", "/mcp", "/setup", "/health", "/error", "/.well-known", "/pv", "/avatar",
        "/newsletter/confirm", "/newsletter/unsubscribe",
    };

    /// <summary>True when the holding page stands in for this address while maintenance mode is on.</summary>
    public static bool Covers(string? path)
    {
        var p = string.IsNullOrEmpty(path) ? "/" : path;
        if (p.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var prefix in OpenPrefixes)
        {
            if (p.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (p.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>The message as stored: trimmed, control characters removed, capped.</summary>
    public static string CleanMessage(string? message)
    {
        var text = new string((message ?? "").Where(c => !char.IsControl(c) || c is '\n').ToArray()).Replace("\n", " ").Trim();
        return text.Length > MaxMessageLength ? text[..MaxMessageLength].TrimEnd() : text;
    }

    /// <summary>
    /// The holding page: one self-contained document with no script, no outside file and no link into
    /// the site, readable in both colour schemes. Everything from settings is HTML-encoded.
    /// </summary>
    public static string Page(string? siteName, string? message, string? language, string? direction)
    {
        var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(siteName) ? "This site" : siteName.Trim());
        var cleaned = CleanMessage(message);
        var text = WebUtility.HtmlEncode(cleaned.Length == 0 ? DefaultMessage : cleaned);
        var lang = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(language) ? "en" : language.Trim());
        var dir = direction == "rtl" ? "rtl" : "ltr";
        return
            "<!DOCTYPE html>\n" +
            $"<html lang=\"{lang}\" dir=\"{dir}\">\n<head>\n<meta charset=\"UTF-8\">\n" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n" +
            "<meta name=\"robots\" content=\"noindex\">\n" +
            $"<title>{name}: back shortly</title>\n" +
            "<style>\n" +
            ":root{color-scheme:light dark;--bg:#F4EFE6;--fg:#16140F;--meta:#6B5B45;--line:#D9CFBE}\n" +
            "@media (prefers-color-scheme:dark){:root{--bg:#16140F;--fg:#F4EFE6;--meta:#B49A78;--line:#332E26}}\n" +
            "html,body{height:100%;margin:0}\n" +
            "body{display:flex;align-items:center;justify-content:center;background:var(--bg);color:var(--fg);font:18px/1.6 Georgia,'Times New Roman',serif;padding:24px;box-sizing:border-box}\n" +
            "main{max-width:34rem;text-align:center;border-top:1px solid var(--line);border-bottom:1px solid var(--line);padding:2.5rem 0}\n" +
            "h1{font-size:1.75rem;line-height:1.25;margin:0 0 1rem}\n" +
            "p{margin:0}\n" +
            ".site{font:600 .8rem/1.4 system-ui,sans-serif;letter-spacing:.08em;text-transform:uppercase;color:var(--meta);margin-bottom:1rem}\n" +
            "</style>\n</head>\n<body>\n<main>\n" +
            $"<p class=\"site\">{name}</p>\n<h1>Back shortly</h1>\n<p>{text}</p>\n" +
            "</main>\n</body>\n</html>\n";
    }
}
