// Bounded LRU cache for decompressed Chebyshev segments loaded from .se1 files.
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SharpAstrology.SwissEphemerides.Infrastructure.SwissEph;

/// <summary>
/// Bounded LRU cache keyed by <see cref="SegmentKey"/>. Concurrent-safe; its
/// eviction path is allocation-free per request (the value type is a cached
/// class instance reused for the lifetime of the segment). Uses a clock /
/// approximate-LRU scheme over a <see cref="ConcurrentDictionary{TKey,TValue}"/>:
/// each access bumps a generation counter on the entry, and on overflow the
/// lowest-generation entries are evicted in batches under a coarse lock.
/// </summary>
internal sealed class SegmentCache
{
    private readonly int _capacity;
    private readonly ConcurrentDictionary<SegmentKey, Entry> _entries;
    private readonly object _evictLock = new();
    private long _accessCounter;

    /// <summary>Hit counter. Used by tests to assert &gt; 90 % hit rate.</summary>
    public long Hits { get; private set; }
    /// <summary>Miss counter. Used by tests to assert &gt; 90 % hit rate.</summary>
    public long Misses { get; private set; }

    public SegmentCache(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _entries = new ConcurrentDictionary<SegmentKey, Entry>();
    }

    /// <summary>Number of segments currently in the cache.</summary>
    public int Count => _entries.Count;

    /// <summary>Maximum number of segments held before eviction.</summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Returns the cached segment for <paramref name="key"/>, computing it via
    /// <paramref name="loader"/> on miss. Callers may share one
    /// <see cref="SegmentCache"/> across threads. Access bumps the entry's
    /// generation, which approximates LRU recency.
    /// </summary>
    /// <remarks>
    /// The hit path is allocation-free; the miss path allocates one
    /// <see cref="Entry"/> wrapper plus whatever the loader allocates.
    /// </remarks>
    internal CachedSegment GetOrAdd(SegmentKey key, Func<SegmentKey, CachedSegment> loader)
    {
        if (loader is null) throw new ArgumentNullException(nameof(loader));
        if (_entries.TryGetValue(key, out var existing))
        {
            existing.Generation = Interlocked.Increment(ref _accessCounter);
            Interlocked.Increment(ref _hits);
            Hits = Interlocked.Read(ref _hits);
            return existing.Segment;
        }

        // Miss path: compute (potentially slow IO) and try to insert.
        var seg = loader(key);
        var newEntry = new Entry { Segment = seg, Generation = Interlocked.Increment(ref _accessCounter) };
        var added = _entries.GetOrAdd(key, newEntry);
        if (ReferenceEquals(added, newEntry))
        {
            Interlocked.Increment(ref _misses);
            Misses = Interlocked.Read(ref _misses);
            if (_entries.Count > _capacity) EvictExcess();
            return seg;
        }
        // Race: another thread inserted first; use that entry instead.
        added.Generation = Interlocked.Increment(ref _accessCounter);
        Interlocked.Increment(ref _hits);
        Hits = Interlocked.Read(ref _hits);
        return added.Segment;
    }

    /// <summary>
    /// Returns the cached segment for <paramref name="key"/>, computing it via
    /// <paramref name="loader"/> on miss with the supplied <paramref name="state"/>
    /// argument. This overload avoids the closure-allocation that the
    /// <see cref="Func{T,TResult}"/> overload incurs at every call site.
    /// </summary>
    internal CachedSegment GetOrAdd<TState>(SegmentKey key, TState state, Func<SegmentKey, TState, CachedSegment> loader)
    {
        if (loader is null) throw new ArgumentNullException(nameof(loader));
        if (_entries.TryGetValue(key, out var existing))
        {
            existing.Generation = Interlocked.Increment(ref _accessCounter);
            Interlocked.Increment(ref _hits);
            Hits = Interlocked.Read(ref _hits);
            return existing.Segment;
        }

        var seg = loader(key, state);
        var newEntry = new Entry { Segment = seg, Generation = Interlocked.Increment(ref _accessCounter) };
        var added = _entries.GetOrAdd(key, newEntry);
        if (ReferenceEquals(added, newEntry))
        {
            Interlocked.Increment(ref _misses);
            Misses = Interlocked.Read(ref _misses);
            if (_entries.Count > _capacity) EvictExcess();
            return seg;
        }
        added.Generation = Interlocked.Increment(ref _accessCounter);
        Interlocked.Increment(ref _hits);
        Hits = Interlocked.Read(ref _hits);
        return added.Segment;
    }

    /// <summary>Resets the hit/miss counters used by the benchmark suite.</summary>
    public void ResetStatistics()
    {
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
        Hits = 0;
        Misses = 0;
    }

    /// <summary>
    /// Drops the oldest entries (smallest generation) until the cache size is
    /// at most <see cref="_capacity"/>. Coarse-locked so concurrent evictions
    /// don't compound.
    /// </summary>
    private void EvictExcess()
    {
        if (!Monitor.TryEnter(_evictLock)) return;
        try
        {
            var excess = _entries.Count - _capacity;
            if (excess <= 0) return;

            // Snapshot keys + generations. Allocation here is acceptable —
            // eviction is on the cold path; the hot path (cache hit) is
            // allocation-free.
            var snapshot = _entries.ToArray();
            // Partial-sort by generation ascending; evict the oldest `excess`.
            Array.Sort(snapshot, static (a, b) => a.Value.Generation.CompareTo(b.Value.Generation));
            var toRemove = excess;
            for (var i = 0; i < snapshot.Length && toRemove > 0; i++)
            {
                if (_entries.TryRemove(snapshot[i].Key, out _)) toRemove--;
            }
        }
        finally
        {
            Monitor.Exit(_evictLock);
        }
    }

    private long _hits;
    private long _misses;

    private sealed class Entry
    {
        public required CachedSegment Segment { get; init; }
        public long Generation;
    }
}

/// <summary>
/// Identifies one segment in one file: file handle wrapper plus internal
/// body number plus segment index. Equality is by reference identity for the
/// reader (a process-unique key) and value-equality for the rest.
/// </summary>
internal readonly record struct SegmentKey(Se1FileReader File, int BodyId, int SegmentIndex)
{
    public bool Equals(SegmentKey other)
        => ReferenceEquals(File, other.File) && BodyId == other.BodyId && SegmentIndex == other.SegmentIndex;

    public override int GetHashCode()
        => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(File), BodyId, SegmentIndex);
}

/// <summary>
/// One decompressed Chebyshev segment in its post-rotation form
/// (already passed through <see cref="SegmentRotation.RotateInPlace"/>).
/// The <c>3 * ncoe</c> coefficients are stored densely:
/// X<sub>0..ncoe-1</sub>, Y<sub>0..ncoe-1</sub>, Z<sub>0..ncoe-1</sub>.
/// </summary>
internal sealed class CachedSegment
{
    public required double[] Coefficients { get; init; }
    public required double JdSegmentStart { get; init; }
    public required double JdSegmentEnd { get; init; }
    public required int CoefficientsPerCoordinate { get; init; }
    /// <summary>Number of leading coefficients to actually evaluate (mirrors <c>plan_data.neval</c>).</summary>
    public required int CoefficientsToEvaluate { get; init; }
}
