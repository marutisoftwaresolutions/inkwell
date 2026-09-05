using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// Turns a raw 404 log into a worklist an operator can act on.
///
/// The hard part is not finding 404s — a public site produces thousands, almost all of them
/// vulnerability scanners. It is finding the handful a *reader* hit. Redirect tools that list every
/// 404 make the operator do that sorting by hand, and it never gets done.
///
/// So this asks for evidence that a 404 is reader-facing rather than trying to recognise attackers:
/// either another page links to it (a referrer), or it is close enough to a real published slug to
/// be a rename, a typo, or a stale external link. Everything else ranks last. Deliberately no
/// attack-signature list lives here — recognising probes is the firewall's job (`ThreatScorer`), and
/// a second copy of those patterns would drift out of step with the one that decides blocking.
/// </summary>
public static class NotFoundTriage
{
    /// <summary>Below this, two slugs are different articles rather than a typo of one.</summary>
    public const double MinSimilarity = 0.62;

    /// <summary>Anything with a file extension is an asset or a probe, never a post slug.</summary>
    private static bool LooksLikeAPostUrl(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 2 || path[0] != '/') return false;

        var slug = path.Trim('/');

        // Post URLs are a single segment; /wp-content/plugins/... never is.
        if (slug.Contains('/')) return false;
        if (slug.Contains('.')) return false;
        if (slug.Length > 200) return false;

        return slug.Length > 0;
    }

    public static IReadOnlyList<NotFoundCandidate> Triage(
        IEnumerable<NotFoundGroup> groups, IEnumerable<string> publishedSlugs)
    {
        var slugs = publishedSlugs
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<NotFoundCandidate>();

        foreach (var g in groups)
        {
            var candidate = new NotFoundCandidate
            {
                Path       = g.Path,
                Hits       = g.Hits,
                LastSeenAt = g.LastSeenAt,
                Referer    = string.IsNullOrWhiteSpace(g.Referer) ? null : g.Referer
            };

            if (LooksLikeAPostUrl(g.Path))
            {
                var (slug, score) = BestMatch(g.Path.Trim('/'), slugs);
                if (score >= MinSimilarity)
                {
                    candidate.SuggestedSlug = slug;
                    candidate.Similarity    = score;
                    candidate.Kind          = NotFoundKind.NearMiss;
                }
            }

            // A referrer still matters when nothing matched: something out there links here.
            if (candidate.Kind == NotFoundKind.Unmatched && candidate.Referer is not null)
                candidate.Kind = NotFoundKind.Referred;

            results.Add(candidate);
        }

        return results
            .OrderByDescending(c => (int)c.Kind)
            .ThenByDescending(c => c.Similarity)
            .ThenByDescending(c => c.Hits)
            .ThenByDescending(c => c.LastSeenAt)
            .ToList();
    }

    /// <summary>The closest published slug and how close it is, on 0–1.</summary>
    public static (string? Slug, double Score) BestMatch(string slug, IEnumerable<string> publishedSlugs)
    {
        string? best = null;
        double bestScore = 0;

        foreach (var s in publishedSlugs)
        {
            var score = Similarity(slug, s);
            if (score > bestScore) { bestScore = score; best = s; }
        }

        return bestScore > 0 ? (best, bestScore) : (null, 0);
    }

    /// <summary>
    /// Character closeness combined with token overlap. Edit distance alone rates
    /// "best-optical-software-2025" against "best-optical-software-2026" very highly (it is one
    /// character), which is right; token overlap alone rates it highly too. Taking the higher of the
    /// two catches both a mistyped slug and a reworded one.
    /// </summary>
    public static double Similarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return 1;

        var x = a.ToLowerInvariant();
        var y = b.ToLowerInvariant();

        var distance = Levenshtein(x, y);
        var charScore = 1.0 - (double)distance / Math.Max(x.Length, y.Length);

        var ta = Tokens(x);
        var tb = Tokens(y);
        var tokenScore = 0.0;
        if (ta.Count > 0 && tb.Count > 0)
        {
            var shared = ta.Intersect(tb, StringComparer.Ordinal).Count();
            // Jaccard, so a short slug matching part of a long one does not score as a rename.
            tokenScore = (double)shared / ta.Union(tb, StringComparer.Ordinal).Count();
        }

        return Math.Max(charScore, tokenScore);
    }

    private static List<string> Tokens(string slug) =>
        slug.Split(['-', '_', '/'], StringSplitOptions.RemoveEmptyEntries).ToList();

    private static int Levenshtein(string a, string b)
    {
        // Two rows rather than a full matrix — slugs are short, but this runs over every published
        // slug for every logged 404 and the allocation adds up.
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    // ── Rule validation ───────────────────────────────────────────────────────

    /// <summary>
    /// Why a proposed rule cannot be saved, or null when it can. A redirect manager that lets an
    /// operator point a URL at itself, or at a path that redirects again, manufactures exactly the
    /// loops and chains the Link Audit screen exists to report.
    /// </summary>
    public static string? Validate(string from, string? to, int statusCode,
        IEnumerable<RedirectRule> existingRules)
    {
        from = (from ?? "").Trim();
        to = (to ?? "").Trim();

        if (from.Length == 0) return "Enter the path to redirect from.";
        if (!from.StartsWith('/')) return "The 'from' path must start with a slash, for example /old-slug.";
        if (from == "/") return "The home page cannot be redirected.";
        if (from.Contains("://")) return "The 'from' path must be a path on this site, not a full URL.";
        if (!RedirectStatus.IsSupported(statusCode)) return "Choose 301, 302, or 410.";

        if (statusCode == RedirectStatus.Gone)
            return null;   // 410 has no destination to validate

        if (to.Length == 0) return "Enter a destination, or choose 410 Gone to retire the URL.";
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return "A URL cannot redirect to itself.";
        if (!to.StartsWith('/') && !to.StartsWith("http://") && !to.StartsWith("https://"))
            return "The destination must be a path starting with / or a full http(s) URL.";

        // Only internal destinations can chain.
        if (to.StartsWith('/'))
        {
            var rules = existingRules
                .Where(r => !string.Equals(r.From, from, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(r => r.From, StringComparer.OrdinalIgnoreCase);

            var hop = to;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { from };

            for (var i = 0; i < 10 && rules.TryGetValue(hop, out var next); i++)
            {
                if (next.IsGone)
                    return $"The destination {hop} is retired with 410 Gone. Point this at a live page instead.";

                if (!seen.Add(hop))
                    return "That would create a redirect loop.";

                if (string.Equals(next.To, from, StringComparison.OrdinalIgnoreCase))
                    return "That would create a redirect loop.";

                hop = next.To;
                if (!hop.StartsWith('/')) break;
            }

            if (!string.Equals(hop, to, StringComparison.OrdinalIgnoreCase))
                return $"{to} already redirects to {hop}. Point this straight at {hop} to avoid a redirect chain.";
        }

        return null;
    }
}
