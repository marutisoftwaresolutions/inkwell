using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// Custom fields (ADR-0025): the site's definitions live in the settings blob, each post's and page's
/// values in its <c>CustomFieldsJson</c> as a flat <c>{ key: value }</c> object of invariant strings.
/// Everything that validates, stores or shows a value goes through here.
/// </summary>
public static class CustomFields
{
    public const int MaxFields = 30;
    public const int MaxLabelLength = 60;
    public const int MaxTextLength = 500;

    private static readonly Regex NotSlug = new("[^a-z0-9]+", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>A key from a label: lower-case letters, digits and single hyphens.</summary>
    public static string KeyOf(string? label)
    {
        // Accented letters keep their base letter ("Ölçü" → "olcu"); anything else becomes a hyphen.
        var plain = new string((label ?? "").Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var k = NotSlug.Replace(plain, "-").Trim('-');
        if (k.Length > 40) k = k[..40].TrimEnd('-');
        return k.Length == 0 ? "field" : k;
    }

    /// <summary>
    /// Cleans the definitions posted from Settings. A field that already exists keeps its key whatever its
    /// label becomes, so stored values stay attached; a new field gets a unique key from its label.
    /// Errors are keyed by the row's position.
    /// </summary>
    public static (List<CustomFieldDefinition> Fields, Dictionary<int, string> Errors) NormalizeDefinitions(
        IEnumerable<CustomFieldDefinition>? posted, IEnumerable<CustomFieldDefinition>? existing)
    {
        var known = new HashSet<string>((existing ?? []).Select(e => e.Key), StringComparer.Ordinal);
        var fields = new List<CustomFieldDefinition>();
        var errors = new Dictionary<int, string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var i = -1;
        foreach (var p in posted ?? [])
        {
            i++;
            if (p is null) continue;
            var label = (p.Label ?? "").Trim();
            if (label.Length == 0) { errors[i] = "Give this field a label, or remove it."; continue; }
            if (label.Length > MaxLabelLength) { errors[i] = $"Keep the label under {MaxLabelLength} characters."; continue; }
            if (fields.Count >= MaxFields) { errors[i] = $"A site can have up to {MaxFields} custom fields."; continue; }

            var key = p.Key is { Length: > 0 } k && known.Contains(k) && !used.Contains(k) ? k : null;
            if (key is null)
            {
                var stem = KeyOf(label);
                key = stem;
                for (var n = 2; used.Contains(key) || (known.Contains(key) && p.Key != key); n++) key = $"{stem}-{n}";
            }
            used.Add(key);
            fields.Add(new CustomFieldDefinition
            {
                Key = key,
                Label = label,
                Type = CustomFieldTypes.All.Contains(p.Type) ? p.Type : CustomFieldTypes.Text,
                AppliesTo = CustomFieldTargets.All.Contains(p.AppliesTo) ? p.AppliesTo : CustomFieldTargets.Posts,
                ShowOnPage = p.ShowOnPage,
            });
        }
        return (fields, errors);
    }

    /// <summary>The fields that apply to posts, or to pages.</summary>
    public static IReadOnlyList<CustomFieldDefinition> For(IEnumerable<CustomFieldDefinition>? definitions, bool pages) =>
        (definitions ?? []).Where(d => d.AppliesTo == CustomFieldTargets.Both
                                    || d.AppliesTo == (pages ? CustomFieldTargets.Pages : CustomFieldTargets.Posts)).ToList();

    /// <summary>Stored values; an unreadable or empty column is no values.</summary>
    public static Dictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.Ordinal);
        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return d is null ? new(StringComparer.Ordinal) : new(d.Where(kv => kv.Value is not null), StringComparer.Ordinal);
        }
        catch (JsonException) { return new(StringComparer.Ordinal); }
    }

    public sealed record CleanResult(string? Json, IReadOnlyDictionary<string, string> Values, IReadOnlyDictionary<string, string> Errors);

    /// <summary>
    /// Validates the values posted from an editor for <paramref name="definitions"/> (the fields that apply
    /// here). Values stored for fields not in the list (deleted from Settings, or for the other content type)
    /// are kept untouched, so removing a definition never deletes data. Empty means unset.
    /// </summary>
    public static CleanResult Clean(IReadOnlyList<CustomFieldDefinition> definitions, IDictionary<string, string?>? posted, string? existingJson)
    {
        var values = Parse(existingJson);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        posted ??= new Dictionary<string, string?>();
        foreach (var d in definitions)
        {
            values.Remove(d.Key);
            posted.TryGetValue(d.Key, out var raw);
            var v = (raw ?? "").Trim();
            if (d.Type == CustomFieldTypes.YesNo)
            {
                // Three states: not set, yes, no. Anything else (an old value) is left unset.
                if (v is "true" or "on") values[d.Key] = "true";
                else if (v == "false") values[d.Key] = "false";
                continue;
            }
            if (v.Length == 0) continue;
            var (ok, stored, message) = d.Type switch
            {
                CustomFieldTypes.Number => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                    ? (true, n.ToString(CultureInfo.InvariantCulture), null)
                    : (false, v, "Enter a number, such as 12 or 4.5."),
                CustomFieldTypes.Date => DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                    ? (true, dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null)
                    : (false, v, "Choose a date."),
                CustomFieldTypes.Url => Uri.TryCreate(v, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
                    ? (true, u.ToString(), null)
                    : (false, v, "Enter a full web address starting with https://."),
                _ => v.Length <= MaxTextLength ? (true, v, (string?)null) : (false, v, $"Keep it under {MaxTextLength} characters."),
            };
            values[d.Key] = stored;
            if (!ok) errors[d.Key] = message!;
        }
        var json = values.Count == 0 ? null : JsonSerializer.Serialize(values.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value), Json);
        return new CleanResult(json, values, errors);
    }

    /// <summary>
    /// Whether a stored value fits its field's type. It may not after the field's type was changed in Settings;
    /// the editor then shows the old value as text with a prompt to correct it, instead of a typed input that
    /// would show it blank and clear it on the next save.
    /// </summary>
    public static bool Fits(string type, string? value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        return type switch
        {
            CustomFieldTypes.Number => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            CustomFieldTypes.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            CustomFieldTypes.Url => Uri.TryCreate(value, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp),
            CustomFieldTypes.YesNo => value is "true" or "false",
            _ => true,
        };
    }

    /// <summary>One value as readers see it.</summary>
    public sealed record Display(string Key, string Label, string Type, string Value, string Text);

    /// <summary>The fields shown to readers, in Settings order, with a value. Nothing is shown for an unset field.</summary>
    public static IReadOnlyList<Display> Visible(IEnumerable<CustomFieldDefinition>? definitions, bool pages, string? json, DateDisplayOptions? dates = null)
    {
        var values = Parse(json);
        if (values.Count == 0) return [];
        var list = new List<Display>();
        foreach (var d in For(definitions, pages).Where(d => d.ShowOnPage))
        {
            if (!values.TryGetValue(d.Key, out var v) || v.Length == 0) continue;
            var text = d.Type switch
            {
                CustomFieldTypes.Date when DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                    => DateDisplay.Format(dt, dates ?? DateDisplayOptions.Default),   // the site's date format (ADR-0027)
                CustomFieldTypes.YesNo => v switch { "true" => "Yes", "false" => "No", _ => "" },
                CustomFieldTypes.Url when Uri.TryCreate(v, UriKind.Absolute, out var u)
                    => (u.Host + u.AbsolutePath).TrimEnd('/'),
                _ => v,
            };
            if (text.Length > 0) list.Add(new Display(d.Key, d.Label, d.Type, v, text));
        }
        return list;
    }

    /// <summary>The schema.org value for a <c>PropertyValue</c>: a number, a boolean, or the text.</summary>
    public static object SchemaValue(Display d) => d.Type switch
    {
        CustomFieldTypes.Number when decimal.TryParse(d.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) => n,
        CustomFieldTypes.YesNo => d.Value == "true",
        _ => d.Value,
    };

    // ── The API (v1.0.9, ADR-0025 follow-up) ────────────────────────────────────

    /// <summary>
    /// The values an API response carries: every field that applies here, in Settings order, as a number, a
    /// boolean, a string or null when unset. <paramref name="includeHidden"/> false limits it to the fields
    /// readers see, which is what an anonymous caller gets. A stored value that no longer fits its field's type
    /// (the type changed in Settings) is returned as the stored text rather than dropped.
    /// </summary>
    public static Dictionary<string, object?> ToApi(IEnumerable<CustomFieldDefinition>? applicable, string? json, bool includeHidden)
    {
        var values = Parse(json);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var d in applicable ?? [])
        {
            if (!includeHidden && !d.ShowOnPage) continue;
            if (!values.TryGetValue(d.Key, out var v) || v.Length == 0) { result[d.Key] = null; continue; }
            result[d.Key] = d.Type switch
            {
                CustomFieldTypes.Number when decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) => n,
                CustomFieldTypes.YesNo when v is "true" or "false" => v == "true",
                _ => v,
            };
        }
        return result;
    }

    /// <summary>
    /// Applies the values an API caller sent. Only the keys sent change: a value sets the field, null or an empty
    /// string clears it, and every other stored value is kept. Values are validated exactly as in the Desk
    /// (<see cref="Clean"/>). A key that is not a field for this content type, or a value that is an object or a
    /// list, is an error, so nothing a caller sends is dropped without saying so.
    /// </summary>
    public static CleanResult FromApi(IReadOnlyList<CustomFieldDefinition> applicable, IReadOnlyDictionary<string, JsonElement>? sent, string? existingJson)
    {
        if (sent is null || sent.Count == 0) return new CleanResult(existingJson, Parse(existingJson), new Dictionary<string, string>());
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var raw = new Dictionary<string, string?>(StringComparer.Ordinal);
        var touched = new List<CustomFieldDefinition>();
        foreach (var (key, element) in sent)
        {
            var d = applicable.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal));
            if (d is null) { errors[key] = "There is no custom field with this key here. Field keys are listed in Settings → Custom fields."; continue; }
            string? text = element.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };
            if (text is null) { errors[key] = "Send a string, a number, true, false or null."; continue; }
            // A yes/no field takes only true, false or null: a stray string would otherwise be stored as unset.
            if (d.Type == CustomFieldTypes.YesNo && text.Length > 0 && text is not ("true" or "false"))
            { errors[key] = "Send true, false or null."; continue; }
            raw[key] = text;
            touched.Add(d);
        }
        var cleaned = Clean(touched, raw, existingJson);
        foreach (var (k, m) in cleaned.Errors) errors[k] = m;
        return new CleanResult(cleaned.Json, cleaned.Values, errors);
    }
}
