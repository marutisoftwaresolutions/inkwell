namespace Blog.Core.Domain;

/// <summary>How much a structured-data problem matters.</summary>
public enum LintSeverity
{
    /// <summary>Worth fixing, but the page still works. Never blocks publishing.</summary>
    Warning,
    /// <summary>Invalid or policy-violating output. Blocks publishing.</summary>
    Error
}

/// <summary>
/// One problem found in a post's structured data, tied to the block it came from so the editor can
/// point the author at the right place.
/// </summary>
/// <param name="Severity">Error blocks publish; Warning is advisory.</param>
/// <param name="Block">"FAQ", "Key Facts", "How-To" or "Roundup".</param>
/// <param name="Message">What is wrong, in the author's language — not a schema dump.</param>
/// <param name="Item">Optional 1-based index of the offending entry within the block.</param>
public record LintFinding(LintSeverity Severity, string Block, string Message, int? Item = null)
{
    public string Where => Item is null ? Block : $"{Block} #{Item}";
}
