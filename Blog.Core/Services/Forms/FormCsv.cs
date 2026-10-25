using System.Globalization;
using System.Text;
using System.Text.Json;
using Blog.Core.Domain;

namespace Blog.Core.Services.Forms;

/// <summary>
/// A form's submissions as CSV (ADR-0030): one column per field in the form's order, then the received time (UTC,
/// ISO 8601) and whether it was read. Visitor text that a spreadsheet would run as a formula (starting with =, +, -,
/// @, a tab or a carriage return) is prefixed with an apostrophe.
/// </summary>
public static class FormCsv
{
    public static string Write(IReadOnlyList<FormField> fields, IEnumerable<FormSubmission> rows)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(',', fields.Select(f => Cell(f.Label)).Append("Received (UTC)").Append("Read")).Append("\r\n");
        foreach (var r in rows)
        {
            Dictionary<string, string>? values;
            try { values = JsonSerializer.Deserialize<Dictionary<string, string>>(r.ValuesJson); }
            catch (JsonException) { values = null; }
            values ??= new();
            sb.AppendJoin(',', fields.Select(f => Cell(values.GetValueOrDefault(f.Key) ?? ""))
                    .Append(r.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                    .Append(r.ReadAt is null ? "no" : "yes"))
              .Append("\r\n");
        }
        return sb.ToString();
    }

    public static string Cell(string value)
    {
        var v = value ?? "";
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
        return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
