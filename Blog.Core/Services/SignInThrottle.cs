using System.Collections.Concurrent;

namespace Blog.Core.Services;

/// <summary>
/// Slows password guessing against one account. The IP firewall already blocks an address that keeps
/// failing; this covers the case it cannot see, guesses for one account spread over many addresses.
/// The numbers are fixed and published: after <see cref="MaxFailures"/> wrong passwords for an account
/// inside <see cref="Window"/>, further attempts for that account are refused for <see cref="Lockout"/>,
/// whatever the address and whether or not the password is right. A correct sign-in or a completed
/// password reset clears the count. Kept in memory: it protects against a burst, it is not a record.
/// Bounded, so a flood of invented emails cannot grow it without limit. Thread-safe; the clock is
/// passed in, so the rules are testable.
/// </summary>
public sealed class SignInThrottle
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);
    public const int MaxTracked = 20_000;

    private sealed class Entry
    {
        public readonly List<DateTime> Failures = new();
        public DateTime? LockedUntil;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public static string KeyFor(string? email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>How long until this account may try again, or null when it is not locked.</summary>
    public TimeSpan? LockedFor(string? email, DateTime nowUtc)
    {
        if (!_entries.TryGetValue(KeyFor(email), out var entry)) return null;
        lock (entry)
        {
            if (entry.LockedUntil is { } until && until > nowUtc) return until - nowUtc;
            return null;
        }
    }

    /// <summary>Counts a wrong password. Returns true when this failure locked the account.</summary>
    public bool RecordFailure(string? email, DateTime nowUtc)
    {
        var key = KeyFor(email);
        if (key.Length == 0) return false;
        if (_entries.Count >= MaxTracked && !_entries.ContainsKey(key)) Prune(nowUtc);
        if (_entries.Count >= MaxTracked && !_entries.ContainsKey(key)) return false;   // full of live entries: do not grow

        var entry = _entries.GetOrAdd(key, _ => new Entry());
        lock (entry)
        {
            entry.Failures.RemoveAll(t => nowUtc - t > Window);
            entry.Failures.Add(nowUtc);
            if (entry.Failures.Count < MaxFailures) return false;
            entry.LockedUntil = nowUtc + Lockout;
            entry.Failures.Clear();
            return true;
        }
    }

    /// <summary>A correct sign-in, or a password reset the owner completed: the count starts again.</summary>
    public void Clear(string? email) => _entries.TryRemove(KeyFor(email), out _);

    /// <summary>The wait, in whole minutes rounded up, to tell a person.</summary>
    public static int MinutesToWait(TimeSpan remaining) => Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));

    private void Prune(DateTime nowUtc)
    {
        foreach (var (key, entry) in _entries)
        {
            bool dead;
            lock (entry)
                dead = (entry.LockedUntil is null || entry.LockedUntil <= nowUtc) && entry.Failures.All(t => nowUtc - t > Window);
            if (dead) _entries.TryRemove(key, out _);
        }
    }
}
