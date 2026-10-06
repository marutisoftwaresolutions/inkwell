namespace Blog.Core.Interfaces;

public interface IEmailService
{
    /// <summary>
    /// True when outbound mail can actually be sent. Features that depend on email (password reset)
    /// check this so they can say "not available on this site" instead of silently doing nothing.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>Fire-and-forget for transactional mail: failures are logged, never thrown.</summary>
    Task SendAsync(string toEmail, string toName, string subject, string htmlBody);

    /// <summary>
    /// Sends one message and reports the outcome, for senders that retry (the newsletter job, ADR-0028):
    /// an HTML part, an optional plain-text part and extra headers such as List-Unsubscribe.
    /// </summary>
    Task<EmailSendResult> SendMessageAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>One outgoing email.</summary>
public sealed record EmailMessage(string ToEmail, string? ToName, string Subject, string Html, string? Text = null,
    IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>What happened to a send: <see cref="Sent"/>, or the reason it was not.</summary>
public sealed record EmailSendResult(bool Sent, string? Error)
{
    public static readonly EmailSendResult Ok = new(true, null);
    public static EmailSendResult Failed(string error) => new(false, error);
}
