using System.Text.RegularExpressions;

namespace Blog.Core.Services;

/// <summary>What the caller learned about the form-timing token that accompanies a comment.</summary>
public enum CommentFormTokenState
{
    /// <summary>No token was posted at all — a browser that rendered the form always sends one.</summary>
    Missing,
    /// <summary>A token was posted but does not verify (tampered, or issued under a key that no longer exists).</summary>
    Invalid,
    Valid,
}

/// <summary>What to do with a comment submission.</summary>
public enum CommentSpamAction
{
    /// <summary>Store the comment as usual.</summary>
    Accept,
    /// <summary>
    /// Discard the comment but answer exactly as a moderated submission would. The sender learns nothing
    /// about which rule fired, so an automated poster cannot tune its way past the filter.
    /// </summary>
    RejectSilently,
    /// <summary>
    /// The submission cannot be trusted but is consistent with a real reader (an expired form, a token
    /// issued before an app restart). Show a message that asks them to reload and try again.
    /// </summary>
    AskToRetry,
}

/// <summary>Everything the filter needs, gathered by the caller. No I/O happens inside the filter.</summary>
public sealed record CommentSubmission(
    string? AuthorName,
    string? AuthorEmail,
    string? Content,
    /// <summary>Value of the hidden field no human ever sees. Anything but empty means a form-filler.</summary>
    string? HoneypotValue,
    CommentFormTokenState TokenState,
    /// <summary>How long ago the form was rendered; null when the token is missing or invalid.</summary>
    TimeSpan? FormAge,
    /// <summary>Whether an identical body was already posted recently, under any name and any status.</summary>
    bool IsDuplicateOfRecentComment,
    /// <summary>Comments already stored from the same address inside the rate window.</summary>
    int RecentCommentsFromSameAddress);

public readonly record struct CommentSpamVerdict(CommentSpamAction Action, string Reason)
{
    public static readonly CommentSpamVerdict Accept = new(CommentSpamAction.Accept, "");
    public bool IsSpam => Action == CommentSpamAction.RejectSilently;
}

/// <summary>
/// Pure rules for deciding whether a public comment submission is automated or unsolicited
/// advertising. Deliberately structural — links, markup, timing, repetition — rather than a word list,
/// because a word list produces false positives on the very vocabulary a niche blog's readers use.
///
/// The rules mirror the four ways comment spam actually arrives on a live site:
///   • form-fillers that post raw HTML anchors and BBCode for the backlink;
///   • lead-generation mailers that post the same 1,500-character pitch under several names;
///   • scripts that submit the form within a second of fetching it, or without fetching it at all;
///   • repeated submissions from one address in a short burst.
///
/// The filter never sees the database; the caller supplies the duplicate and rate facts. That keeps
/// every rule unit-testable and keeps the decision in one place.
/// </summary>
public static class CommentSpamFilter
{
    /// <summary>Nobody reads a post and writes a comment in under this many seconds.</summary>
    public const int MinSecondsToWrite = 4;
    /// <summary>A form left open longer than this must be reloaded before it is trusted.</summary>
    public const int MaxFormAgeHours = 24;
    /// <summary>More links than this in one comment is advertising, not conversation.</summary>
    public const int MaxLinks = 2;
    public const int MaxContentLength = 4000;
    public const int MaxAuthorNameLength = 100;
    /// <summary>Comments accepted from one address inside <see cref="RateWindow"/> before further ones are dropped.</summary>
    public const int MaxCommentsPerAddressInWindow = 3;
    public static readonly TimeSpan RateWindow = TimeSpan.FromHours(1);
    /// <summary>How far back an identical body counts as a repeat.</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromDays(30);

    // Absolute URLs, "www." hosts, and bare domains on the TLDs that advertising overwhelmingly uses.
    // A reader mentioning one vendor site passes; a pitch that names its landing page four times does not.
    private static readonly Regex _link = new(
        @"(?:https?://|www\.)[^\s<>""']+|\b[a-z0-9-]+(?:\.[a-z0-9-]+)*\.(?:com|net|org|io|co|info|biz|xyz|top|site|online|shop|store|club|link|ru|cn)\b(?:/[^\s<>""']*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Real HTML tags (opening or closing, with attributes) and the BBCode / Markdown link forms that
    // form-fillers use for backlinks. Written so that prose such as "a < b", "<3" or "x<y" passes:
    // the tag name must follow "<" immediately, be a known HTML element, and close with ">".
    private static readonly Regex _markup = new(
        @"</?(?:a|abbr|b|blockquote|br|div|em|font|h[1-6]|i|iframe|img|li|link|ol|p|script|span|strong|style|table|u|ul)(?:\s[^>]*)?>|\[/?(?:url|link|a)\b|\]\(https?://",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _whitespace = new(@"\s+", RegexOptions.Compiled);

    public static CommentSpamVerdict Evaluate(CommentSubmission s)
    {
        // 1. Honeypot — the field is invisible and unlabeled; only software fills it.
        if (!string.IsNullOrWhiteSpace(s.HoneypotValue))
            return Reject("honeypot field filled");

        // 2. Timing token — proves the form was fetched, and how long ago.
        switch (s.TokenState)
        {
            case CommentFormTokenState.Missing:
                return Reject("posted without fetching the form");
            case CommentFormTokenState.Invalid:
                return Retry("form token did not verify");
        }
        if (s.FormAge is null)
            return Retry("form token carried no timestamp");
        if (s.FormAge.Value < TimeSpan.FromSeconds(MinSecondsToWrite))
            return Reject($"submitted {s.FormAge.Value.TotalSeconds:0.#}s after the form was rendered");
        if (s.FormAge.Value > TimeSpan.FromHours(MaxFormAgeHours))
            return Retry("form older than 24 hours");

        // 3. Author name — a name is not a place to put a URL or a tag.
        var name = s.AuthorName ?? "";
        if (name.Length > MaxAuthorNameLength)
            return Reject("author name too long");
        if (_link.IsMatch(name) || name.Contains('<'))
            return Reject("author name contains a link or markup");

        // 4. Body — links, markup, length.
        var content = s.Content ?? "";
        if (content.Length > MaxContentLength)
            return Reject("comment longer than the limit");
        if (_markup.IsMatch(content))
            return Reject("comment contains HTML or BBCode markup");

        var links = CountLinks(content);
        if (links > MaxLinks)
            return Reject($"comment contains {links} links");
        if (links > 0 && IsLinkOnly(content))
            return Reject("comment is a bare link");

        // 5. Repetition — the same pitch under three names is the signature of a mailer.
        if (s.IsDuplicateOfRecentComment)
            return Reject("identical comment already submitted recently");

        // 6. Burst — several comments from one address in an hour.
        if (s.RecentCommentsFromSameAddress >= MaxCommentsPerAddressInWindow)
            return Reject("too many comments from this address");

        return CommentSpamVerdict.Accept;
    }

    /// <summary>Number of link-like tokens in the text.</summary>
    public static int CountLinks(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : _link.Matches(text).Count;

    /// <summary>True when the text contains HTML tags, BBCode links, or Markdown links.</summary>
    public static bool ContainsMarkup(string? text) =>
        !string.IsNullOrEmpty(text) && _markup.IsMatch(text);

    /// <summary>The comparison key for repeat detection: trimmed, single-spaced, case-folded.</summary>
    public static string NormalizeForDuplicate(string? content) =>
        string.IsNullOrWhiteSpace(content) ? "" : _whitespace.Replace(content.Trim(), " ").ToLowerInvariant();

    // Fewer than ten letters once every link is removed: "check this out https://…" and nothing else.
    private static bool IsLinkOnly(string content)
    {
        var withoutLinks = _link.Replace(content, "");
        var letters = 0;
        foreach (var ch in withoutLinks)
            if (char.IsLetter(ch)) letters++;
        return letters < 10;
    }

    private static CommentSpamVerdict Reject(string reason) => new(CommentSpamAction.RejectSilently, reason);
    private static CommentSpamVerdict Retry(string reason) => new(CommentSpamAction.AskToRetry, reason);
}
