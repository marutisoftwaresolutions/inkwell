using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IJobRunRepository
{
    Task<JobRun?> GetAsync(string name);
    Task<IReadOnlyList<JobRun>> GetAllAsync();

    /// <summary>
    /// Atomically claim the next run of <paramref name="name"/>: advance <c>LastStartedAt</c> to
    /// <paramref name="nowUtc"/> only if the job is due (never started, or started at least
    /// <paramref name="interval"/> ago). Exactly one of any number of concurrent callers — two IIS
    /// instances, a web garden, an overlapping admin tick — gets <c>true</c>; the rest get
    /// <c>false</c> and must not run. A missing row is created first, so the claim works on a fresh
    /// install. Throws when the ledger is unreachable; the caller decides whether to run blind (it should not).
    /// </summary>
    Task<bool> TryClaimAsync(string name, DateTime nowUtc, TimeSpan interval);

    /// <summary>Upsert the row for <paramref name="name"/> with the outcome of the run that just finished.</summary>
    Task RecordAsync(string name, DateTime startedAtUtc, DateTime finishedAtUtc, bool succeeded, string? message);
}
