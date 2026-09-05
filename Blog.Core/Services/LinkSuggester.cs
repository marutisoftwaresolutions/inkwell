using System.Text.RegularExpressions;

namespace Blog.Core.Services;

/// <summary>A published post the suggester can propose linking to or from.</summary>
public record LinkCandidate(
    Guid Id,
    string Slug,
    string Title,
    string Html,
    IReadOnlyCollection<string> CategorySlugs,
    IReadOnlyCollection<string> TagSlugs);

/// <summary>Why a suggestion was made, so the author can judge it rather than trust it.</summary>
public enum SuggestionKind
{
    /// <summary>This post already names the other one; the first mention should link to it.</summary>
    UnlinkedMention,
    /// <summary>They share topics, so a contextual link is likely to help the reader.</summary>
    SharedTopic
}

/// <summary>One proposed link, in a stated direction.</summary>
public record LinkSuggestion(
    string Slug,
    string Title,
    SuggestionKind Kind,
    int Score,
    string Reason);

/// <summary>
/// What the current post needs, measured against the Internal Linking Rule.
/// </summary>
public record LinkStatus(int Outbound, int Inbound, int Required = 2)
{
    public bool NeedsOutbound => Outbound < Required;
    public bool NeedsInbound  => Inbound  < Required;
    public bool IsOrphan      => Inbound == 0;
}

/// <summary>
/// Proposes internal links while a post is being written, in both directions: pages this post
/// should link to, and published posts that should link back to it.
///
/// The site reached 59 posts with 49 editorial links between them — 44 linking to nothing and 31
/// with no inbound link — because nothing surfaced the opportunity at the moment of writing.
/// Correcting that by hand took a full pass over the corpus; this makes it a publish-time habit.
///
/// Suggestions are ranked but never applied. An unlinked mention outranks a shared topic because it
/// is a link the prose already implies, and the reader is already expecting it.
/// </summary>
public static class LinkSuggester
{
    private const int MentionScore = 100;
    private const int TagScore = 3;
    private const int CategoryScore = 2;

    /// <summary>A token in more titles than this is describing the topic, not naming an article.</summary>
    private const int MaxTitlesForMentionKey = 2;

    /// <summary>Words too common in this corpus to make two posts genuinely related.</summary>
    private static readonly HashSet<string> _stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","and","for","with","your","from","best","top","how","why","what","which","guide",
        "complete","compared","comparison","review","reviews","software","platform","platforms",
        "system","systems","tools","tool","practice","practices","2024","2025","2026","2027"
    };

    private static readonly Regex _word = new(@"[a-z0-9][a-z0-9\-]{2,}", RegexOptions.Compiled);

    /// <summary>Posts this one should link to, best first.</summary>
    public static IReadOnlyList<LinkSuggestion> SuggestOutbound(
        LinkCandidate current, IEnumerable<LinkCandidate> candidates, int max = 6)
    {
        var pool = candidates as IReadOnlyList<LinkCandidate> ?? candidates.ToList();
        var keys = MentionKeys(pool.Append(current));
        var already = LinkedSlugs(current.Html);
        var results = new List<LinkSuggestion>();

        foreach (var other in pool)
        {
            if (other.Slug.Equals(current.Slug, StringComparison.OrdinalIgnoreCase)) continue;
            if (already.Contains(other.Slug)) continue;

            // A name this post already uses in its prose — the link the reader expects.
            if (MentionsUnlinked(current.Html, other, keys))
            {
                results.Add(new(other.Slug, other.Title, SuggestionKind.UnlinkedMention, MentionScore,
                    "This post already names it — link the first mention."));
                continue;
            }

            var (score, reason) = TopicOverlap(current, other);
            if (score > 0)
                results.Add(new(other.Slug, other.Title, SuggestionKind.SharedTopic, score, reason));
        }

        return Rank(results, max);
    }

    /// <summary>Published posts that should link back to this one, best first.</summary>
    public static IReadOnlyList<LinkSuggestion> SuggestInbound(
        LinkCandidate current, IEnumerable<LinkCandidate> candidates, int max = 6)
    {
        var pool = candidates as IReadOnlyList<LinkCandidate> ?? candidates.ToList();
        var keys = MentionKeys(pool.Append(current));
        var results = new List<LinkSuggestion>();

        foreach (var other in pool)
        {
            if (other.Slug.Equals(current.Slug, StringComparison.OrdinalIgnoreCase)) continue;
            if (LinkedSlugs(other.Html).Contains(current.Slug)) continue;   // already links here

            if (MentionsUnlinked(other.Html, current, keys))
            {
                results.Add(new(other.Slug, other.Title, SuggestionKind.UnlinkedMention, MentionScore,
                    "It already names this post — link the first mention from there."));
                continue;
            }

            var (score, reason) = TopicOverlap(current, other);
            if (score > 0)
                results.Add(new(other.Slug, other.Title, SuggestionKind.SharedTopic, score, reason));
        }

        return Rank(results, max);
    }

    /// <summary>How this post stands against the Internal Linking Rule's two-in, two-out minimum.</summary>
    public static LinkStatus Status(LinkCandidate current, IEnumerable<LinkCandidate> candidates)
    {
        var known = new HashSet<string>(candidates.Select(c => c.Slug).Append(current.Slug),
            StringComparer.OrdinalIgnoreCase);

        var outbound = LinkedSlugs(current.Html)
            .Where(s => known.Contains(s) && !s.Equals(current.Slug, StringComparison.OrdinalIgnoreCase))
            .Count();

        var inbound = candidates.Count(c =>
            !c.Slug.Equals(current.Slug, StringComparison.OrdinalIgnoreCase) &&
            LinkedSlugs(c.Html).Contains(current.Slug));

        return new LinkStatus(outbound, inbound);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<LinkSuggestion> Rank(List<LinkSuggestion> results, int max) =>
        results.OrderByDescending(r => r.Score)
               .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
               .Take(max)
               .ToList();

    /// <summary>Internal slugs this HTML already links to.</summary>
    private static HashSet<string> LinkedSlugs(string? html)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(html)) return set;

        foreach (Match m in Regex.Matches(html, @"href=""/([a-z0-9\-]+)/?[""#?]", RegexOptions.IgnoreCase))
            set.Add(m.Groups[1].Value);

        return set;
    }

    /// <summary>
    /// Tokens rare enough across the corpus to identify one specific post. A word appearing in more
    /// than a couple of titles is describing the subject matter, not naming an article: "scheduling"
    /// occurs throughout an optometry blog, while "RevolutionEHR" occurs once. Without this the
    /// mention rule fires on generic vocabulary and every suggestion becomes noise.
    /// </summary>
    private static Dictionary<string, int> MentionKeys(IEnumerable<LinkCandidate> all)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in all)
            foreach (var token in DistinctiveTokens(c.Title).Distinct(StringComparer.OrdinalIgnoreCase))
                counts[token] = counts.TryGetValue(token, out var n) ? n + 1 : 1;
        return counts;
    }

    /// <summary>
    /// True when the body names the other post distinctively and has not linked it. Deliberately
    /// conservative: the token must be five characters or more, survive the stop list, and be rare
    /// across the corpus, so a shared generic word never reads as a mention.
    /// </summary>
    private static bool MentionsUnlinked(string? html, LinkCandidate other, Dictionary<string, int> keys)
    {
        if (string.IsNullOrEmpty(html)) return false;

        var key = DistinctiveTokens(other.Title)
            .FirstOrDefault(t => t.Length >= 5
                              && keys.TryGetValue(t, out var n) && n <= MaxTitlesForMentionKey
                              && HasInternalCapital(other.Title, t));
        if (key is null) return false;

        foreach (Match m in Regex.Matches(html, Regex.Escape(key), RegexOptions.IgnoreCase))
        {
            var before = html[..m.Index];
            var lastOpen = before.LastIndexOf('<');
            var lastClose = before.LastIndexOf('>');
            if (lastOpen > lastClose) continue;                       // inside a tag or attribute
            if (Regex.IsMatch(before, @"<a\s[^>]*$", RegexOptions.IgnoreCase)) continue;

            // Inside an existing anchor's text? Then it is already linked somewhere.
            var openA = before.LastIndexOf("<a ", StringComparison.OrdinalIgnoreCase);
            var closeA = before.LastIndexOf("</a>", StringComparison.OrdinalIgnoreCase);
            if (openA > closeA) continue;

            return true;
        }

        return false;
    }

    private static (int Score, string Reason) TopicOverlap(LinkCandidate a, LinkCandidate b)
    {
        var tags = a.TagSlugs.Intersect(b.TagSlugs, StringComparer.OrdinalIgnoreCase).ToList();
        var cats = a.CategorySlugs.Intersect(b.CategorySlugs, StringComparer.OrdinalIgnoreCase).ToList();

        var score = tags.Count * TagScore + cats.Count * CategoryScore;
        if (score == 0) return (0, "");

        var parts = new List<string>();
        if (tags.Count > 0) parts.Add($"{tags.Count} shared topic{(tags.Count > 1 ? "s" : "")} ({string.Join(", ", tags.Take(3))})");
        if (cats.Count > 0) parts.Add($"{cats.Count} shared categor{(cats.Count > 1 ? "ies" : "y")}");

        return (score, string.Join(" and ", parts) + ".");
    }

    /// <summary>
    /// True when the token is written with a capital inside it in the original title - the shape of a
    /// product name (RevolutionEHR, iMedicWare, MaximEyes, OptoSoft) rather than ordinary vocabulary.
    /// Ordinary words are Title Cased in a heading too, so only an internal capital separates a brand
    /// from a common noun without a dictionary. Brands without one (Compulink, Eyefinity) fall back to
    /// topic suggestions, which is the safe direction to be wrong in.
    /// </summary>
    private static bool HasInternalCapital(string title, string token)
    {
        var i = title.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return false;

        var actual = title.Substring(i, token.Length);
        return actual.Skip(1).Any(char.IsUpper);
    }

    private static IEnumerable<string> DistinctiveTokens(string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? []
            : _word.Matches(title.ToLowerInvariant())
                   .Select(m => m.Value)
                   .Where(t => !_stop.Contains(t))
                   .OrderByDescending(t => t.Length);
}
