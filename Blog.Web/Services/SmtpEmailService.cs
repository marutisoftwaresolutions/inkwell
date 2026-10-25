using Blog.Core.Interfaces;
using System.Net;
using System.Net.Mail;

namespace Blog.Web.Services;

public class SmtpEmailService : IEmailService
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _settings = config.GetSection("Smtp").Get<SmtpSettings>() ?? new SmtpSettings();
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.Host);

    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_settings.Host))
        {
            _logger.LogWarning("SMTP not configured — skipping email to {Email}", toEmail);
            return;
        }

        try
        {
            using var client = new SmtpClient(_settings.Host, _settings.Port)
            {
                EnableSsl = _settings.EnableSsl,
                Credentials = new NetworkCredential(_settings.Username, _settings.Password)
            };

            var from = new MailAddress(_settings.FromEmail, _settings.FromName);
            var to = new MailAddress(toEmail, toName);
            using var message = new MailMessage(from, to)
            {
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            await client.SendMailAsync(message);
            _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
        }
    }

    public async Task<EmailSendResult> SendMessageAsync(EmailMessage m, CancellationToken ct = default)
    {
        if (!IsConfigured) return EmailSendResult.Failed("Outgoing email is not set up (Smtp:Host is empty).");
        try
        {
            using var client = new SmtpClient(_settings.Host, _settings.Port)
            {
                EnableSsl = _settings.EnableSsl,
                Credentials = new NetworkCredential(_settings.Username, _settings.Password)
            };
            using var message = new MailMessage(new MailAddress(_settings.FromEmail, _settings.FromName),
                new MailAddress(m.ToEmail, m.ToName ?? string.Empty))
            {
                Subject = m.Subject,
                SubjectEncoding = System.Text.Encoding.UTF8,
                HeadersEncoding = System.Text.Encoding.UTF8,
            };
            // Plain text first, HTML last: mail clients show the last alternative they can render.
            if (!string.IsNullOrEmpty(m.Text))
                message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(m.Text, System.Text.Encoding.UTF8, "text/plain"));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(m.Html, System.Text.Encoding.UTF8, "text/html"));
            foreach (var (name, value) in m.Headers ?? new Dictionary<string, string>())
            {
                // System.Net.Mail writes Reply-To from ReplyToList and drops a raw header of that name.
                if (string.Equals(name, "Reply-To", StringComparison.OrdinalIgnoreCase))
                {
                    try { message.ReplyToList.Add(new MailAddress(value)); } catch (FormatException) { /* not an address: send without it */ }
                    continue;
                }
                message.Headers.Add(name, value);
            }

            await client.SendMailAsync(message, ct);
            return EmailSendResult.Ok;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email to {Email} not sent", m.ToEmail);
            // The reason, without the stack: it is shown to the operator in the send log.
            var reason = ex is SmtpException se ? $"{se.StatusCode}: {se.Message}" : ex.Message;
            return EmailSendResult.Failed(reason.Length > 480 ? reason[..480] : reason);
        }
    }
}

public class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
}
