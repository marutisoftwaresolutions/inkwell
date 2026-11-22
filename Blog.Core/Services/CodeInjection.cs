using System.Text.Json;
using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// Site code: HTML an Admin adds to every reader page (a tag-manager or analytics snippet, a
/// verification tag, a chat widget) and extra CSS. It is the operator's own code, emitted as written,
/// so the rules around it are what matter: only an Admin may change it, every change is audited with
/// the text before and after, a host can switch the feature off, and while the cookie-consent banner
/// is on the HTML is held back until a reader accepts. This type holds the pure parts: cleaning what
/// is stored, deciding whether it waits for consent, and packing held code so that nothing in it can
/// run early.
/// </summary>
public static class CodeInjection
{
    /// <summary>Longest text stored in each box.</summary>
    public const int MaxLength = 20_000;

    /// <summary>The text as stored: no NUL characters, trimmed, capped.</summary>
    public static string Clean(string? code)
    {
        var text = (code ?? "").Replace("\0", "").Trim();
        return text.Length > MaxLength ? text[..MaxLength] : text;
    }

    /// <summary>True when the text is longer than a box may hold (so the screen can say so instead of cutting it silently).</summary>
    public static bool TooLong(string? code) => (code ?? "").Trim().Length > MaxLength;

    /// <summary>
    /// CSS that cannot leave its style element: a "&lt;" is written as its CSS escape, so "&lt;/style&gt;"
    /// inside a rule stays text. Valid CSS has no bare "&lt;" outside a string, where the escape means the same.
    /// </summary>
    public static string SafeCss(string? css) => Clean(css).Replace("<", "\\3C ");

    public static bool HasHtml(UserSettings s) =>
        !string.IsNullOrWhiteSpace(s.CodeInjectionHead) || !string.IsNullOrWhiteSpace(s.CodeInjectionFoot);

    /// <summary>
    /// True when the head and foot HTML wait for the reader's consent: the banner is on, the Admin
    /// has not marked the code as needing none, and there is some.
    /// </summary>
    public static bool WaitsForConsent(UserSettings s) =>
        s.CookieConsentEnabled && s.CodeInjectionNeedsConsent && HasHtml(s);

    /// <summary>
    /// Held code as the content of a JSON data block: a string the browser never parses as HTML. The
    /// encoder writes "&lt;", "&gt;" and "&amp;" as \u escapes, so "&lt;/script&gt;" in the code cannot close the block.
    /// </summary>
    public static string HeldJson(string? head, string? foot) =>
        JsonSerializer.Serialize(new { head = Clean(head), foot = Clean(foot) });

    /// <summary>The three stored texts, for the audit entry's before and after.</summary>
    public static string AuditSnapshot(UserSettings s) => JsonSerializer.Serialize(new
    {
        head = s.CodeInjectionHead ?? "",
        foot = s.CodeInjectionFoot ?? "",
        css = s.CustomCss ?? "",
        waitsForConsent = s.CodeInjectionNeedsConsent,
    });
}
