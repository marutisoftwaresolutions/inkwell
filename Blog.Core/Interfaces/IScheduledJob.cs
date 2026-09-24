namespace Blog.Core.Interfaces;

/// <summary>
/// A unit of background work the in-process scheduler runs on an interval. Implementations are
/// registered as scoped services; the scheduler resolves them inside a fresh scope per run, so a job
/// can take repositories in its constructor exactly like a controller does.
///
/// Rules every job obeys:
///   • idempotent — the host can recycle mid-run and the job will be started again;
///   • self-contained failure — throw freely; the scheduler records the failure and moves on;
///   • no request context — nothing about "the current tenant" exists here.
/// </summary>
public interface IScheduledJob
{
    /// <summary>Stable identifier persisted in the Jobs table and shown to the operator.</summary>
    string Name { get; }

    /// <summary>How long after the previous start the job becomes due again.</summary>
    TimeSpan Interval { get; }

    /// <summary>Do the work. Return a one-line summary for the operator (rows pruned, items processed).</summary>
    Task<string> RunAsync(CancellationToken cancellationToken);
}
