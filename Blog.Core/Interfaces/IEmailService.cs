namespace Blog.Core.Interfaces;

public interface IEmailService
{
    /// <summary>
    /// True when outbound mail can actually be sent. Features that depend on email (password reset)
    /// check this so they can say "not available on this site" instead of silently doing nothing.
    /// </summary>
    bool IsConfigured { get; }

    Task SendAsync(string toEmail, string toName, string subject, string htmlBody);
}
