namespace Blog.Core.Services;

/// <summary>
/// The one decision the scheduler makes, kept pure so it can be tested without a clock or a host:
/// is a job due, given when it last started?
/// </summary>
public static class JobSchedule
{
    /// <summary>
    /// A job is due when it has never run, or when at least <paramref name="interval"/> has passed
    /// since it last <em>started</em>. Measuring from the start rather than the finish keeps a slow
    /// job on its cadence instead of drifting later with every run.
    /// </summary>
    public static bool IsDue(DateTime? lastStartedUtc, TimeSpan interval, DateTime nowUtc)
    {
        if (lastStartedUtc is null) return true;
        return lastStartedUtc.Value <= DueBefore(nowUtc, interval);
    }

    /// <summary>
    /// The latest <c>LastStartedAt</c> that still counts as due at <paramref name="nowUtc"/> — the
    /// threshold the ledger claim compares against in SQL, so the database and <see cref="IsDue"/>
    /// apply one rule. A non-positive interval means always due, expressed as "started no later than now".
    /// </summary>
    public static DateTime DueBefore(DateTime nowUtc, TimeSpan interval) =>
        interval <= TimeSpan.Zero ? nowUtc : nowUtc - interval;
}
