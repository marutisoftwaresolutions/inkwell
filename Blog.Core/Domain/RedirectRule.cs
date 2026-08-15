namespace Blog.Core.Domain;

/// <summary>
/// A rule describing how a URL that no longer serves content is retired: moved (301/302) or
/// intentionally removed (410 Gone). Keyed by <see cref="From"/>, an app-relative path like
/// <c>/old-slug</c>.
/// </summary>
public class RedirectRule
{
    public string From { get; set; } = "";
    /// <summary>Destination path or absolute URL. Carries no meaning when <see cref="StatusCode"/> is 410.</summary>
    public string To   { get; set; } = "";
    /// <summary>301 (default), 302, or 410 — see <see cref="RedirectStatus"/>.</summary>
    public int StatusCode { get; set; } = RedirectStatus.MovedPermanently;

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
