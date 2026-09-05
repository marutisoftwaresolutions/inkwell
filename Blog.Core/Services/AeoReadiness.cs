using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// Scores how ready a post is to be *quoted* by an answer engine, as opposed to merely ranked.
///
/// The pieces already exist — answer capsules, Key Facts, FAQ, How-To, structured data, internal
/// links, freshness dates — but an author had no way to see which of them this post is missing
/// before publishing. The structured-data linter says what is *broken*; this says what is *absent*.
/// It is advisory by design and never blocks a publish: a short news note that scores 40 may be
/// exactly right, and a rule that forced every post into the same shape would produce filler.
///
/// Deliberately not scored: anything we cannot observe. There is no "is it accurate", no
/// "is it authoritative", and no invented ranking prediction.
/// </summary>
public static class AeoReadiness
{
    /// <summary>What the scorer needs. Kept separate from <see cref="Post"/> so the dashboard can
    /// compute these in SQL for every post without transferring 60 article bodies.</summary>
    public class Inputs
    {
        public int  AnswerCapsuleWords { get; set; }
        public int  KeyFactCount       { get; set; }
        public int  FaqCount           { get; set; }
        public int  MetaLength         { get; set; }
        public int  H2Count            { get; set; }
        public int  InternalLinks      { get; set; }
        /// <summary>Length of the article HTML. Scored instead of a word count because the Content
        /// Health dashboard computes it in SQL without transferring article bodies, and a score that
        /// differed between the dashboard and the editor would be worse than no score.</summary>
        public int  HtmlLength         { get; set; }
        public bool HasFeatureImage    { get; set; }
        public DateTime? LastVerifiedAt { get; set; }
        public DateTime? NextReviewAt   { get; set; }
    }

    public record Signal(string Name, int Earned, int Possible, string Detail, string Advice)
    {
        public bool IsComplete => Earned >= Possible;
        public bool IsMissing  => Earned == 0;
    }

    public class Result
    {
        public int Score { get; init; }
        public IReadOnlyList<Signal> Signals { get; init; } = [];

        public string Grade => Score switch
        {
            >= 85 => "Excellent",
            >= 70 => "Good",
            >= 50 => "Fair",
            _     => "Needs work"
        };

        /// <summary>The highest-value thing still missing, or null when nothing is.</summary>
        public Signal? NextBestAction => Signals
            .Where(s => !s.IsComplete)
            .OrderByDescending(s => s.Possible - s.Earned)
            .FirstOrDefault();
    }

    public const int MetaDescriptionLimit = ContentHealthItem.MetaDescriptionLimit;

    public static Result Score(Inputs i)
    {
        var signals = new List<Signal>
        {
            ScoreCapsule(i),
            ScoreKeyFacts(i),
            ScoreFaq(i),
            ScoreMeta(i),
            ScoreHeadings(i),
            ScoreInternalLinks(i),
            ScoreFreshness(i),
            ScoreDepth(i),
            ScoreImage(i)
        };

        return new Result { Score = signals.Sum(s => s.Earned), Signals = signals };
    }

    // ── Individual signals ────────────────────────────────────────────────────

    private static Signal ScoreCapsule(Inputs i)
    {
        // The single most extractable unit on the page, hence the largest weight.
        const int max = 20;
        var w = i.AnswerCapsuleWords;

        if (w == 0)
            return new("Answer capsule", 0, max, "None",
                "Add a 40–60 word direct answer at the top. This is the passage an answer engine lifts.");

        if (w is >= 30 and <= 70)
            return new("Answer capsule", max, max, $"{w} words", "");

        return new("Answer capsule", max / 2, max, $"{w} words",
            w < 30 ? "Too short to stand alone as an answer — aim for 40–60 words."
                   : "Too long to be quoted whole — trim to 40–60 words.");
    }

    private static Signal ScoreKeyFacts(Inputs i)
    {
        const int max = 15;
        return i.KeyFactCount switch
        {
            0     => new("Key Facts", 0, max, "None",
                         "Add an \"At a glance\" block. Facts in a labelled list are far easier to extract than prose."),
            >= 3  => new("Key Facts", max, max, $"{i.KeyFactCount} facts", ""),
            _     => new("Key Facts", max / 2, max, $"{i.KeyFactCount} fact(s)",
                         "Three or more facts gives an engine something to compare against other sources.")
        };
    }

    private static Signal ScoreFaq(Inputs i)
    {
        const int max = 15;
        return i.FaqCount switch
        {
            0    => new("FAQ", 0, max, "None",
                        "Add the questions readers actually ask. FAQ entries map directly to FAQPage schema."),
            >= 3 => new("FAQ", max, max, $"{i.FaqCount} questions", ""),
            _    => new("FAQ", max / 2, max, $"{i.FaqCount} question(s)",
                        "Three or more covers the follow-up questions an answer engine is asked next.")
        };
    }

    private static Signal ScoreMeta(Inputs i)
    {
        const int max = 10;
        if (i.MetaLength == 0)
            return new("Meta description", 0, max, "Missing",
                "Write one. Without it the engine picks an arbitrary sentence from the page.");

        if (i.MetaLength > MetaDescriptionLimit)
            return new("Meta description", max / 2, max, $"{i.MetaLength} chars",
                $"Over {MetaDescriptionLimit} characters, so it will be truncated in search results.");

        return new("Meta description", max, max, $"{i.MetaLength} chars", "");
    }

    private static Signal ScoreHeadings(Inputs i)
    {
        // Passage-level retrieval splits an article on its headings; an unbroken wall of text gives
        // it nothing to split on.
        const int max = 10;
        return i.H2Count switch
        {
            0    => new("Section headings", 0, max, "None",
                        "Break the article into H2 sections. Answer engines retrieve passages, not pages."),
            >= 3 => new("Section headings", max, max, $"{i.H2Count} sections", ""),
            _    => new("Section headings", max / 2, max, $"{i.H2Count} section(s)",
                        "A few more H2s gives each idea its own retrievable passage.")
        };
    }

    private static Signal ScoreInternalLinks(Inputs i)
    {
        const int max = 10;
        return i.InternalLinks switch
        {
            0    => new("Internal links", 0, max, "None",
                        "Link at least two related posts in the prose. An unlinked post is discounted by search engines and unreachable by topic."),
            >= 2 => new("Internal links", max, max, $"{i.InternalLinks} links", ""),
            _    => new("Internal links", max / 2, max, "1 link",
                        "Two outbound contextual links is the working minimum.")
        };
    }

    private static Signal ScoreFreshness(Inputs i)
    {
        const int max = 10;
        var verified = i.LastVerifiedAt is not null && i.LastVerifiedAt > DateTime.UtcNow.AddMonths(-12);
        var scheduled = i.NextReviewAt is not null;

        if (verified && scheduled)
            return new("Freshness", max, max, $"Verified {i.LastVerifiedAt:d MMM yyyy}", "");

        if (verified || scheduled)
            return new("Freshness", max / 2, max,
                verified ? "No review scheduled" : "Never verified",
                verified ? "Set a review date so this does not quietly go stale."
                         : "Record when the facts were last checked.");

        return new("Freshness", 0, max, "Never verified, no review date",
            "Set both dates. A post with no verification history reads as unmaintained to a citation engine.");
    }

    private static Signal ScoreDepth(Inputs i)
    {
        const int max = 5;
        return i.HtmlLength switch
        {
            >= 6000 => new("Depth", max, max, $"{i.HtmlLength:N0} chars", ""),
            >= 3000 => new("Depth", max / 2, max, $"{i.HtmlLength:N0} chars",
                           "Short pieces are rarely the source an engine cites for a topic."),
            _       => new("Depth", 0, max, $"{i.HtmlLength:N0} chars",
                           "Too thin to be treated as a source on this topic.")
        };
    }

    private static Signal ScoreImage(Inputs i) =>
        i.HasFeatureImage
            ? new("Feature image", 5, 5, "Set", "")
            : new("Feature image", 0, 5, "None",
                  "Add one. Rich results and social cards both need an image to render.");

    // ── Building inputs from a loaded post ────────────────────────────────────

    // Counted as the exact literals the Content Health SQL counts, so the editor and the dashboard
    // can never disagree about the same post. The closing tag is used because it carries no
    // attributes and so has a fixed length, which is what makes the SQL count exact.
    private const string H2Token   = "</h2>";
    private const string LinkToken = "href=\"/";

    public static Inputs From(Post post)
    {
        var html = post.Html ?? "";

        return new Inputs
        {
            AnswerCapsuleWords = post.AnswerCapsuleWordCount,
            KeyFactCount       = post.KeyFacts.Count,
            // Post.Faqs throws on malformed JSON where KeyFacts degrades to empty. A draft with a
            // broken block is exactly when an author opens the editor, so scoring must survive it —
            // the linter is what reports the breakage.
            FaqCount           = SafeCount(() => post.Faqs.Count),
            MetaLength         = (post.MetaDescription ?? "").Trim().Length,
            H2Count            = CountOccurrences(html, H2Token),
            InternalLinks      = CountOccurrences(html, LinkToken),
            HtmlLength         = html.Length,
            HasFeatureImage    = !string.IsNullOrWhiteSpace(post.FeatureImage),
            LastVerifiedAt     = post.LastVerifiedAt,
            NextReviewAt       = post.NextReviewAt
        };
    }

    public static Result Score(Post post) => Score(From(post));

    /// <summary>
    /// Scores a dashboard row. The counted signals (headings, links, length) arrive pre-computed
    /// from SQL; the JSON blocks are parsed through the same <see cref="Post"/> properties the
    /// editor uses, so a post cannot score differently on the two screens.
    /// </summary>
    public static Inputs From(ContentHealthItem item)
    {
        var shim = new Post
        {
            AnswerCapsule = item.AnswerCapsule,
            KeyFactsJson  = item.KeyFactsJson,
            FaqJson       = item.FaqJson
        };

        return new Inputs
        {
            AnswerCapsuleWords = shim.AnswerCapsuleWordCount,
            KeyFactCount       = shim.KeyFacts.Count,
            FaqCount           = SafeCount(() => shim.Faqs.Count),
            MetaLength         = item.MetaLength,
            H2Count            = item.H2Count,
            InternalLinks      = item.InternalLinks,
            HtmlLength         = item.HtmlLength,
            HasFeatureImage    = item.HasFeatureImage,
            LastVerifiedAt     = item.LastVerifiedAt,
            NextReviewAt       = item.NextReviewAt
        };
    }

    public static Result Score(ContentHealthItem item) => Score(From(item));

    private static int SafeCount(Func<int> count)
    {
        try { return count(); }
        catch { return 0; }
    }

    public static int CountOccurrences(string haystack, string token)
    {
        if (string.IsNullOrEmpty(haystack)) return 0;

        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(token, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            at += token.Length;
        }
        return count;
    }
}
