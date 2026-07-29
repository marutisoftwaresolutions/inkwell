namespace Blog.Core.Domain;

/// <summary>
/// Structured data behind a "Verdict" roundup post — a scored "Top N Best X" comparison
/// listicle. Stored as a JSON blob in Posts.RoundupJson (see DBScripts/2026-07-14_add-roundupjson-to-posts.sql).
/// Drives both the rendered UI and the ItemList / Review / AggregateRating JSON-LD.
/// </summary>
public class RoundupData
{
    /// <summary>schema.org type for each entry — e.g. SoftwareApplication, Product, Service.</summary>
    public string ListType { get; set; } = "SoftwareApplication";

    public RoundupWeights Weights { get; set; } = new();
    public List<RoundupStep> Methodology { get; set; } = new();
    public List<RoundupEntry> Entries { get; set; } = new();

    /// <summary>Entries in rank order. Unranked entries sort last.</summary>
    public IEnumerable<RoundupEntry> Ranked =>
        Entries.OrderBy(e => e.Rank <= 0 ? int.MaxValue : e.Rank);

    public bool HasEntries => Entries.Count > 0;

    /// <summary>Mean score across scored entries — powers AggregateRating.</summary>
    public double AverageScore
    {
        get
        {
            var scored = Entries.Where(e => e.Score > 0).ToList();
            return scored.Count == 0 ? 0 : Math.Round(scored.Average(e => e.Score), 1);
        }
    }
}

/// <summary>Scoring weights as percentages. The reference format is 40 / 30 / 30.</summary>
public class RoundupWeights
{
    public int Features { get; set; } = 40;
    public int Ease { get; set; } = 30;
    public int Value { get; set; } = 30;
}

/// <summary>One numbered step in the "How we ranked these tools" section.</summary>
public class RoundupStep
{
    public string Step { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

/// <summary>Per-dimension sub-scores, each out of 10.</summary>
public class RoundupScores
{
    public double Features { get; set; }
    public double Ease { get; set; }
    public double Value { get; set; }
}

public class RoundupEntry
{
    public int Rank { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional ribbon — "Editor's pick", "Runner-up", "Also great".</summary>
    public string? Badge { get; set; }

    /// <summary>Category chip shown next to the name — e.g. "optical-POS".</summary>
    public string? Category { get; set; }

    /// <summary>Overall score out of 10.</summary>
    public double Score { get; set; }

    public RoundupScores? Scores { get; set; }

    public string? BestFor { get; set; }
    public string? Standout { get; set; }

    /// <summary>Narrative review body. Plain text or trusted HTML from the admin editor.</summary>
    public string? Body { get; set; }

    public List<string> Pros { get; set; } = new();
    public List<string> Cons { get; set; } = new();

    /// <summary>Internal review-post path for the topic cluster, e.g. "/eyefinity-ehr-review".</summary>
    public string? CtaUrl { get; set; }

    /// <summary>
    /// The product/company's real website, e.g. "https://www.revolutionehr.com". Rendered as a
    /// "Visit website" outbound reference and used as the SoftwareApplication url in JSON-LD.
    /// Mandatory for every entry per the Content External-Link Rule in CLAUDE.md.
    /// </summary>
    public string? Website { get; set; }

    public string? LogoUrl { get; set; }

    /// <summary>Shown in the "Verified · domain" footer and the Sources list.</summary>
    public string? Domain { get; set; }

    public bool HasCta => !string.IsNullOrWhiteSpace(CtaUrl);

    public bool HasWebsite => !string.IsNullOrWhiteSpace(Website);

    /// <summary>
    /// True when this entry is the operator's own first-party product (OptoSoft). First-party
    /// entries are shown with an on-page disclosure and must NOT emit a self-authored
    /// Review/AggregateRating in JSON-LD (self-serving ratings violate Google's policy and the
    /// SEO/AEO Rule). Derived from the website domain — no stored flag.
    /// </summary>
    public bool IsFirstParty =>
        (Website?.Contains("opto-soft.com", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (Website?.Contains("opticalsoftware.org", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// True when CtaUrl points off-site. External CTAs render as sponsored/nofollow links
    /// that open in a new tab; internal ones (e.g. "/eyefinity-ehr-review") stay in-tab as
    /// normal dofollow links so link equity flows through the topic cluster.
    /// </summary>
    public bool CtaIsExternal =>
        CtaUrl is not null &&
        (CtaUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         CtaUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>Stable anchor id for TOC jump links.</summary>
    public string Anchor => Slugify(Name);

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "entry";
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-') is { Length: > 0 } s ? s : "entry";
    }
}
