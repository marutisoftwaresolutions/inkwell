namespace Blog.Core.Domain;

/// <summary>
/// A grouped record of an application error (HTTP 4xx/5xx). Rows are de-duplicated by
/// <see cref="Fingerprint"/> (status code + normalized path + exception type), so repeated/similar
/// errors collapse into a single row with an <see cref="OccurrenceCount"/> and first/last-seen times
/// instead of flooding the table. Read-only in the admin UI (Admin → Errors).
/// </summary>
public class ErrorLog
{
    public long     Id              { get; set; }
    public string   Fingerprint     { get; set; } = "";
    public int      StatusCode      { get; set; }
    public string   Method          { get; set; } = "";
    public string   Path            { get; set; } = "";
    public string?  ExceptionType   { get; set; }
    public string?  Message         { get; set; }
    public string?  StackTrace      { get; set; }
    public string?  UserAgent       { get; set; }
    public string?  Referer         { get; set; }
    public int      OccurrenceCount { get; set; }
    public DateTime FirstSeenAt     { get; set; }
    public DateTime LastSeenAt      { get; set; }
}

/// <summary>Summary counts for the error dashboard tiles.</summary>
public class ErrorStats
{
    public int TotalGroups       { get; set; }
    public int TotalOccurrences  { get; set; }
    public int NotFoundGroups    { get; set; } // distinct 404 signatures
    public int ServerErrorGroups { get; set; } // distinct 5xx signatures
}
