namespace Blog.Core.Domain;

public class PageView
{
    public long Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? Referrer { get; set; }
    public string? Source { get; set; }
    public string? Medium { get; set; }
    public string? Campaign { get; set; }
    public string? Term { get; set; }
    public string? Content { get; set; }
    public string? IpHash { get; set; }
    public string? UserAgent { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Everything Admin → Analytics shows for one date range, read in one stored-procedure call
/// (<c>usp_Analytics_Report</c>, ADR-0037). Counts cover content pages only (home and post slugs).
/// </summary>
public class AnalyticsReport
{
    public int Total { get; init; }
    public IReadOnlyList<(DateTime Date, int Count)> Daily { get; init; } = [];
    public IReadOnlyList<(string Source, string Medium, int Count)> Sources { get; init; } = [];
    public IReadOnlyList<(string Path, int Views)> TopPages { get; init; } = [];
    public IReadOnlyList<(string Referrer, int Count)> Referrers { get; init; } = [];
    public IReadOnlyList<(string Campaign, int Count)> Campaigns { get; init; } = [];
    public IReadOnlyList<(string Country, int Count)> Countries { get; init; } = [];
    public IReadOnlyList<(string Region, int Count)> Regions { get; init; } = [];
}
