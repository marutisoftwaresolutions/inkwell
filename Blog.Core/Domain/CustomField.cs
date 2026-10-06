namespace Blog.Core.Domain;

/// <summary>
/// One custom field a site defines (Admin → Settings → Custom fields), stored in the settings JSON blob.
/// Values live per post and per page in <c>CustomFieldsJson</c>, keyed by <see cref="Key"/>, which never
/// changes after the field is created so renaming the label keeps every value.
/// </summary>
public class CustomFieldDefinition
{
    /// <summary>Lower-case letters, digits and hyphens; derived from the first label.</summary>
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>One of <see cref="CustomFieldTypes"/>.</summary>
    public string Type { get; set; } = CustomFieldTypes.Text;
    /// <summary>One of <see cref="CustomFieldTargets"/>.</summary>
    public string AppliesTo { get; set; } = CustomFieldTargets.Posts;
    /// <summary>Shown to readers in the "Details" list and emitted as structured data. Off keeps it internal.</summary>
    public bool ShowOnPage { get; set; } = true;
}

public static class CustomFieldTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Date = "date";
    public const string Url = "url";
    public const string YesNo = "yesno";
    public static readonly IReadOnlyList<string> All = [Text, Number, Date, Url, YesNo];
}

public static class CustomFieldTargets
{
    public const string Posts = "posts";
    public const string Pages = "pages";
    public const string Both = "both";
    public static readonly IReadOnlyList<string> All = [Posts, Pages, Both];
}
