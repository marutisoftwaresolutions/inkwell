using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// Turns stored Search Console rows into the judgements the striking-distance panel makes. Pure:
/// give it the daily page rows for the last 56 days and a reference date, get back one summary per
/// page. Every threshold is a named constant so the rules can be read in one place and tested.
///
/// The three flags answer three different editorial questions:
///   • <b>striking distance</b> — "which pages would page one with one more push?" (position 8–20
///     with enough impressions to prove demand);
///   • <b>losing impressions</b> — "which pages is Google showing less than it used to?" (a hard
///     fall against the previous window, from a base big enough to mean something);
///   • <b>low CTR for position</b> — "which pages rank but nobody clicks?" (page one, real
///     impressions, click-through under the floor — a title/snippet problem, not a ranking one).
/// </summary>
public static class SearchPerformanceAnalyzer
{
    public const int WindowDays = 28;

    public const double StrikingMinPosition = 8;
    public const double StrikingMaxPosition = 20;
    public const int StrikingMinImpressions = 20;

    public const int LosingMinPrevImpressions = 50;
    /// <summary>Current impressions at or below this share of the previous window counts as losing.</summary>
    public const double LosingRatio = 0.70;

    public const double LowCtrMaxPosition = 10;
    public const int LowCtrMinImpressions = 50;
    public const double LowCtrFloor = 0.015;

    /// <summary>
    /// Summaries for every page seen in either window. <paramref name="asOfDate"/> is the newest date
    /// with data; the current window is the 28 days ending on it, the previous window the 28 before.
    /// </summary>
    public static IReadOnlyList<PageSearchSummary> Summarise(IEnumerable<PageSearchDay> days, DateTime asOfDate)
    {
        var end = asOfDate.Date;
        var curStart = end.AddDays(-(WindowDays - 1));
        var prevEnd = curStart.AddDays(-1);
        var prevStart = prevEnd.AddDays(-(WindowDays - 1));

        var byPage = days.GroupBy(d => d.Page, StringComparer.Ordinal);
        var result = new List<PageSearchSummary>();

        foreach (var g in byPage)
        {
            var cur = g.Where(d => d.Date.Date >= curStart && d.Date.Date <= end).ToList();
            var prev = g.Where(d => d.Date.Date >= prevStart && d.Date.Date <= prevEnd).ToList();

            var (clicks, imps, pos) = Aggregate(cur);
            var (pClicks, pImps, pPos) = Aggregate(prev);
            var ctr = imps == 0 ? 0 : (double)clicks / imps;

            result.Add(new PageSearchSummary
            {
                Page = g.Key,
                Path = PathOf(g.Key),
                Clicks = clicks, Impressions = imps, Ctr = ctr, Position = pos,
                PrevClicks = pClicks, PrevImpressions = pImps, PrevPosition = pPos,
                StrikingDistance  = imps >= StrikingMinImpressions && pos >= StrikingMinPosition && pos <= StrikingMaxPosition,
                LosingImpressions = pImps >= LosingMinPrevImpressions && imps <= pImps * LosingRatio,
                LowCtrForPosition = imps >= LowCtrMinImpressions && pos > 0 && pos <= LowCtrMaxPosition && ctr < LowCtrFloor,
            });
        }

        return result.OrderByDescending(s => s.Impressions).ThenBy(s => s.Page, StringComparer.Ordinal).ToList();
    }

    /// <summary>Clicks, impressions and impression-weighted position over a set of daily rows.</summary>
    public static (int Clicks, int Impressions, double Position) Aggregate(IEnumerable<PageSearchDay> rows)
    {
        int clicks = 0, imps = 0; double weighted = 0;
        foreach (var r in rows)
        {
            clicks += r.Clicks;
            imps += r.Impressions;
            weighted += r.Position * r.Impressions;
        }
        return (clicks, imps, imps == 0 ? 0 : Math.Round(weighted / imps, 2));
    }

    /// <summary>"https://www.example.com/foo-bar/" → "foo-bar"; the root → "". Query strings are dropped.</summary>
    public static string PathOf(string page)
    {
        if (Uri.TryCreate(page, UriKind.Absolute, out var uri))
            return Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
        var s = page;
        var q = s.IndexOf('?');
        if (q >= 0) s = s[..q];
        return s.Trim('/');
    }

    /// <summary>The summary for a post slug, or null when the page has never appeared in search.</summary>
    public static PageSearchSummary? ForSlug(IEnumerable<PageSearchSummary> summaries, string slug)
    {
        var target = (slug ?? string.Empty).Trim('/');
        return summaries.FirstOrDefault(s => string.Equals(s.Path, target, StringComparison.OrdinalIgnoreCase));
    }
}
