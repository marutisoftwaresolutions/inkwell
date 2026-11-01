namespace Blog.Core.Domain;

/// <summary>
/// The persisted record of one scheduled job: when it last started and finished, whether it
/// succeeded, and what it reported. One row per job name — the scheduler upserts it after every run,
/// so an operator can see from Admin → Dashboard that the nightly work is actually happening.
/// Since ADR-0040 the row also carries the operator's controls: a pause flag (the scheduler never
/// claims a paused job) and an optional interval override in minutes that replaces the cadence the
/// job declares in code.
/// </summary>
public class JobRun
{
    public string Name { get; set; } = string.Empty;
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastFinishedAt { get; set; }
    public bool LastSucceeded { get; set; }
    public string? LastMessage { get; set; }
    public int RunCount { get; set; }
    public bool Paused { get; set; }
    public int? IntervalOverrideMinutes { get; set; }
}
