using System.Text.RegularExpressions;

namespace Blog.Core.Domain;

/// <summary>
/// One published post seen through a maintenance lens: is its review overdue, is it missing the
/// blocks that make it quotable, is its SERP snippet the right length, is its title still claiming
/// a year that has passed. Backs Admin → Content Health.
/// </summary>
public class ContentHealthItem
{
    public Guid      Id             { get; set; }
    public string    Title          { get; set; } = "";
    public string    Slug           { get; set; } = "";
    public DateTime? PublishedAt    { get; set; }
    public DateTime  UpdatedAt      { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime? NextReviewAt   { get; set; }
    public bool      HasKeyFacts    { get; set; }
    public bool      HasFaq         { get; set; }
    public int       MetaLength     { get; set; }

    // ── AEO readiness inputs ──────────────────────────────────────────────────
    // Counted in SQL rather than by loading article bodies; see ContentHealthRepository.
    public string?   AnswerCapsule   { get; set; }
    public string?   KeyFactsJson    { get; set; }
    public string?   FaqJson         { get; set; }
    public bool      HasFeatureImage { get; set; }
    public int       H2Count         { get; set; }
    public int       InternalLinks   { get; set; }
    public int       HtmlLength      { get; set; }

    /// <summary>Longest meta description Google reliably shows before truncating.</summary>
    public const int MetaDescriptionLimit = 155;

    public bool NeverVerified  => LastVerifiedAt is null;
    public bool ReviewOverdue  => NextReviewAt is not null && NextReviewAt < DateTime.UtcNow;
    public bool ReviewDueSoon  => NextReviewAt is not null
                                  && NextReviewAt >= DateTime.UtcNow
                                  && NextReviewAt < DateTime.UtcNow.AddDays(30);
    public bool NoReviewDate   => NextReviewAt is null;
    public bool MetaMissing    => MetaLength == 0;
    public bool MetaTooLong    => MetaLength > MetaDescriptionLimit;

    /// <summary>
    /// A title promising a year that has already passed ("… 2026" once it is 2027). The content, not
    /// just the number, needs revisiting — a stamp bumped without a real update is a false signal.
    /// </summary>
    public bool StaleYearStamp
    {
        get
        {
            var match = _yearInTitle.Match(Title);
            return match.Success
                && int.TryParse(match.Groups[1].Value, out var year)
                && year < DateTime.UtcNow.Year;
        }
    }

    /// <summary>
    /// AEO readiness for this post, 0–100. Computed on first access so a caller that only wants the
    /// maintenance view does not pay for it.
    /// </summary>
    public int AeoScore => _aeo ??= Services.AeoReadiness.Score(this).Score;
    private int? _aeo;

    /// <summary>Below this, a post is unlikely to be the source an answer engine quotes.</summary>
    public const int WeakAeoScore = 50;

    public bool AeoWeak => AeoScore < WeakAeoScore;

    /// <summary>
    /// Search Console performance for this post over the last 28 days, attached by the controller
    /// when a property is connected. Null means "not connected" or "never appeared in search" —
    /// the view distinguishes the two through <see cref="ContentHealthSummary.SearchConnected"/>.
    /// </summary>
    public PageSearchSummary? Search { get; set; }

    public bool StrikingDistance  => Search?.StrikingDistance  == true;
    public bool LosingImpressions => Search?.LosingImpressions == true;
    public bool LowCtrForPosition => Search?.LowCtrForPosition == true;

    /// <summary>True when anything needs attention — drives the default filter.</summary>
    public bool NeedsAttention =>
        NeverVerified || ReviewOverdue || NoReviewDate || !HasKeyFacts || MetaMissing || MetaTooLong || StaleYearStamp;

    // Years 2000-2099 appearing as a standalone token, so "Top 5" or a version number never matches.
    private static readonly Regex _yearInTitle = new(@"\b(20\d{2})\b", RegexOptions.Compiled);
}

/// <summary>Counts for the Content Health tiles.</summary>
public class ContentHealthSummary
{
    public int Total            { get; set; }
    public int ReviewOverdue    { get; set; }
    public int ReviewDueSoon    { get; set; }
    public int NeverVerified    { get; set; }
    public int MissingKeyFacts  { get; set; }
    public int MissingFaq       { get; set; }
    public int MetaIssues       { get; set; }
    public int StaleYearStamps  { get; set; }
    public int WeakAeo          { get; set; }
    public int AverageAeoScore  { get; set; }
    public int Healthy          { get; set; }

    /// <summary>A Search Console property is connected and has delivered at least one day of data.</summary>
    public bool SearchConnected     { get; set; }
    /// <summary>Newest date with search data, for the "as of" note. Search Console lags ~3 days.</summary>
    public DateTime? SearchAsOf     { get; set; }
    public int StrikingDistance     { get; set; }
    public int LosingImpressions    { get; set; }
    public int LowCtrForPosition    { get; set; }

    public static ContentHealthSummary From(IReadOnlyList<ContentHealthItem> items) => new()
    {
        StrikingDistance  = items.Count(i => i.StrikingDistance),
        LosingImpressions = items.Count(i => i.LosingImpressions),
        LowCtrForPosition = items.Count(i => i.LowCtrForPosition),
        Total           = items.Count,
        ReviewOverdue   = items.Count(i => i.ReviewOverdue),
        ReviewDueSoon   = items.Count(i => i.ReviewDueSoon),
        NeverVerified   = items.Count(i => i.NeverVerified),
        MissingKeyFacts = items.Count(i => !i.HasKeyFacts),
        MissingFaq      = items.Count(i => !i.HasFaq),
        MetaIssues      = items.Count(i => i.MetaMissing || i.MetaTooLong),
        StaleYearStamps = items.Count(i => i.StaleYearStamp),
        WeakAeo         = items.Count(i => i.AeoWeak),
        AverageAeoScore = items.Count == 0 ? 0 : (int)Math.Round(items.Average(i => i.AeoScore)),
        Healthy         = items.Count(i => !i.NeedsAttention)
    };
}
