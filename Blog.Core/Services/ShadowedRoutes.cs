namespace Blog.Core.Services;

/// <summary>
/// Finds files in the web root that sit at the same path as a mapped route. Static-file middleware
/// runs before routing, so such a file silently replaces the dynamic endpoint: the route still
/// exists, still passes its tests, and is never reached. A stale <c>llms.txt</c> left on a server by
/// an earlier deploy did exactly this for months.
///
/// Pure: the caller supplies the route patterns and a file-exists probe, so the rule is testable
/// without a host or a file system.
/// </summary>
public static class ShadowedRoutes
{
    /// <summary>
    /// Route patterns that are plain literal paths (no <c>{parameter}</c> segments) and are shadowed by
    /// an existing file, as normalised relative paths (no leading slash). Parameterised routes are
    /// skipped: a file cannot shadow a pattern, only a concrete path.
    /// </summary>
    public static IReadOnlyList<string> Find(IEnumerable<string?> routePatterns, Func<string, bool> fileExists)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in routePatterns)
        {
            var path = Normalise(raw);
            if (path is null) continue;
            if (fileExists(path)) found.Add(path);
        }
        return found.ToList();
    }

    /// <summary>A literal, file-like relative path for the pattern, or null when it cannot be shadowed.</summary>
    public static string? Normalise(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        var p = pattern.Trim().TrimStart('/');
        if (p.Length == 0) return null;                       // "/" is the home route, never a file
        if (p.Contains('{') || p.Contains('}')) return null;  // parameterised
        if (p.Contains("..")) return null;                    // never probe outside the web root
        if (p.EndsWith('/')) return null;                     // a directory-like route
        return p;
    }
}
