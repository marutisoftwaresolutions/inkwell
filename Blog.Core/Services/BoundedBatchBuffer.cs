using System.Collections.Concurrent;

namespace Blog.Core.Services;

/// <summary>
/// A thread-safe, capacity-capped queue that request threads add to and a single flusher drains in
/// batches. It exists so per-request analytics (page views, crawler visits) cost a memory write
/// instead of a database round trip: under a flood the database sees one batch per flush interval,
/// never one insert per request. When the buffer is full new items are dropped and counted, so a
/// flood can never grow memory without bound — losing analytics rows is acceptable, losing the
/// site is not.
/// </summary>
public sealed class BoundedBatchBuffer<T>
{
    private readonly ConcurrentQueue<T> _items = new();
    private int _count;
    private long _dropped;

    public BoundedBatchBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public int Capacity { get; }

    /// <summary>Items currently waiting to be flushed.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Adds an item; returns false (and counts a drop) when the buffer is full.</summary>
    public bool TryAdd(T item)
    {
        if (Interlocked.Increment(ref _count) > Capacity)
        {
            Interlocked.Decrement(ref _count);
            Interlocked.Increment(ref _dropped);
            return false;
        }
        _items.Enqueue(item);
        return true;
    }

    /// <summary>Removes up to <paramref name="max"/> items in arrival order.</summary>
    public IReadOnlyList<T> Drain(int max)
    {
        var batch = new List<T>(Math.Min(Math.Max(max, 0), Count));
        while (batch.Count < max && _items.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _count);
            batch.Add(item);
        }
        return batch;
    }

    /// <summary>Returns the number of items dropped since the last call and resets it.</summary>
    public long TakeDropped() => Interlocked.Exchange(ref _dropped, 0);

    /// <summary>
    /// A flush is due when the batch is large enough to be worth a round trip, or when anything has
    /// waited at least <paramref name="interval"/> since the last flush.
    /// </summary>
    public static bool IsFlushDue(int pending, int threshold, DateTime lastFlushUtc, DateTime nowUtc, TimeSpan interval) =>
        pending > 0 && (pending >= threshold || nowUtc - lastFlushUtc >= interval);
}
