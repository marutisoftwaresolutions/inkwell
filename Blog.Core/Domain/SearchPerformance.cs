namespace Blog.Core.Domain;

/// <summary>One Search Console row: a (date, page, query) cell with its four metrics. Stored as pulled.</summary>
public class SearchPerformanceRow
{
    public Guid OwnerId { get; set; }
    public DateTime Date { get; set; }
    public string Page { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public int Clicks { get; set; }
    public int Impressions { get; set; }
    public double Ctr { get; set; }
    public double Position { get; set; }
}

/// <summary>A page's metrics for one day, queries already summed. What the analyzer consumes.</summary>
public sealed record PageSearchDay(string Page, DateTime Date, int Clicks, int Impressions, double Position);

/// <summary>One query's metrics for a page over a window — the "what people typed" list in the editor.</summary>
public sealed record SearchQueryStat(string Query, int Clicks, int Impressions, double Ctr, double Position);

/// <summary>
/// A page's search performance over the current 28-day window against the 28 days before it, with
/// the three flags the striking-distance panel and Content Health filters are built on.
/// </summary>
public sealed class PageSearchSummary
{
    public string Page { get; init; } = string.Empty;
    /// <summary>Path of <see cref="Page"/> with surrounding slashes trimmed — matches a post slug.</summary>
    public string Path { get; init; } = string.Empty;

    public int Clicks { get; init; }
    public int Impressions { get; init; }
    /// <summary>Clicks ÷ impressions over the window; 0 when there were no impressions.</summary>
    public double Ctr { get; init; }
    /// <summary>Impression-weighted average position; 0 when there were no impressions.</summary>
    public double Position { get; init; }

    public int PrevClicks { get; init; }
    public int PrevImpressions { get; init; }
    public double PrevPosition { get; init; }

    /// <summary>Ranking just off the first page with real demand — one push from page one.</summary>
    public bool StrikingDistance { get; init; }
    /// <summary>Impressions fell hard against the previous window — the page is losing visibility.</summary>
    public bool LosingImpressions { get; init; }
    /// <summary>On page one with real impressions but almost nobody clicks — the snippet, not the rank, is the problem.</summary>
    public bool LowCtrForPosition { get; init; }

    public bool NeedsAttention => StrikingDistance || LosingImpressions || LowCtrForPosition;
    public bool HasData => Impressions > 0 || PrevImpressions > 0;
}
