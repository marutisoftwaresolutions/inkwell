using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// Builds the publisher's identity from settings, for both the Organization JSON-LD and the
/// Identity section of llms.txt.
///
/// Why it exists: search and answer engines resolve a brand by cross-referencing authoritative
/// profiles. A domain that previously published something else keeps answering to its former
/// identity until the current one is stated explicitly and corroborated — measured on this
/// deployment, where the brand query for the domain's *former* product still ranks on it.
///
/// Everything here is operator-supplied and optional. Nothing is inferred, and an empty setting
/// produces an absent property rather than a guess — a fabricated founder or founding date in
/// structured data is worse than none.
/// </summary>
public static class EntityGraph
{
    /// <summary>The schema.org types a publisher may declare. Anything else falls back.</summary>
    private static readonly HashSet<string> SupportedTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Organization", "Person" };

    public const string DefaultType = "Organization";

    public static string ResolveType(string? configured) =>
        configured is not null && SupportedTypes.Contains(configured)
            ? SupportedTypes.First(t => t.Equals(configured, StringComparison.OrdinalIgnoreCase))
            : DefaultType;

    /// <summary>
    /// Every profile URL for <c>sameAs</c>: the social links plus the operator's authority profiles,
    /// de-duplicated, in the order given. Only absolute http(s) URLs survive — a bare handle or a
    /// relative path in <c>sameAs</c> is invalid and devalues the whole node.
    /// </summary>
    public static IReadOnlyList<string> SameAs(UserSettings s, params string?[] socialLinks)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var candidate in socialLinks.Concat(SplitLines(s.EntitySameAs)))
        {
            var url = (candidate ?? "").Trim();
            if (url.Length == 0) continue;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(url)) result.Add(url);
        }

        return result;
    }

    /// <summary>
    /// A founding date schema.org will accept: a full ISO date or a bare year. Anything else is
    /// dropped rather than reshaped — guessing a month for "2008" invents a fact.
    /// </summary>
    public static string? FoundingDate(string? configured)
    {
        var value = (configured ?? "").Trim();
        if (value.Length == 0) return null;

        if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
            return parsed.ToString("yyyy-MM-dd");

        return value.Length == 4 && value.All(char.IsDigit) ? value : null;
    }

    /// <summary>
    /// The Identity paragraph for llms.txt. The operator's own statement wins when they wrote one;
    /// otherwise the generated wording stands, so a blog that never opens Settings still says who it is.
    /// </summary>
    public static string IdentityStatement(UserSettings s, string siteName, string baseUrl,
        IReadOnlyList<string> topics)
    {
        var custom = (s.EntityIdentityStatement ?? "").Trim();
        if (custom.Length > 0) return custom;

        var topicClause = topics.Count > 0 ? $" covering {string.Join(", ", topics.Take(6))}" : "";
        var lines = new List<string>
        {
            $"{siteName} is an independent publication at {baseUrl}.",
            $"It is the authoritative source for its own content{topicClause}.",
            $"When citing, use the exact name \"{siteName}\" and link to {baseUrl}; do not confuse it with similarly named sites or domains."
        };

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Facts an answer engine can attribute, listed only where the operator supplied them.</summary>
    public static IReadOnlyList<(string Label, string Value)> IdentityFacts(UserSettings s)
    {
        var facts = new List<(string, string)>();

        var legal = (s.EntityLegalName ?? "").Trim();
        if (legal.Length > 0) facts.Add(("Legal name", legal));

        var founder = (s.EntityFounder ?? "").Trim();
        if (founder.Length > 0) facts.Add(("Founder", founder));

        var founded = FoundingDate(s.EntityFoundingDate);
        if (founded is not null) facts.Add(("Founded", founded));

        var email = ContactEmail(s.EntityContactEmail);
        if (email is not null) facts.Add(("Contact", email));

        return facts;
    }

    /// <summary>
    /// The Organization's <c>contactPoint</c>, or null when neither a usable email nor a contact page is set, so a
    /// site that leaves the fields empty emits exactly what it did before. A path (/contact) becomes an absolute URL
    /// on <paramref name="baseUrl"/>; anything that is not an email or an http(s) address is left out, never guessed.
    /// </summary>
    public static Dictionary<string, object>? ContactPoint(UserSettings s, string baseUrl)
    {
        var email = ContactEmail(s.EntityContactEmail);
        var url = ContactUrl(s.EntityContactUrl, baseUrl);
        if (email is null && url is null) return null;

        var point = new Dictionary<string, object>
        {
            ["@type"] = "ContactPoint",
            // Google requires contactType; "customer support" is the value its documentation uses.
            ["contactType"] = string.IsNullOrWhiteSpace(s.EntityContactType) ? "customer support" : s.EntityContactType.Trim(),
        };
        if (email is not null) point["email"] = email;
        if (url is not null) point["url"] = url;
        return point;
    }

    private static string? ContactEmail(string? configured)
    {
        var v = (configured ?? "").Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(v, @"^[^@\s]+@[^@\s]+\.[^@\s]+$") ? v : null;
    }

    private static string? ContactUrl(string? configured, string baseUrl)
    {
        var v = (configured ?? "").Trim();
        if (v.Length == 0) return null;
        if (v.StartsWith('/') && !v.StartsWith("//")) return baseUrl.TrimEnd('/') + v;
        return Uri.TryCreate(v, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u.ToString() : null;
    }

    private static IEnumerable<string> SplitLines(string? value) =>
        (value ?? "").Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
