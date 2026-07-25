using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// The active data breakpoints — <see cref="DataBreakpoint"/> stop-policy objects keyed by variable name
/// (D12, spec §9.8.4). It owns the <b>local</b> change detection so <see cref="DebugSession"/> does not
/// scatter it: <see cref="Snapshot"/> captures the watched values in a frame's scope <i>before</i> a step,
/// and <see cref="FindChanged"/> compares that against the frame <i>after</i> the step and returns the first
/// watched variable that changed. Names fold case-insensitively (Firebird folds unquoted identifiers) and
/// resolve through the frame's scope chain (<see cref="Frame.TryResolveValue"/>), so a captured outer /
/// closure variable is watchable (D9). Pure Core — no server, no UI.
/// </summary>
public sealed class DataBreakpointSet
{
    private readonly Dictionary<string, DataBreakpoint> _watches = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds a data breakpoint watching <paramref name="variable"/>; returns false when one already
    /// watches it.</summary>
    public bool Add(string variable)
    {
        if (_watches.ContainsKey(variable))
        {
            return false;
        }
        _watches[variable] = new DataBreakpoint(variable);
        return true;
    }

    /// <summary>Removes the data breakpoint on <paramref name="variable"/>; returns false when none was set.</summary>
    public bool Remove(string variable) => _watches.Remove(variable);

    /// <summary>Toggles the data breakpoint on <paramref name="variable"/>: removes it when set (returns
    /// false), else adds one (returns true).</summary>
    public bool Toggle(string variable)
    {
        if (_watches.Remove(variable))
        {
            return false;
        }
        _watches[variable] = new DataBreakpoint(variable);
        return true;
    }

    /// <summary>True when <paramref name="variable"/> is watched.</summary>
    public bool Contains(string variable) => _watches.ContainsKey(variable);

    /// <summary>Removes every data breakpoint.</summary>
    public void Clear() => _watches.Clear();

    /// <summary>The watched variable names (unordered).</summary>
    public IReadOnlyCollection<string> Variables => _watches.Keys;

    /// <summary>How many variables are watched.</summary>
    public int Count => _watches.Count;

    /// <summary>Captures each watched variable's current value as resolved in <paramref name="frame"/>'s scope
    /// — the "before" side of a change check, snapshotted before a step. A variable not in scope reads null.</summary>
    public IReadOnlyDictionary<string, object?> Snapshot(Frame frame)
    {
        var snap = new Dictionary<string, object?>(_watches.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var name in _watches.Keys)
        {
            snap[name] = frame.TryResolveValue(name, out var v) ? v : null;
        }
        return snap;
    }

    /// <summary>Given a prior <see cref="Snapshot"/> and <paramref name="frame"/> now, returns the first
    /// watched variable whose value changed (per its <see cref="DataBreakpoint.ShouldBreak"/>), or null when
    /// none changed. The whole snapshot → diff → decision lives here; the caller only pairs a before-snapshot
    /// with this after-check.</summary>
    public DataBreakpoint? FindChanged(IReadOnlyDictionary<string, object?> before, Frame frame)
    {
        foreach (var (name, bp) in _watches)
        {
            object? now = frame.TryResolveValue(name, out var v) ? v : null;
            before.TryGetValue(name, out var was);
            if (bp.ShouldBreak(was, now))
            {
                return bp;
            }
        }
        return null;
    }
}
