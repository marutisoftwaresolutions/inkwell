namespace Blog.Web.Models;

public class AnalyticsDashboardViewModel
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string Period { get; set; } = "30d";

    // Summary cards
    public int TotalViews { get; set; }
    public int UniquePages { get; set; }
    public string TopSource { get; set; } = "—";
    public int TotalReferrers { get; set; }

    // Chart data
    public List<DailyViewCount> DailyCounts { get; set; } = [];
    public List<SourceCount> SourceBreakdown { get; set; } = [];

    // Tables
    public List<PageViewCount> TopPages { get; set; } = [];
    public List<ReferrerCount> TopReferrers { get; set; } = [];
    public List<CampaignCount> Campaigns { get; set; } = [];
    public List<CountryCount> TopCountries { get; set; } = [];
    public List<RegionCount> TopRegions { get; set; } = [];
}

public record DailyViewCount(string Date, int Count);
public record SourceCount(string Label, string Source, string Medium, int Count);
public record PageViewCount(string Path, int Views);
public record ReferrerCount(string Referrer, int Count);
public record CampaignCount(string Campaign, int Count);
public record CountryCount(string Country, int Count);
public record RegionCount(string Region, int Count);
