using System.Net;

namespace Blog.Core.Services;

/// <summary>
/// The email that tells the people who moderate a site that a reader has commented. Pure: this type
/// decides who is written to, how many notices an hour may bring, and what the message says; the web
/// project sends it. Everything a reader typed is HTML-encoded, and the reader's email address and
/// network address are left out of the message (they are in the Desk for those allowed to see them).
/// </summary>
public static class CommentNotice
{
    /// <summary>Most notices sent for one site in an hour. Past it, comments still arrive in the Desk; only the email is skipped.</summary>
    public const int MaxPerHour = 20;

    /// <summary>Most recipients one notice goes to.</summary>
    public const int MaxRecipients = 10;

    private const int MaxQuoted = 1500;

    public sealed record Message(string Subject, string Html, string Text);

    /// <summary>
    /// The addresses to write to: the list from Settings (commas, semicolons or new lines), or, when
    /// that is empty, the post's author. Duplicates are dropped and the list is capped.
    /// </summary>
    public static IReadOnlyList<string> Recipients(string? configured, string? postAuthorEmail)
    {
        var listed = (configured ?? "")
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(LooksLikeAddress).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRecipients).ToList();
        if (listed.Count > 0) return listed;
        var author = (postAuthorEmail ?? "").Trim();
        return LooksLikeAddress(author) ? new[] { author } : Array.Empty<string>();
    }

    /// <summary>A plain shape check, enough to keep a typo or a header-injection attempt out of the recipient list.</summary>
    public static bool LooksLikeAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254) return false;
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1) return false;
        if (value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '<' or '>' or ',' or ';' or '"')) return false;
        return value.IndexOf('.', at) > at + 1 && !value.EndsWith('.');
    }

    /// <summary>
    /// True when another notice may be sent now, given when earlier ones went out. The caller keeps
    /// <paramref name="sentAt"/> (this drops entries older than an hour and adds the new one).
    /// </summary>
    public static bool TryTake(List<DateTime> sentAt, DateTime nowUtc)
    {
        sentAt.RemoveAll(t => nowUtc - t >= TimeSpan.FromHours(1));
        if (sentAt.Count >= MaxPerHour) return false;
        sentAt.Add(nowUtc);
        return true;
    }

    /// <summary>The notice. <paramref name="held"/> says whether the comment waits for approval or is already on the page.</summary>
    public static Message Build(string? siteName, string? postTitle, string postUrl, string moderationUrl,
        string? commenterName, string? comment, bool held, bool isReply)
    {
        var site = OneLine(siteName, "Your site");
        var title = OneLine(postTitle, "a post");
        var who = OneLine(commenterName, "A reader");
        var body = (comment ?? "").Trim();
        if (body.Length > MaxQuoted) body = body[..MaxQuoted].TrimEnd() + " …";

        var kind = isReply ? "reply" : "comment";
        var subject = held ? $"[{site}] A {kind} is waiting for approval: {title}" : $"[{site}] New {kind} on: {title}";
        if (subject.Length > 200) subject = subject[..199] + "…";

        var state = held
            ? "It is not on the site yet. Approve or delete it under Comments."
            : "It is already on the site. You can delete it under Comments.";

        static string e(string? s) => WebUtility.HtmlEncode(s ?? "");
        var html =
            "<div style=\"font-family:system-ui,-apple-system,'Segoe UI',sans-serif;font-size:15px;line-height:1.55;color:#16140F\">" +
            $"<p><strong>{e(who)}</strong> left a {kind} on <a href=\"{e(postUrl)}\">{e(title)}</a>.</p>" +
            $"<blockquote style=\"margin:0 0 16px;padding:8px 14px;border-left:3px solid #D9CFBE;white-space:pre-wrap\">{e(body)}</blockquote>" +
            $"<p>{e(state)}</p>" +
            $"<p><a href=\"{e(moderationUrl)}\">Open Comments in the Desk</a></p>" +
            $"<p style=\"color:#6B5B45;font-size:12px\">You receive this because comment emails are switched on in Settings &rarr; Comments on {e(site)}. " +
            $"At most {MaxPerHour} are sent in an hour.</p></div>";

        var text =
            $"{who} left a {kind} on \"{title}\".\n{postUrl}\n\n{body}\n\n{state}\n{moderationUrl}\n\n" +
            $"You receive this because comment emails are switched on in Settings, Comments on {site}. At most {MaxPerHour} are sent in an hour.\n";

        return new Message(subject, html, text);
    }

    private static string OneLine(string? value, string fallback)
    {
        var text = new string((value ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        if (text.Length > 120) text = text[..119] + "…";
        return text.Length == 0 ? fallback : text;
    }
}
