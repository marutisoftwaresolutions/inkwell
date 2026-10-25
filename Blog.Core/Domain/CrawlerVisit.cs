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
    /// <summary>The query string without its leading "?", truncated to 512 characters; null when there was none.</summary>
    public string?  Query      { get; set; }
    public int      StatusCode { get; set; }
    public string?  UserAgent  { get; set; }
    public DateTime VisitedAt  { get; set; }

    /// <summary>true: the address is the operator's; false: checked and it is not (a spoofed User-Agent);
    /// null: the operator publishes no verification method, or the check could not run.</summary>
    public bool?    Verified   { get; set; }
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

    /// <summary>Visits from an address the operator publishes as its own.</summary>
    public int      VerifiedVisits { get; set; }

    /// <summary>Visits whose address failed the operator's check — someone else using the name.</summary>
    public int      FailedVisits   { get; set; }

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

/// <summary>A path and query string crawlers requested, for the "what are they actually fetching" table.</summary>
public class CrawledRequest
{
    public string Path     { get; set; } = "";
    public string Query    { get; set; } = "";
    public int    Visits   { get; set; }
    public int    Crawlers { get; set; }
}

/// <summary>
/// Everything Admin → AI Crawlers shows for one window, read in one stored-procedure call
/// (<c>usp_CrawlerVisits_Report</c>, ADR-0037) so the visits table is collapsed once per load.
/// </summary>
public class CrawlerReport
{
    public IReadOnlyList<CrawlerActivity> Activity { get; init; } = [];
    public IReadOnlyList<CrawledPage>     TopPages { get; init; } = [];
    public IReadOnlyList<CrawledRequest>  Queried  { get; init; } = [];
    public IReadOnlyList<CrawlerDay>      Daily    { get; init; } = [];
}
