namespace Blog.Core.Services;

/// <summary>
/// Tells a refusal that will never succeed from one worth retrying, from the status code the mail
/// server gave while the message was being handed over. A permanent refusal (the mailbox does not
/// exist, the address is not allowed) is a hard bounce: sending again only teaches mail providers that
/// this sender does not clean its list, so the address stops receiving newsletters at once. Anything
/// else (mailbox busy or full, greylisting, a connection problem) is retried as before.
/// This sees only what the server says during the conversation. A bounce that arrives later as an
/// email to the sender address is not seen by Inkwell. Pure.
/// </summary>
public static class SmtpBounce
{
    /// <summary>Member status for an address a mail server has permanently refused.</summary>
    public const string BouncedStatus = "bounced";

    /// <summary>SMTP reply codes that mean "this recipient will never accept mail here".</summary>
    private static readonly HashSet<int> PermanentRecipientCodes = [550, 551, 553, 556];

    public static bool IsPermanent(int smtpStatusCode) => PermanentRecipientCodes.Contains(smtpStatusCode);

    /// <summary>
    /// True when the text of a refusal says the mailbox itself is the problem. Used only together with a
    /// 5xx code that is otherwise ambiguous (554), because servers word policy blocks the same way.
    /// </summary>
    public static bool LooksLikeUnknownMailbox(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        string[] signs = ["user unknown", "no such user", "unknown user", "mailbox not found", "mailbox unavailable", "does not exist", "recipient address rejected", "5.1.1", "5.1.10"];
        return signs.Any(s => message.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsPermanent(int smtpStatusCode, string? message) =>
        IsPermanent(smtpStatusCode) || (smtpStatusCode == 554 && LooksLikeUnknownMailbox(message));
}
