using Microsoft.AspNetCore.Http;

namespace Blog.Web.Services;

public static class VisitSourceClassifier
{
    private static readonly string[] SearchEngines =
        ["google", "bing", "yahoo", "duckduckgo", "baidu", "yandex", "ecosia", "brave", "ask", "aol"];

    private static readonly string[] SocialNetworks =
        ["facebook", "twitter", "x.com", "t.co", "linkedin", "instagram", "pinterest",
         "reddit", "youtube", "tiktok", "snapchat", "whatsapp", "telegram", "quora"];

    public static (string Source, string Medium) Classify(string? referrer, IQueryCollection query)
    {
        // UTM parameters take highest priority
        if (query.TryGetValue("utm_source", out var utmSource) && !string.IsNullOrWhiteSpace(utmSource))
        {
            var medium = query.TryGetValue("utm_medium", out var utmMedium) ? utmMedium.ToString() : "unknown";
            // Some assistants mark the links they cite with utm_source alone ("chatgpt.com"): that is an AI visit, not an unknown one.
            if (medium == "unknown" && Blog.Core.Services.AiReferrers.NameFor(utmSource.ToString()) is not null)
                medium = Blog.Core.Services.AiReferrers.Medium;
            return (utmSource.ToString().ToLowerInvariant(), medium.ToLowerInvariant());
        }

        if (string.IsNullOrWhiteSpace(referrer))
            return ("direct", "none");

        if (!Uri.TryCreate(referrer, UriKind.Absolute, out var uri))
            return ("direct", "none");

        // Only a leading "www." is dropped. (TrimStart('w', '.') used to eat every leading w: "wired.com" was stored as "ired.com".)
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];

        // Before the search engines: gemini.google.com and copilot.microsoft.com are assistants, not searches.
        if (Blog.Core.Services.AiReferrers.NameFor(host) is not null)
            return (host, Blog.Core.Services.AiReferrers.Medium);

        foreach (var se in SearchEngines)
            if (host.Contains(se))
                return (se, "organic");

        foreach (var sn in SocialNetworks)
            if (host.Contains(sn))
                return (host, "social");

        return (host, "referral");
    }

    public static string GetDisplayLabel(string source, string medium) => medium switch
    {
        "organic" => "Organic Search",
        "ai"      => $"AI assistant ({Blog.Core.Services.AiReferrers.NameFor(source) ?? source})",
        "social"  => "Social Media",
        "none"    => "Direct",
        "email"   => "Email",
        "cpc" or "ppc" or "paid" => "Paid Ads",
        // A visit stored as a plain referral before assistants were recognised still reads as one.
        "referral" when Blog.Core.Services.AiReferrers.NameFor(source) is { } assistant => $"AI assistant ({assistant})",
        "referral" => $"Referral ({source})",
        _ when medium == "unknown" && source == "direct" => "Direct",
        _ => string.IsNullOrEmpty(source) ? "Other" : source
    };
}
