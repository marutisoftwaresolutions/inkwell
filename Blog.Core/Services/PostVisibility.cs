namespace Blog.Core.Services;

/// <summary>
/// Whether a published post is listed. A public post appears everywhere a reader browses. An
/// unlisted post is published and opens at its own address for anyone who has the link, but is
/// left out of the home page, category, tag, author and series lists, search, related posts, the
/// feed, the sitemap and <c>llms.txt</c>, and asks search engines not to index it. It is not
/// private: anyone with the address can read it. Pure.
/// </summary>
public static class PostVisibility
{
    public const string Public = "public";
    public const string Unlisted = "unlisted";

    public static bool IsUnlisted(string? visibility) => string.Equals(visibility?.Trim(), Unlisted, StringComparison.OrdinalIgnoreCase);

    /// <summary>What to store for a value from a form or the API: "unlisted", otherwise "public".</summary>
    public static string Normalize(string? visibility) => IsUnlisted(visibility) ? Unlisted : Public;

    /// <summary>The SQL condition that keeps unlisted posts out of a list, for a Posts alias (or none).</summary>
    public static string ListedSql(string? alias = null)
    {
        var column = string.IsNullOrEmpty(alias) ? "Visibility" : alias + ".Visibility";
        return $"({column} IS NULL OR {column} <> '{Unlisted}')";
    }
}
