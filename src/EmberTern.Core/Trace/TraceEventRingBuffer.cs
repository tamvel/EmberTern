using System;
using System.Collections.Generic;

namespace EmberTern.Core.Trace;

/// <summary>
/// A bounded, thread-safe FIFO buffer of trace events — the memory-control heart of a
/// long-running trace session. On a busy ERP database events arrive faster than a human
/// reads them, so the buffer has a hard capacity: when full, the OLDEST event is dropped
/// to make room and <see cref="DroppedCount"/> is incremented (surfaced in the UI so the
/// loss is honest, never silent). This bounds memory regardless of session length and
/// never blocks the producing trace thread (no backpressure into the server).
/// <para>
/// Concrete on <see cref="TraceEvent"/> by design — it is the reusable *pattern* for a
/// future Diagnostics Center (Sessions/Locks/Transactions monitors), not a shared generic
/// framework (that abstraction waits for its second consumer).
/// </para>
/// </summary>
public sealed class TraceEventRingBuffer
{
    private readonly object _gate = new();
    private readonly Queue<TraceEvent> _items;

    public TraceEventRingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Ring buffer capacity must be positive.");
        Capacity = capacity;
        _items = new Queue<TraceEvent>(capacity);
    }

    public int Capacity { get; }

    /// <summary>Number of events currently held (0..<see cref="Capacity"/>).</summary>
    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    /// <summary>Total events dropped (oldest-first) because the buffer was full when they arrived.</summary>
    public long DroppedCount { get; private set; }

    /// <summary>Appends an event, evicting the oldest if at capacity (incrementing
    /// <see cref="DroppedCount"/>). Returns true if nothing had to be dropped.</summary>
    public bool Add(TraceEvent e)
    {
        lock (_gate)
        {
            bool dropped = false;
            if (_items.Count >= Capacity)
            {
                _items.Dequeue();
                DroppedCount++;
                dropped = true;
            }
            _items.Enqueue(e);
            return !dropped;
        }
    }

    /// <summary>An ordered (oldest → newest) snapshot copy — safe to hand to the UI thread.</summary>
    public IReadOnlyList<TraceEvent> Snapshot()
    {
        lock (_gate) return _items.ToArray();
    }

    /// <summary>Empties the buffer. Does NOT reset <see cref="DroppedCount"/> unless
    /// <paramref name="resetDropped"/> is set (Clear vs. a fresh session).</summary>
    public void Clear(bool resetDropped = false)
    {
        lock (_gate)
        {
            _items.Clear();
            if (resetDropped) DroppedCount = 0;
        }
    }
}
