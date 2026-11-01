namespace Blog.Core.Services;

/// <summary>
/// Splits a T-SQL script into batches on <c>GO</c> separator lines, the way sqlcmd and SSMS do, so a
/// script written for either runs unchanged through SqlClient (which has no notion of GO). Pure.
/// Rules: GO must be alone on its line (surrounding whitespace allowed, any case, optional trailing
/// repeat count which is honoured); a GO inside a string or block comment is not special-cased, same
/// as the real tools; empty batches are dropped.
/// </summary>
public static class SqlBatchSplitter
{
    public static IReadOnlyList<string> Split(string script)
    {
        var batches = new List<string>();
        if (string.IsNullOrEmpty(script)) return batches;

        var current = new List<string>();
        foreach (var raw in script.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (IsGo(trimmed, out var repeat))
            {
                var batch = string.Join("\n", current).Trim();
                if (batch.Length > 0) for (var i = 0; i < repeat; i++) batches.Add(batch);
                current.Clear();
            }
            else current.Add(line);
        }
        var last = string.Join("\n", current).Trim();
        if (last.Length > 0) batches.Add(last);
        return batches;
    }

    private static bool IsGo(string trimmed, out int repeat)
    {
        repeat = 1;
        if (trimmed.Length < 2 || !trimmed.StartsWith("GO", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.Length == 2) return true;
        var rest = trimmed[2..].Trim();
        if (rest.StartsWith("--")) return true;
        return int.TryParse(rest, out repeat) && repeat > 0 && repeat <= 1000;
    }
}
