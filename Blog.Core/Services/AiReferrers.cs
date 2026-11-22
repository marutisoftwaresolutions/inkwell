namespace Blog.Core.Services;

/// <summary>
/// Which visits came from an AI assistant: a reader followed a link in an answer from ChatGPT,
/// Perplexity, Claude and the like. Recognised by the site the visit came from (the referrer's host,
/// or the <c>utm_source</c> some assistants add to the links they cite), against a short list of
/// hosts each operator uses for its assistant. It is a list, so it can only name what it knows: an
/// assistant that sends no referrer is counted as Direct like any other such visit, and a new
/// assistant is "Referral" until its host is added here. Pure.
/// </summary>
public static class AiReferrers
{
    /// <summary>The medium stored with such a visit, beside "organic", "social" and "referral".</summary>
    public const string Medium = "ai";

    /// <summary>Host (or host suffix) to the assistant's name. A sub-domain of a listed host matches too.</summary>
    private static readonly (string Host, string Name)[] Hosts =
    [
        ("chatgpt.com", "ChatGPT"),
        ("chat.openai.com", "ChatGPT"),
        ("perplexity.ai", "Perplexity"),
        ("claude.ai", "Claude"),
        ("gemini.google.com", "Gemini"),
        ("copilot.microsoft.com", "Copilot"),
        ("copilot.com", "Copilot"),
        ("chat.mistral.ai", "Le Chat"),
        ("chat.deepseek.com", "DeepSeek"),
        ("grok.com", "Grok"),
        ("meta.ai", "Meta AI"),
        ("you.com", "You.com"),
        ("phind.com", "Phind"),
        ("duck.ai", "Duck.ai"),
        ("kagi.com", "Kagi"),
    ];

    /// <summary>The assistants this list knows, once each, for the Desk to name.</summary>
    public static IReadOnlyList<string> KnownNames => Hosts.Select(h => h.Name).Distinct().ToList();

    /// <summary>
    /// The assistant behind a host or a stored source value ("chatgpt.com", "www.perplexity.ai",
    /// "https://claude.ai/chat/…"), or null when it is not one this list knows.
    /// </summary>
    public static string? NameFor(string? hostOrSource)
    {
        var value = (hostOrSource ?? "").Trim().ToLowerInvariant();
        if (value.Length == 0) return null;
        if (value.Contains("://") && Uri.TryCreate(value, UriKind.Absolute, out var uri)) value = uri.Host;
        var slash = value.IndexOf('/');
        if (slash >= 0) value = value[..slash];
        if (value.StartsWith("www.", StringComparison.Ordinal)) value = value[4..];

        foreach (var (host, name) in Hosts)
            if (value == host || value.EndsWith("." + host, StringComparison.Ordinal)) return name;
        return null;
    }

    /// <summary>
    /// Visits per assistant from the source rows of a report, most first. Rows are matched by their
    /// source, whatever medium they were stored with, so visits recorded before this list existed
    /// (as plain referrals) are counted too.
    /// </summary>
    public static List<(string Name, int Visits)> Summarise(IEnumerable<(string Source, string Medium, int Count)> sources) =>
        sources.Select(s => (Name: NameFor(s.Source), s.Count))
            .Where(s => s.Name is not null && s.Count > 0)
            .GroupBy(s => s.Name!)
            .Select(g => (Name: g.Key, Visits: g.Sum(s => s.Count)))
            .OrderByDescending(s => s.Visits).ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
}
