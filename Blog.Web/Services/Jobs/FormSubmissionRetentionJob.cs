using Blog.Core.Interfaces;

namespace Blog.Web.Services.Jobs;

/// <summary>
/// Deletes contact-form submissions older than their form keeps them (ADR-0030; Forms.RetentionDays, default 365,
/// 0 = until deleted). Personal data should not outlive its purpose; this is what makes the setting true.
/// </summary>
public sealed class FormSubmissionRetentionJob : IScheduledJob
{
    private readonly IFormRepository _forms;
    public FormSubmissionRetentionJob(IFormRepository forms) => _forms = forms;

    public string Name => "form-submission-retention";
    public TimeSpan Interval => TimeSpan.FromHours(24);

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var removed = await _forms.DeleteExpiredAsync(DateTime.UtcNow);
        return removed == 1 ? "Deleted 1 expired form submission." : $"Deleted {removed} expired form submissions.";
    }
}
