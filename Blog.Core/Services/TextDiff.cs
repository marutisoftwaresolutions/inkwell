namespace Blog.Core.Services;

public enum DiffKind { Same, Added, Removed }

public sealed record DiffLine(DiffKind Kind, string Text);

/// <summary>
/// Line-level diff for the revision compare screen: longest-common-subsequence on lines, so an
/// inserted paragraph shows as one added block rather than shifting everything after it. HTML is
/// split on tag boundaries first so a one-paragraph body still diffs paragraph by paragraph.
/// Bounded: past <see cref="MaxLines"/> lines on either side it falls back to a plain
/// removed/added listing rather than an O(n·m) table.
/// </summary>
public static class TextDiff
{
    public const int MaxLines = 4000;

    public static IReadOnlyList<DiffLine> Lines(string? oldText, string? newText)
    {
        var a = Split(oldText);
        var b = Split(newText);

        if (a.Count > MaxLines || b.Count > MaxLines)
            return a.Select(l => new DiffLine(DiffKind.Removed, l)).Concat(b.Select(l => new DiffLine(DiffKind.Added, l))).ToList();

        // LCS table
        var lcs = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
            for (var j = b.Count - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var result = new List<DiffLine>();
        int x = 0, y = 0;
        while (x < a.Count && y < b.Count)
        {
            if (a[x] == b[y]) { result.Add(new DiffLine(DiffKind.Same, a[x])); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) { result.Add(new DiffLine(DiffKind.Removed, a[x])); x++; }
            else { result.Add(new DiffLine(DiffKind.Added, b[y])); y++; }
        }
        while (x < a.Count) result.Add(new DiffLine(DiffKind.Removed, a[x++]));
        while (y < b.Count) result.Add(new DiffLine(DiffKind.Added, b[y++]));
        return result;
    }

    /// <summary>Counts of added and removed lines — the "+3 −1" badge.</summary>
    public static (int Added, int Removed) Count(IReadOnlyList<DiffLine> lines) =>
        (lines.Count(l => l.Kind == DiffKind.Added), lines.Count(l => l.Kind == DiffKind.Removed));

    /// <summary>Splits on newlines and on block-level tag boundaries; drops blank lines.</summary>
    public static List<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        var normalised = System.Text.RegularExpressions.Regex.Replace(text, @"</(p|div|li|h[1-6]|blockquote|pre|tr|section|article)>", "$0\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return normalised.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }
}
