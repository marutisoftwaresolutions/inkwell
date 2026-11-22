using System.Text;

namespace Blog.Core.Services;

/// <summary>
/// Reads and writes the subscriber list as CSV, for moving a list in from another newsletter tool
/// and taking it out again. Import is forgiving about what a spreadsheet produces (a header row or
/// none, commas or semicolons, quotes, extra columns) and strict about what it accepts: one valid
/// address per row, each address once, at most <see cref="MaxRows"/> rows. Export guards every cell
/// against being read as a formula when the file is opened in a spreadsheet. Pure.
/// </summary>
public static class SubscriberCsv
{
    public const int MaxRows = 5000;
    public const int MaxFileBytes = 1024 * 1024;

    public sealed record Row(string Email, string? Name);

    public sealed record ParseResult(IReadOnlyList<Row> Rows, int Invalid, int Duplicates, bool Truncated);

    public static ParseResult Parse(string? text)
    {
        var rows = new List<Row>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int invalid = 0, duplicates = 0;
        var truncated = false;
        int emailColumn = -1, nameColumn = -1;
        var first = true;

        foreach (var line in (text ?? "").TrimStart('﻿').Split('\n'))
        {
            var trimmed = line.Trim('\r', ' ', '\t');
            if (trimmed.Length == 0) continue;
            var cells = SplitLine(trimmed);

            if (first)
            {
                first = false;
                // A header row names its columns; otherwise the first column that looks like an address is the email.
                emailColumn = cells.FindIndex(c => c.Trim().Equals("email", StringComparison.OrdinalIgnoreCase) || c.Trim().Equals("e-mail", StringComparison.OrdinalIgnoreCase) || c.Trim().Equals("email address", StringComparison.OrdinalIgnoreCase));
                if (emailColumn >= 0)
                {
                    nameColumn = cells.FindIndex(c => c.Trim().Equals("name", StringComparison.OrdinalIgnoreCase) || c.Trim().Equals("full name", StringComparison.OrdinalIgnoreCase) || c.Trim().Equals("first name", StringComparison.OrdinalIgnoreCase));
                    continue;
                }
                emailColumn = cells.FindIndex(IsEmail);
                if (emailColumn < 0) { invalid++; emailColumn = 0; continue; }
                nameColumn = cells.Count > 1 ? (emailColumn == 0 ? 1 : 0) : -1;
            }

            var email = emailColumn < cells.Count ? cells[emailColumn].Trim() : "";
            if (!IsEmail(email)) { invalid++; continue; }
            if (!seen.Add(email)) { duplicates++; continue; }
            if (rows.Count >= MaxRows) { truncated = true; break; }

            var name = nameColumn >= 0 && nameColumn < cells.Count ? Clean(cells[nameColumn]) : null;
            rows.Add(new Row(email.ToLowerInvariant(), string.IsNullOrEmpty(name) || IsEmail(name) ? null : name));
        }
        return new ParseResult(rows, invalid, duplicates, truncated);
    }

    public static string Write(IEnumerable<(string Email, string? Name, string Status, DateTime? ConfirmedAtUtc)> members)
    {
        var sb = new StringBuilder("email,name,status,confirmed_at_utc\r\n");
        foreach (var m in members)
            sb.Append(Cell(m.Email)).Append(',').Append(Cell(m.Name)).Append(',').Append(Cell(m.Status)).Append(',')
              .Append(m.ConfirmedAtUtc?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") ?? "").Append("\r\n");
        return sb.ToString();
    }

    /// <summary>One address: something@domain.tld, no spaces, no control characters, at most 254 characters.</summary>
    public static bool IsEmail(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Length is < 6 or > 254 || v.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ',' or ';' or '<' or '>' or '"')) return false;
        var at = v.IndexOf('@');
        if (at < 1 || at != v.LastIndexOf('@')) return false;
        var domain = v[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        return dot > 0 && dot < domain.Length - 2 && !domain.StartsWith('.') && !domain.Contains("..");
    }

    /// <summary>A cell safe for a spreadsheet: quoted when needed, and never starting with a formula character.</summary>
    public static string Cell(string? value)
    {
        var v = (value ?? "").Replace("\r", " ").Replace("\n", " ");
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t') v = "'" + v;
        return v.Contains(',') || v.Contains('"') || v.StartsWith(' ') || v.EndsWith(' ') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    private static string Clean(string value)
    {
        var v = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return v.Length > 120 ? v[..120] : v;
    }

    /// <summary>Splits one line on commas (or semicolons when the line has no comma), honouring double quotes.</summary>
    private static List<string> SplitLine(string line)
    {
        var separator = line.Contains(',') ? ',' : ';';
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else current.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == separator) { cells.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        cells.Add(current.ToString());
        return cells;
    }
}
