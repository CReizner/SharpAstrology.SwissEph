// Bounded LRU cache for raw Chebyshev coefficient blocks loaded from
// JPL .eph files. Allocation-free hit-path; closure-free
// `GetOrAdd<TState>` overload for the steady-state read loop. Shares
// the same design idea as the SwissEph SegmentCache but keyed per JPL
// record rather than per packed-Chebyshev .se1 segment.
// Original license: see LICENSE.SwissEph.txt.

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SharpAstrology.SwissEphemerides.Infrastructure.Jpl;

/// <summary>
/// Bounded LRU cache keyed by <see cref="JplSegmentKey"/>. Concurrent-safe;
/// the hit path is allocation-free. Uses a clock / approximate-LRU scheme
/// over a <see cref="ConcurrentDictionary{TKey,TValue}"/>: each access bumps
/// a generation counter on the entry, and on overflow the lowest-generation
/// entries are evicted in batches under a coarse lock.
/// </summary>
internal sealed class JplSegmentCache
{
    private readonly int _capacity;
    private readonly ConcurrentDictionary<JplSegmentKey, Entry> _entries;
    private readonly object _evictLock = new();
    private long _accessCounter;
    private long _hits;
    private long _misses;

    /// <summary>Hit counter. Used by tests / benchmarks.</summary>
    public long Hits { get; private set; }
    /// <summary>Miss counter. Used by tests / benchmarks.</summary>
    public long Misses { get; private set; }

    public JplSegmentCache(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _entries = new ConcurrentDictionary<JplSegmentKey, Entry>();
    }

    /// <summary>Number of segments currently in the cache.</summary>
    public int Count => _entries.Count;

    /// <summary>Maximum number of segments held before eviction.</summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Returns the cached coefficient block for <paramref name="key"/>,
    /// computing it via <paramref name="loader"/> on miss.
    /// </summary>
    internal JplCachedSegment GetOrAdd(JplSegmentKey key, Func<JplSegmentKey, JplCachedSegment> loader)
    {
        if (loader is null) throw new ArgumentNullException(nameof(loader));
        if (_entries.TryGetValue(key, out var existing))
        {
            existing.Generation = Interlocked.Increment(ref _accessCounter);
            Interlocked.Increment(ref _hits);
            Hits = Interlocked.Read(ref _hits);
            return existing.Segment;
        }

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
        added.Generation = Interlocked.Increment(ref _accessCounter);
        Interlocked.Increment(ref _hits);
        Hits = Interlocked.Read(ref _hits);
        return added.Segment;
    }

    /// <summary>
    /// Closure-free <c>GetOrAdd</c> overload — caller passes a struct-typed
    /// state into a static loader so the hot path doesn't allocate a closure.
    /// </summary>
    internal JplCachedSegment GetOrAdd<TState>(JplSegmentKey key, TState state, Func<JplSegmentKey, TState, JplCachedSegment> loader)
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

    private void EvictExcess()
    {
        if (!Monitor.TryEnter(_evictLock)) return;
        try
        {
            var excess = _entries.Count - _capacity;
            if (excess <= 0) return;
            var snapshot = _entries.ToArray();
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

    private sealed class Entry
    {
        public required JplCachedSegment Segment { get; init; }
        public long Generation;
    }
}

/// <summary>
/// Identifies one record block in one JPL file. Equality is by reference
/// identity for the reader (a process-unique key) and value-equality for the
/// segment index.
/// </summary>
internal readonly record struct JplSegmentKey(JplFileReader File, int SegmentIndex)
{
    public bool Equals(JplSegmentKey other)
        => ReferenceEquals(File, other.File) && SegmentIndex == other.SegmentIndex;

    public override int GetHashCode()
        => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(File), SegmentIndex);
}

/// <summary>
/// One raw record loaded from disk: <see cref="JplHeader.DoublesPerRecord"/>
/// doubles, the first two of which are the segment's start/end JD. Stored
/// big-endian-corrected (i.e. host-native byte order).
/// </summary>
internal sealed class JplCachedSegment
{
    /// <summary>The full record buffer (length = <see cref="JplHeader.DoublesPerRecord"/>).</summary>
    public required double[] Buffer { get; init; }
    /// <summary>JD at which this segment starts (= Buffer[0]).</summary>
    public required double JdSegmentStart { get; init; }
    /// <summary>JD at which this segment ends (= Buffer[1] = JdSegmentStart + SegmentLengthDays).</summary>
    public required double JdSegmentEnd { get; init; }
}
