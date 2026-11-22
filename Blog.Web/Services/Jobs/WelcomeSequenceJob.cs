using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;

namespace Blog.Web.Services.Jobs;

/// <summary>
/// Sends the welcome sequence (Admin → Newsletter → Welcome emails). Every quarter of an hour it
/// looks, step by step, for confirmed subscribers who are due the next email, sends a bounded number
/// and records how far each reader has got. <see cref="WelcomeSequence"/> decides who is due; this
/// class only fetches, sends and records. Each copy carries the reader's own unsubscribe link and the
/// one-click headers, like a newsletter. A hard bounce removes the address from the list; a temporary
/// failure is tried again on the next run, and dropped once a step is a week overdue. Self-hosted
/// only: cloud mode has no newsletter. With no sequence switched on, or no outgoing email, it does nothing.
/// </summary>
public sealed class WelcomeSequenceJob : IScheduledJob
{
    public const int BatchPerStep = 25;
    public static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(30);

    private readonly IMemberRepository _members;
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<WelcomeSequenceJob> _log;

    public WelcomeSequenceJob(IMemberRepository members, ISettingRepository settings, IUserRepository users, IEmailService email,
        IConfiguration config, ILogger<WelcomeSequenceJob> log)
    {
        _members = members;
        _settings = settings;
        _users = users;
        _email = email;
        _config = config;
        _log = log;
    }

    public string Name => "welcome-sequence";
    public TimeSpan Interval => TimeSpan.FromMinutes(15);

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        if (string.Equals(_config["DeploymentMode"], "Cloud", StringComparison.OrdinalIgnoreCase)) return "Not used in cloud mode.";

        var owner = (await _users.GetFirstAdminAsync())?.Id ?? Guid.Empty;
        var site = await _settings.GetSettingsAsync(owner);
        var steps = WelcomeSequence.Normalize(site.WelcomeEmails);
        if (!WelcomeSequence.AnyEnabled(steps) || site.WelcomeSequenceStartedAt is null) return "No welcome email is switched on.";
        if (!_email.IsConfigured) return "Outgoing email is not set up; welcome emails are waiting.";
        if (string.IsNullOrWhiteSpace(site.WelcomeSiteUrl)) return "The site address is not known yet; save the welcome emails once from the Desk.";

        var started = DateTime.UtcNow;
        int sent = 0, passed = 0, failed = 0, bounced = 0;

        for (var stepsSent = 0; stepsSent < WelcomeSequence.MaxSteps; stepsSent++)
        {
            if (WelcomeSequence.NextStep(steps, stepsSent) is not { } stepNumber) break;
            var step = steps[stepNumber - 1];
            var now = DateTime.UtcNow;
            // Due on or before now; anything due longer ago than the give-up window is fetched too, so it can be passed over.
            var candidates = await _members.GetWelcomeCandidatesAsync(stepsSent, site.WelcomeSequenceStartedAt.Value, now.AddDays(-step.DelayDays), BatchPerStep);

            (string Html, string Text)? rendered = null;
            foreach (var member in candidates)
            {
                if (cancellationToken.IsCancellationRequested || DateTime.UtcNow - started > TimeBudget) break;
                if (member.ConfirmedAt is not { } confirmedAt) continue;

                switch (WelcomeSequence.Decide(step, confirmedAt, site.WelcomeSequenceStartedAt, DateTime.UtcNow))
                {
                    case WelcomeSequence.Decision.TooLate:
                        await _members.SetWelcomeStepsSentAsync(member.Id, stepNumber);
                        passed++;
                        continue;
                    case WelcomeSequence.Decision.Send:
                        break;
                    default:
                        continue;
                }

                rendered ??= NewsletterEmail.Render(new NewsletterContent(
                    string.IsNullOrWhiteSpace(site.SiteName) ? "Newsletter" : site.SiteName, site.WelcomeSiteUrl, step.Subject, null, step.BodyMarkdown,
                    Array.Empty<NewsletterPost>(), string.IsNullOrWhiteSpace(site.SiteLanguage) ? "en" : site.SiteLanguage));
                var unsubscribe = NewsletterEmail.UnsubscribeUrl(site.WelcomeSiteUrl, member.UnsubscribeToken);

                var result = await _email.SendMessageAsync(new EmailMessage(member.Email, member.Name, step.Subject,
                    NewsletterEmail.Personalize(rendered.Value.Html, unsubscribe, html: true),
                    NewsletterEmail.Personalize(rendered.Value.Text, unsubscribe, html: false),
                    new Dictionary<string, string>
                    {
                        ["List-Unsubscribe"] = $"<{unsubscribe}>",
                        ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
                    }), cancellationToken);

                if (result.Sent)
                {
                    await _members.SetWelcomeStepsSentAsync(member.Id, stepNumber);
                    sent++;
                }
                else if (result.Permanent)
                {
                    await _members.MarkBouncedAsync(member.Id);
                    bounced++;
                }
                else
                {
                    failed++;   // left as it is: tried again next run, passed over once a week overdue
                    _log.LogInformation("Welcome email {Step} not sent this run: {Reason}", stepNumber, result.Error);
                }
            }
            if (DateTime.UtcNow - started > TimeBudget) break;
        }

        return $"Welcome sequence: sent {sent}, will retry {failed}, bounced and removed {bounced}, passed over as too late {passed}.";
    }
}
