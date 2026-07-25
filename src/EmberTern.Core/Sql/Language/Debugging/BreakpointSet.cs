using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// The active breakpoints, keyed by a step point's source <b>offset</b> (an
/// <see cref="IExecutableStatement.Start"/>). Each entry is a <see cref="Breakpoint"/> stop-policy object
/// (D12) — a plain <see cref="Add"/> creates an unconditional, always-breaking one (the pre-D12 behaviour),
/// and <see cref="GetOrAdd"/> hands back the entry so a condition / hit-count policy can be set on it. A run
/// command (<see cref="StepKind.Continue"/> / <see cref="StepKind.Out"/> / <see cref="StepKind.RunToCursor"/>)
/// asks <see cref="DebugSession"/> whether the next step point's breakpoint should stop (condition + hit
/// count); a <see cref="StepKind.Into"/>/<see cref="StepKind.Over"/> landing on one reports
/// <see cref="StopReason.Breakpoint"/> too. Mutable during a session. Pure Core — no server, no UI.
/// </summary>
public sealed class BreakpointSet
{
    private readonly Dictionary<int, Breakpoint> _breakpoints = new();

    /// <summary>Adds a plain (unconditional, always-breaking) breakpoint at <paramref name="offset"/>; returns
    /// false when one is already set there (leaving the existing policy untouched).</summary>
    public bool Add(int offset)
    {
        if (_breakpoints.ContainsKey(offset))
        {
            return false;
        }
        _breakpoints[offset] = new Breakpoint(offset);
        return true;
    }

    /// <summary>Removes the breakpoint at <paramref name="offset"/>; returns false when none was set.</summary>
    public bool Remove(int offset) => _breakpoints.Remove(offset);

    /// <summary>Toggles the breakpoint at <paramref name="offset"/>: removes it when set (returns false), else
    /// adds a plain one (returns true). Toggling off then on again discards any condition / hit-count policy —
    /// the caller keeps that config if it wants to preserve it across a toggle.</summary>
    public bool Toggle(int offset)
    {
        if (_breakpoints.Remove(offset))
        {
            return false;
        }
        _breakpoints[offset] = new Breakpoint(offset);
        return true;
    }

    /// <summary>True when a breakpoint is set at <paramref name="offset"/>.</summary>
    public bool Contains(int offset) => _breakpoints.ContainsKey(offset);

    /// <summary>The breakpoint set at <paramref name="offset"/>, or null when none is set — for reading its
    /// policy (condition / hit count / hit tally) at the stop decision.</summary>
    public Breakpoint? Get(int offset) => _breakpoints.TryGetValue(offset, out var bp) ? bp : null;

    /// <summary>Returns the breakpoint at <paramref name="offset"/>, creating a plain one if none is set — the
    /// entry point for setting a condition or hit-count policy on a breakpoint.</summary>
    public Breakpoint GetOrAdd(int offset)
    {
        if (!_breakpoints.TryGetValue(offset, out var bp))
        {
            _breakpoints[offset] = bp = new Breakpoint(offset);
        }
        return bp;
    }

    /// <summary>Removes every breakpoint.</summary>
    public void Clear() => _breakpoints.Clear();

    /// <summary>Resets every breakpoint's hit tally (each <see cref="Breakpoint.ResetHits"/>) — called when a
    /// session (re)starts so hit-count policies count from scratch, while the set itself (and its policies)
    /// persists across launch/restart.</summary>
    public void ResetHitCounts()
    {
        foreach (var bp in _breakpoints.Values) bp.ResetHits();
    }

    /// <summary>The offsets with a breakpoint set (unordered).</summary>
    public IReadOnlyCollection<int> Offsets => _breakpoints.Keys;

    /// <summary>How many breakpoints are set.</summary>
    public int Count => _breakpoints.Count;
}
