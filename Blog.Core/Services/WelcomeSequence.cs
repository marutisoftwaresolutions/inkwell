using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// The welcome sequence: up to three emails sent automatically some days after a reader confirms their
/// subscription. This class holds the rules, so the job that sends is a thin shell. The rules are:
/// steps go out in order and each at most once; a step that is switched off is passed over; a reader
/// who confirmed before the sequence was switched on never receives it (an upgrade must not mail an
/// existing list); and a step that could not be sent within <see cref="GiveUpAfterDays"/> of its due
/// date is dropped, not sent weeks late. Pure.
/// </summary>
public static class WelcomeSequence
{
    public const int MaxSteps = 3;
    public const int MaxDelayDays = 60;
    public const int MaxSubjectLength = 150;
    public const int MaxBodyLength = 10000;
    public const int GiveUpAfterDays = 7;

    /// <summary>Exactly <see cref="MaxSteps"/> entries, trimmed and clamped, so the editor and the job always see the same shape.</summary>
    public static List<WelcomeEmail> Normalize(IEnumerable<WelcomeEmail>? emails)
    {
        var list = (emails ?? []).Take(MaxSteps).Select(e => new WelcomeEmail
        {
            Enabled = e.Enabled,
            DelayDays = Math.Clamp(e.DelayDays, 0, MaxDelayDays),
            Subject = (e.Subject ?? "").Trim(),
            BodyMarkdown = (e.BodyMarkdown ?? "").Trim(),
        }).ToList();
        while (list.Count < MaxSteps) list.Add(new WelcomeEmail { DelayDays = (list.Count + 1) * 3 });
        return list;
    }

    /// <summary>What stops this set from being saved, keyed by "step:field"; empty when it is fine.</summary>
    public static Dictionary<string, string> Validate(IReadOnlyList<WelcomeEmail> emails)
    {
        var errors = new Dictionary<string, string>();
        var lastDelay = -1;
        for (var i = 0; i < emails.Count; i++)
        {
            var e = emails[i];
            if (!e.Enabled) continue;
            if (e.Subject.Length == 0) errors[$"{i}:subject"] = "Give this email a subject.";
            else if (e.Subject.Length > MaxSubjectLength) errors[$"{i}:subject"] = $"Keep the subject under {MaxSubjectLength} characters.";
            if (e.BodyMarkdown.Length == 0) errors[$"{i}:body"] = "Write the email, or switch it off.";
            else if (e.BodyMarkdown.Length > MaxBodyLength) errors[$"{i}:body"] = $"Keep the email under {MaxBodyLength:N0} characters.";
            if (e.DelayDays <= lastDelay) errors[$"{i}:delay"] = "Send this one later than the email before it.";
            lastDelay = e.DelayDays;
        }
        return errors;
    }

    public static bool AnyEnabled(IReadOnlyList<WelcomeEmail>? emails) => emails is not null && emails.Any(e => e.Enabled);

    /// <summary>The next step (1-based) a reader who has been sent <paramref name="stepsSent"/> should get, or null when there is none.</summary>
    public static int? NextStep(IReadOnlyList<WelcomeEmail> emails, int stepsSent)
    {
        for (var i = Math.Max(0, stepsSent); i < Math.Min(emails.Count, MaxSteps); i++)
            if (emails[i].Enabled) return i + 1;
        return null;
    }

    public enum Decision { NotYet, Send, TooLate, NotEligible }

    /// <summary>
    /// What to do about one step for one reader now. Not eligible when they confirmed before the sequence
    /// began; not yet until the step's day arrives; too late once it is more than a week overdue.
    /// </summary>
    public static Decision Decide(WelcomeEmail step, DateTime confirmedAtUtc, DateTime? sequenceStartedAtUtc, DateTime nowUtc)
    {
        if (sequenceStartedAtUtc is null || confirmedAtUtc < sequenceStartedAtUtc.Value) return Decision.NotEligible;
        var due = confirmedAtUtc.AddDays(step.DelayDays);
        if (nowUtc < due) return Decision.NotYet;
        return nowUtc > due.AddDays(GiveUpAfterDays) ? Decision.TooLate : Decision.Send;
    }
}
