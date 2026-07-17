using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// The set of active breakpoints, keyed by a step point's source <b>offset</b> (an
/// <see cref="IExecutableStatement.Start"/>). A run command (<see cref="StepKind.Continue"/> /
/// <see cref="StepKind.Out"/> / <see cref="StepKind.RunToCursor"/>) stops when the next step point's offset
/// is in this set; a <see cref="StepKind.Into"/>/<see cref="StepKind.Over"/> that happens to land on one
/// reports <see cref="StopReason.Breakpoint"/> too. Mutable during a session — a breakpoint can be added or
/// removed while paused. Conditional breakpoints, hit counts and break-on-exception are D12 (they compose
/// with this set; they are not modelled here). Pure Core — no server, no UI.
/// </summary>
public sealed class BreakpointSet
{
    private readonly HashSet<int> _offsets = new();

    /// <summary>Adds a breakpoint at <paramref name="offset"/>; returns false when one was already set.</summary>
    public bool Add(int offset) => _offsets.Add(offset);

    /// <summary>Removes the breakpoint at <paramref name="offset"/>; returns false when none was set.</summary>
    public bool Remove(int offset) => _offsets.Remove(offset);

    /// <summary>Toggles the breakpoint at <paramref name="offset"/>; returns true when it is now set,
    /// false when it was cleared.</summary>
    public bool Toggle(int offset)
    {
        if (_offsets.Remove(offset)) return false;
        _offsets.Add(offset);
        return true;
    }

    /// <summary>True when a breakpoint is set at <paramref name="offset"/>.</summary>
    public bool Contains(int offset) => _offsets.Contains(offset);

    /// <summary>Removes every breakpoint.</summary>
    public void Clear() => _offsets.Clear();

    /// <summary>The offsets with a breakpoint set (unordered).</summary>
    public IReadOnlyCollection<int> Offsets => _offsets;

    /// <summary>How many breakpoints are set.</summary>
    public int Count => _offsets.Count;
}
