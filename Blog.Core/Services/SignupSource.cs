namespace Blog.Core.Services;

/// <summary>
/// Works out which page of this site a newsletter sign-up came from, so the owner can see which posts
/// bring subscribers. It keeps the path only: no query string, no fragment, nothing from another site
/// and nothing about the reader. The sign-up form is posted from the page it sits on, so the browser's
/// Referer is that page. Pure.
/// </summary>
public static class SignupSource
{
    public const int MaxLength = 200;

    /// <summary>
    /// The path of the page on <paramref name="host"/> that <paramref name="referer"/> names, or null when
    /// there is no referer, it is another site, or it is the sign-up page itself.
    /// </summary>
    public static string? FromReferer(string? referer, string? host)
    {
        if (string.IsNullOrWhiteSpace(referer) || string.IsNullOrWhiteSpace(host)) return null;
        if (!Uri.TryCreate(referer.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var refererHost = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        if (!string.Equals(refererHost, host.Trim(), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Host, host.Trim(), StringComparison.OrdinalIgnoreCase)) return null;

        var path = uri.AbsolutePath;
        if (string.IsNullOrEmpty(path)) path = "/";
        if (path.Any(char.IsControl)) return null;
        if (path.StartsWith("/newsletter", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)) return null;
        path = path.Length > 1 ? path.TrimEnd('/') : path;
        return path.Length > MaxLength ? path[..MaxLength] : path.ToLowerInvariant();
    }
}
