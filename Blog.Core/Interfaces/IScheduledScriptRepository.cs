using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

/// <summary>Persistence for Admin → Scheduled scripts. Install-wide (no owner): database housekeeping, like backups.</summary>
public interface IScheduledScriptRepository
{
    Task<IReadOnlyList<ScheduledScript>> GetAllAsync();
    Task<ScheduledScript?> GetByIdAsync(Guid id);
    Task<ScheduledScript?> GetByNameAsync(string name);
    /// <summary>Enabled rows whose NextRunAt is at or before <paramref name="nowUtc"/>, oldest due first.</summary>
    Task<IReadOnlyList<ScheduledScript>> GetDueAsync(DateTime nowUtc);
    Task UpsertAsync(ScheduledScript script);
    Task<bool> DeleteAsync(Guid id);
    /// <summary>Record one run: last run time, outcome, captured output (truncated by the caller), duration, and the next due time.</summary>
    Task RecordRunAsync(Guid id, DateTime ranAtUtc, bool succeeded, string output, int durationMs, DateTime nextRunAtUtc);
}
