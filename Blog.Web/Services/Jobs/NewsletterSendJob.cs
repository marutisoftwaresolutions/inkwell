using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;

namespace Blog.Web.Services.Jobs;

/// <summary>
/// Sends queued newsletters (ADR-0028). A queued issue is first expanded to one delivery per confirmed
/// subscriber; then each run sends up to <c>Newsletter:BatchSize</c> copies (default 50, within a 40-second
/// budget) so a large list goes out over several minutes at a pace mail providers accept, and one slow SMTP
/// server cannot hold the single scheduler hostage. Each copy carries the recipient's own unsubscribe link and
/// RFC 8058 one-click headers. A failure is retried after 5 and 30 minutes, then recorded as failed. Someone
/// who unsubscribed after the issue was queued is skipped. When nothing is pending the issue is marked sent
/// and audited on behalf of the Admin who queued it. Without outgoing email configured, nothing is attempted.
/// </summary>
public sealed class NewsletterSendJob : IScheduledJob
{
    public const int DefaultBatchSize = 50;
    public const int MaxAttempts = 3;
    public static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(40);

    private readonly INewsletterRepository _repo;
    private readonly IEmailService _email;
    private readonly AuditService _audit;
    private readonly IConfiguration _config;
    private readonly ILogger<NewsletterSendJob> _log;

    public NewsletterSendJob(INewsletterRepository repo, IEmailService email, AuditService audit, IConfiguration config, ILogger<NewsletterSendJob> log)
    {
        _repo = repo;
        _email = email;
        _audit = audit;
        _config = config;
        _log = log;
    }

    public string Name => "newsletter";
    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public int BatchSize => Math.Clamp(_config.GetValue("Newsletter:BatchSize", DefaultBatchSize), 1, 500);

    /// <summary>Retry spacing by attempts already made: 5 minutes, then 30.</summary>
    public static TimeSpan RetryDelay(int attempts) => attempts <= 1 ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(30);

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var active = await _repo.GetActiveAsync();
        if (active.Count == 0) return "No newsletter queued.";
        if (!_email.IsConfigured) return $"Outgoing email is not set up; {active.Count} newsletter(s) waiting.";

        var started = DateTime.UtcNow;
        var budget = BatchSize;
        int sent = 0, retried = 0, failed = 0, skipped = 0, finished = 0;

        foreach (var issue in active)
        {
            if (issue.Status == NewsletterStatus.Queued)
            {
                var recipients = await _repo.ExpandRecipientsAsync(issue.Id, DateTime.UtcNow);
                _log.LogInformation("Newsletter {Id} queued for {Count} recipient(s).", issue.Id, recipients);
            }

            while (budget > 0 && DateTime.UtcNow - started < TimeBudget && !cancellationToken.IsCancellationRequested)
            {
                var due = await _repo.GetDueAsync(issue.Id, DateTime.UtcNow, Math.Min(budget, 25));
                if (due.Count == 0) break;
                foreach (var d in due)
                {
                    if (budget <= 0 || DateTime.UtcNow - started >= TimeBudget) break;
                    budget--;
                    if (d.MemberStatus != "confirmed" || string.IsNullOrEmpty(d.UnsubscribeToken))
                    {
                        await _repo.MarkDeliveryAsync(d.Id, NewsletterDeliveryStatus.Skipped, d.Attempts, DateTime.UtcNow, null, "No longer subscribed.");
                        skipped++;
                        continue;
                    }
                    var siteUrl = issue.SiteBaseUrl ?? "";
                    var unsubscribe = NewsletterEmail.UnsubscribeUrl(siteUrl, d.UnsubscribeToken);
                    var result = await _email.SendMessageAsync(new EmailMessage(d.Email, d.MemberName, issue.Subject,
                        NewsletterEmail.Personalize(issue.RenderedHtml ?? "", unsubscribe, html: true),
                        NewsletterEmail.Personalize(issue.RenderedText ?? "", unsubscribe, html: false),
                        new Dictionary<string, string>
                        {
                            ["List-Unsubscribe"] = $"<{unsubscribe}>",
                            ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
                        }), cancellationToken);

                    var attempts = d.Attempts + 1;
                    if (result.Sent)
                    {
                        await _repo.MarkDeliveryAsync(d.Id, NewsletterDeliveryStatus.Sent, attempts, DateTime.UtcNow, DateTime.UtcNow, null);
                        sent++;
                    }
                    else if (attempts >= MaxAttempts)
                    {
                        await _repo.MarkDeliveryAsync(d.Id, NewsletterDeliveryStatus.Failed, attempts, DateTime.UtcNow, null, result.Error);
                        failed++;
                    }
                    else
                    {
                        await _repo.MarkDeliveryAsync(d.Id, NewsletterDeliveryStatus.Pending, attempts, DateTime.UtcNow + RetryDelay(attempts), null, result.Error);
                        retried++;
                    }
                }
            }

            var done = await _repo.FinishIfDoneAsync(issue.Id, DateTime.UtcNow);
            if (done is not null)
            {
                finished++;
                await _audit.LogAsync(AuditActions.NewsletterSent, "Newsletter", done.Id.ToString(), done.Subject,
                    newValues: $"{done.SentCount} sent, {done.FailedCount} failed, of {done.RecipientCount} recipient(s)",
                    userIdOverride: done.QueuedBy, userNameOverride: done.QueuedByName);
            }
            if (budget <= 0 || DateTime.UtcNow - started >= TimeBudget) break;
        }
        return $"Newsletter: sent {sent}, will retry {retried}, failed {failed}, skipped {skipped}; {finished} issue(s) finished.";
    }
}
