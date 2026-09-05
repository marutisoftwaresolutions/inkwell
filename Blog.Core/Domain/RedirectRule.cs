namespace Blog.Core.Domain;

/// <summary>
/// A rule describing how a URL that no longer serves content is retired: moved (301/302) or
/// intentionally removed (410 Gone). Keyed by <see cref="From"/>, an app-relative path like
/// <c>/old-slug</c>.
/// </summary>
public class RedirectRule
{
    public Guid Id { get; set; }
    public string From { get; set; } = "";
    /// <summary>Destination path or absolute URL. Carries no meaning when <see cref="StatusCode"/> is 410.</summary>
    public string To   { get; set; } = "";
    /// <summary>301 (default), 302, or 410 — see <see cref="RedirectStatus"/>.</summary>
    public int StatusCode { get; set; } = RedirectStatus.MovedPermanently;

    /// <summary>When the rule was written. Rules accrue silently from slug changes and imports,
    /// so the age of a rule is often the only clue to where it came from.</summary>
    public DateTime CreatedAt { get; set; }

    public bool IsGone => StatusCode == RedirectStatus.Gone;
}

public static class RedirectStatus
{
    public const int MovedPermanently = 301;
    public const int Found            = 302;
    /// <summary>The resource is intentionally removed and will not return — de-indexes faster than a 404.</summary>
    public const int Gone             = 410;

    public static bool IsSupported(int statusCode) =>
        statusCode is MovedPermanently or Found or Gone;
}

/// <summary>
/// One 404 path as the error log recorded it, before triage. `Referer` is the most recent one seen,
/// which is the strongest evidence a real link somewhere points at this URL.
/// </summary>
public class NotFoundGroup
{
    public string   Path       { get; set; } = "";
    public int      Hits       { get; set; }
    public DateTime LastSeenAt { get; set; }
    public string?  Referer    { get; set; }
}

/// <summary>Why a 404 is worth an operator's attention — or, for <see cref="Unmatched"/>, why it is not.</summary>
public enum NotFoundKind
{
    /// <summary>Nothing connects it to this site's content. On a public site most 404s are scanners.</summary>
    Unmatched = 0,
    /// <summary>Another page links here, so a reader can actually reach the dead end.</summary>
    Referred = 1,
    /// <summary>Close enough to a real published slug to be a rename, a typo, or a stale external link.</summary>
    NearMiss = 2
}

/// <summary>A triaged 404: what was requested, whether it matters, and the slug it probably meant.</summary>
public class NotFoundCandidate
{
    public string       Path          { get; set; } = "";
    public int          Hits          { get; set; }
    public DateTime     LastSeenAt    { get; set; }
    public string?      Referer       { get; set; }
    public NotFoundKind Kind          { get; set; }

    /// <summary>The published slug this most likely meant, when one is close enough. Null otherwise.</summary>
    public string?      SuggestedSlug { get; set; }

    /// <summary>0–1 closeness to <see cref="SuggestedSlug"/>. 0 when there is no suggestion.</summary>
    public double       Similarity    { get; set; }
}
