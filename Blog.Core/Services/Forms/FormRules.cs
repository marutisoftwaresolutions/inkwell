using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Blog.Core.Services.Forms;

/// <summary>The kinds of field a contact form can have (ADR-0030). Names are stored: never rename one.</summary>
public static class FormFieldTypes
{
    public const string Name = "name";
    public const string Email = "email";
    public const string Text = "text";
    public const string LongText = "textarea";
    public const string Choice = "select";
    public const string Checkbox = "checkbox";
    public const string Consent = "consent";

    public static readonly IReadOnlyList<(string Type, string Label)> All =
    [
        (Name, "Name"), (Email, "Email address"), (Text, "Short text"), (LongText, "Long text"),
        (Choice, "Choice list"), (Checkbox, "Checkbox"), (Consent, "Consent (must be ticked)"),
    ];

    public static bool IsKnown(string? type) => All.Any(t => t.Type == type);
}

/// <summary>One field of a form, as the owner defined it.</summary>
public sealed record FormField
{
    /// <summary>Stable key used in submissions, CSV columns and webhook payloads.</summary>
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Type { get; init; } = FormFieldTypes.Text;
    public bool Required { get; init; }
    /// <summary>Choices for a choice list, one per entry.</summary>
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();
    /// <summary>A short hint under the field.</summary>
    public string? Help { get; init; }
}

/// <summary>A problem with a form definition or a submission, and the field it belongs to (null for the whole form).</summary>
public sealed record FormProblem(string? Field, string Message);

/// <summary>
/// The rules behind contact forms (ADR-0030), pure so they can be tested without a database: what a valid form
/// definition is, how a key is made, and whether a visitor's submission is complete and well-formed.
/// </summary>
public static class FormRules
{
    public const int MaxFields = 20;
    public const int MaxLabelLength = 120;
    public const int MaxOptions = 30;
    public const int MaxShortTextLength = 300;
    public const int MaxLongTextLength = 5000;
    public const int MaxKeyLength = 60;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private static readonly Regex KeyPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"^[^@\s<>""]+@[^@\s<>""]+\.[^@\s<>""]+$", RegexOptions.Compiled);

    // ── Definition ───────────────────────────────────────────────────────────

    public static IReadOnlyList<FormField> ParseFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<FormField>();
        try { return JsonSerializer.Deserialize<List<FormField>>(json, Json) ?? new List<FormField>(); }
        catch (JsonException) { return Array.Empty<FormField>(); }
    }

    public static string SerializeFields(IEnumerable<FormField> fields) => JsonSerializer.Serialize(fields, Json);

    /// <summary>Turns a name or label into a key: lowercase letters, digits and single hyphens.</summary>
    public static string MakeKey(string? text, int max = MaxKeyLength)
    {
        var sb = new StringBuilder();
        foreach (var ch in (text ?? "").Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            else sb.Append('-');
        }
        var key = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (key.Length > max) key = key[..max].Trim('-');
        return key;
    }

    /// <summary>
    /// Cleans a definition the owner submitted: trims labels and options, fills in missing keys from labels and makes
    /// them unique, drops options from non-choice fields. Returns the cleaned fields and every problem left.
    /// </summary>
    public static (IReadOnlyList<FormField> Fields, IReadOnlyList<FormProblem> Problems) CleanDefinition(
        string? name, string? key, IEnumerable<FormField> fields, string? notifyEmail, int retentionDays)
    {
        var problems = new List<FormProblem>();
        if (string.IsNullOrWhiteSpace(name)) problems.Add(new("Name", "Give the form a name, such as “Contact us”."));
        else if (name.Trim().Length > MaxLabelLength) problems.Add(new("Name", $"Keep the name under {MaxLabelLength} characters."));
        if (string.IsNullOrWhiteSpace(key) || !KeyPattern.IsMatch(key) || key.Length > MaxKeyLength)
            problems.Add(new("FormKey", "The form address can contain only lowercase letters, digits and hyphens."));
        if (!string.IsNullOrWhiteSpace(notifyEmail) && !EmailPattern.IsMatch(notifyEmail.Trim()))
            problems.Add(new("NotifyEmail", "Enter one email address, such as you@example.com, or leave it empty."));
        if (retentionDays is < 0 or > 3650)
            problems.Add(new("RetentionDays", "Keep submissions for 0 (until deleted) to 3650 days."));

        var cleaned = new List<FormField>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var list = fields.ToList();
        if (list.Count == 0) problems.Add(new("Fields", "Add at least one field."));
        if (list.Count > MaxFields) problems.Add(new("Fields", $"A form can have at most {MaxFields} fields."));
        for (var i = 0; i < list.Count && i < MaxFields; i++)
        {
            var f = list[i];
            var label = (f.Label ?? "").Trim();
            var n = i + 1;
            // Keyed by row ("Fields[2]"), so the editor shows each message under the field it is about.
            if (label.Length == 0) problems.Add(new($"Fields[{i}]", "This field needs a label."));
            else if (label.Length > MaxLabelLength) problems.Add(new($"Fields[{i}]", $"Keep the label under {MaxLabelLength} characters."));
            var type = FormFieldTypes.IsKnown(f.Type) ? f.Type : FormFieldTypes.Text;
            var fieldKey = MakeKey(string.IsNullOrWhiteSpace(f.Key) ? label : f.Key, 40);
            if (fieldKey.Length == 0) fieldKey = "field";
            var unique = fieldKey;
            for (var k = 2; !used.Add(unique); k++) unique = $"{fieldKey}-{k}";
            var options = type == FormFieldTypes.Choice
                ? f.Options.Select(o => (o ?? "").Trim()).Where(o => o.Length > 0).Distinct(StringComparer.Ordinal).Take(MaxOptions).ToList()
                : new List<string>();
            if (type == FormFieldTypes.Choice && options.Count < 2) problems.Add(new($"Fields[{i}]", "A choice list needs at least two choices, one per line."));
            cleaned.Add(new FormField
            {
                Key = unique, Label = label, Type = type,
                Required = type == FormFieldTypes.Consent || f.Required,   // consent means nothing unless it is required
                Options = options,
                Help = string.IsNullOrWhiteSpace(f.Help) ? null : f.Help.Trim(),
            });
        }
        return (cleaned, problems);
    }

    // ── Submission ───────────────────────────────────────────────────────────

    /// <summary>
    /// Checks a submission against the form's fields. Returns the cleaned values (only the form's own fields, trimmed,
    /// checkboxes as "yes"/"no") and the problems, each placed at its field.
    /// </summary>
    public static (IReadOnlyDictionary<string, string> Values, IReadOnlyList<FormProblem> Problems) CheckSubmission(
        IReadOnlyList<FormField> fields, Func<string, string?> read)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<FormProblem>();
        foreach (var f in fields)
        {
            var raw = (read(f.Key) ?? "").Trim();
            switch (f.Type)
            {
                case FormFieldTypes.Checkbox:
                case FormFieldTypes.Consent:
                    var ticked = raw is "yes" or "on" or "true";
                    if (f.Required && !ticked)
                        problems.Add(new(f.Key, f.Type == FormFieldTypes.Consent ? "Tick this box to send the form." : $"Tick “{f.Label}” to continue."));
                    values[f.Key] = ticked ? "yes" : "no";
                    continue;
                case FormFieldTypes.Choice:
                    if (raw.Length > 0 && !f.Options.Contains(raw, StringComparer.Ordinal)) { problems.Add(new(f.Key, "Choose one of the options.")); continue; }
                    break;
                case FormFieldTypes.Email:
                    if (raw.Length > 0 && (raw.Length > 254 || !EmailPattern.IsMatch(raw))) { problems.Add(new(f.Key, "Enter an email address, such as you@example.com.")); continue; }
                    break;
            }
            var max = f.Type == FormFieldTypes.LongText ? MaxLongTextLength : MaxShortTextLength;
            if (f.Required && raw.Length == 0) problems.Add(new(f.Key, f.Type switch
            {
                FormFieldTypes.Choice => "Choose one of the options.",
                FormFieldTypes.Name => "Enter your name.",
                FormFieldTypes.Email => "Enter your email address.",
                _ => $"Fill in “{f.Label}”.",
            }));
            else if (raw.Length > max) problems.Add(new(f.Key, $"Keep this under {max:N0} characters."));
            else values[f.Key] = raw;
        }
        return (values, problems);
    }

    /// <summary>The visitor's email, when the form has an email field and it was filled in (for Reply-To).</summary>
    public static string? ReplyAddress(IReadOnlyList<FormField> fields, IReadOnlyDictionary<string, string> values) =>
        fields.Where(f => f.Type == FormFieldTypes.Email).Select(f => values.GetValueOrDefault(f.Key)).FirstOrDefault(v => !string.IsNullOrEmpty(v));

    /// <summary>Repeat-detection key: the values, normalised, hashed.</summary>
    public static string ContentHash(IReadOnlyDictionary<string, string> values)
    {
        var text = string.Join("\n", values.OrderBy(v => v.Key, StringComparer.Ordinal)
            .Select(v => v.Key + "=" + CommentSpamFilter.NormalizeForDuplicate(v.Value)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>A one-line summary for the Desk's search: the values, shortened.</summary>
    public static string SearchText(IReadOnlyDictionary<string, string> values)
    {
        var text = string.Join(" · ", values.Values.Where(v => v.Length > 0 && v != "yes" && v != "no"));
        return text.Length <= 1000 ? text : text[..1000];
    }
}

/// <summary>Everything the form spam filter needs, gathered by the caller (ADR-0030).</summary>
public sealed record FormSpamCheck(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<FormField> Fields,
    string? HoneypotValue,
    CommentFormTokenState TokenState,
    TimeSpan? FormAge,
    bool IsRepeat,
    int RecentFromSameAddress);

/// <summary>
/// The comment spam rules (<see cref="CommentSpamFilter"/>), applied to a contact form: honeypot, signed timing token,
/// no markup in any field, a link limit across the whole submission, no name made of a link, a repeat check and a
/// per-address rate limit. Structural, not a word list, for the same reason as comments.
/// </summary>
public static class FormSpamFilter
{
    public const int MaxPerAddressInWindow = 5;
    public static readonly TimeSpan RateWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromDays(30);

    public static CommentSpamVerdict Evaluate(FormSpamCheck s)
    {
        if (!string.IsNullOrWhiteSpace(s.HoneypotValue)) return new(CommentSpamAction.RejectSilently, "honeypot field filled");
        switch (s.TokenState)
        {
            case CommentFormTokenState.Missing: return new(CommentSpamAction.RejectSilently, "posted without fetching the form");
            case CommentFormTokenState.Invalid: return new(CommentSpamAction.AskToRetry, "form token did not verify");
        }
        if (s.FormAge is null) return new(CommentSpamAction.AskToRetry, "form token carried no timestamp");
        if (s.FormAge.Value < TimeSpan.FromSeconds(CommentSpamFilter.MinSecondsToWrite))
            return new(CommentSpamAction.RejectSilently, $"submitted {s.FormAge.Value.TotalSeconds:0.#}s after the form was rendered");
        if (s.FormAge.Value > TimeSpan.FromHours(CommentSpamFilter.MaxFormAgeHours))
            return new(CommentSpamAction.AskToRetry, "form older than 24 hours");

        var links = 0;
        foreach (var f in s.Fields)
        {
            if (!s.Values.TryGetValue(f.Key, out var v) || v.Length == 0) continue;
            if (CommentSpamFilter.ContainsMarkup(v)) return new(CommentSpamAction.RejectSilently, $"field {f.Key} contains markup");
            var n = CommentSpamFilter.CountLinks(v);
            if (f.Type == FormFieldTypes.Name && (n > 0 || v.Contains('<'))) return new(CommentSpamAction.RejectSilently, "name contains a link");
            if (f.Type != FormFieldTypes.Email) links += n;
        }
        if (links > CommentSpamFilter.MaxLinks) return new(CommentSpamAction.RejectSilently, $"submission contains {links} links");
        if (s.IsRepeat) return new(CommentSpamAction.RejectSilently, "identical submission already received");
        if (s.RecentFromSameAddress >= MaxPerAddressInWindow) return new(CommentSpamAction.RejectSilently, "too many submissions from this address");
        return CommentSpamVerdict.Accept;
    }
}
