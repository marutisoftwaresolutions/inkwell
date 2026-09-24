namespace Blog.Core.Domain;

/// <summary>
/// The persisted record of one scheduled job: when it last started and finished, whether it
/// succeeded, and what it reported. One row per job name — the scheduler upserts it after every run,
/// so an operator can see from Admin → Dashboard that the nightly work is actually happening.
/// </summary>
public class JobRun
{
    public string Name { get; set; } = string.Empty;
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastFinishedAt { get; set; }
    public bool LastSucceeded { get; set; }
    public string? LastMessage { get; set; }
    public int RunCount { get; set; }
}
