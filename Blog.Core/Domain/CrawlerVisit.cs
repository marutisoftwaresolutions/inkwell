namespace Blog.Core.Domain;

/// <summary>One identified crawler request. Kept apart from PageViews, which is human analytics.</summary>
public class CrawlerVisit
{
    public long     Id         { get; set; }
    public Guid     OwnerId    { get; set; }
    public string   Crawler    { get; set; } = "";
    public string   Operator   { get; set; } = "";
    public bool     IsAi       { get; set; }
    public string   Path       { get; set; } = "";
    public int      StatusCode { get; set; }
    public string?  UserAgent  { get; set; }
    public DateTime VisitedAt  { get; set; }
}

/// <summary>Per-crawler totals for the reporting window.</summary>
public class CrawlerActivity
{
    public string   Crawler    { get; set; } = "";
    public string   Operator   { get; set; } = "";
    public bool     IsAi       { get; set; }
    public int      Visits     { get; set; }
    public int      Pages      { get; set; }
    public DateTime? LastSeen  { get; set; }

    /// <summary>What the operator publishes this bot as doing; filled in from the identifier.</summary>
    public string Purpose { get; set; } = "";
}

/// <summary>A page and how much crawler attention it drew.</summary>
public class CrawledPage
{
    public string Path   { get; set; } = "";
    public int    Visits { get; set; }
    public int    Crawlers { get; set; }
}

/// <summary>One day of the AI-crawler trend. A class, not a ValueTuple — Dapper maps by column name.</summary>
public class CrawlerDay
{
    public DateTime Day    { get; set; }
    public int      Visits { get; set; }
}
