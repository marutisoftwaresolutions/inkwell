using Blog.Core.Interfaces;

namespace Blog.Web.Services.Jobs;

/// <summary>Removes password-reset tokens that expired more than a day ago. Used or not, they are dead rows.</summary>
public sealed class PasswordResetTokenPruneJob : IScheduledJob
{
    private readonly IPasswordResetRepository _tokens;
    public PasswordResetTokenPruneJob(IPasswordResetRepository tokens) => _tokens = tokens;

    public string Name => "password-reset-token-prune";
    public TimeSpan Interval => TimeSpan.FromHours(24);

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var removed = await _tokens.PruneExpiredAsync(DateTime.UtcNow.AddDays(-1));
        return $"Removed {removed} expired reset token(s).";
    }
}
