namespace Blog.Core.Services;

/// <summary>
/// Turns what a reader typed into the words a search matches on. Each word must appear in the title or
/// the text (so "lens coating" finds a post that uses both words anywhere, not only that exact phrase),
/// and results can be ordered by where the words were found: the whole phrase in the title first, then
/// words in the title, then the description, then the body. Works on any SQL Server edition, with no
/// full-text index to install. Pure.
/// </summary>
public static class SearchTerms
{
    public const int MaxTerms = 6;
    public const int MaxLength = 100;

    /// <summary>Distinct words of two characters or more, at most <see cref="MaxTerms"/>; the whole text when it has no such word.</summary>
    public static IReadOnlyList<string> Parse(string? query)
    {
        var text = (query ?? "").Trim();
        if (text.Length == 0) return [];
        if (text.Length > MaxLength) text = text[..MaxLength];

        var terms = text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('"', '\'', ',', '.', ';', ':', '!', '?', '(', ')', '“', '”', '‘', '’'))
            .Where(w => w.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)
            .ToList();
        return terms.Count > 0 ? terms : [text];
    }

    /// <summary>The whole query as one phrase (trimmed, capped), for the "phrase in the title" boost.</summary>
    public static string Phrase(string? query)
    {
        var text = string.Join(' ', (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > MaxLength ? text[..MaxLength] : text;
    }

    /// <summary>
    /// A LIKE pattern that finds <paramref name="term"/> anywhere, with the wildcard characters a reader
    /// may have typed (%, _, [) made literal. Use with <c>ESCAPE '\'</c>.
    /// </summary>
    public static string Like(string term) =>
        "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";
}
