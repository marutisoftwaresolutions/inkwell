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
            return (utmSource.ToString().ToLowerInvariant(), medium.ToLowerInvariant());
        }

        if (string.IsNullOrWhiteSpace(referrer))
            return ("direct", "none");

        if (!Uri.TryCreate(referrer, UriKind.Absolute, out var uri))
            return ("direct", "none");

        var host = uri.Host.ToLowerInvariant().TrimStart('w', '.');

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
        "social"  => "Social Media",
        "none"    => "Direct",
        "email"   => "Email",
        "cpc" or "ppc" or "paid" => "Paid Ads",
        "referral" => $"Referral ({source})",
        _ when medium == "unknown" && source == "direct" => "Direct",
        _ => string.IsNullOrEmpty(source) ? "Other" : source
    };
}
