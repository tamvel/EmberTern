namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// A data breakpoint as a <b>stop-policy object</b> (D12, spec §9.8.4) — "break when this variable changes",
/// modelled the same way an ordinary <see cref="Breakpoint"/> is a policy over a step point rather than a
/// bare entry in a set. It owns the change decision: <see cref="ShouldBreak"/> is the whole of it (did the
/// watched value change across a step?). Future data-breakpoint policies (break when it reaches a value,
/// break on a condition over the new value) evolve as further state + a richer <see cref="ShouldBreak"/> on
/// this one model, not as parallel machinery. Pure Core: it compares two opaque values; <see cref="Frame"/>
/// resolution + the before/after snapshotting live in <see cref="DataBreakpointSet"/>, and the loop hookup
/// in <see cref="DebugSession"/>.
/// </summary>
public sealed class DataBreakpoint
{
    /// <summary>Creates a data breakpoint watching <paramref name="variable"/> for any change.</summary>
    public DataBreakpoint(string variable) => Variable = variable;

    /// <summary>The watched variable's name (resolved through the frame's scope chain, so a captured outer /
    /// closure variable is watchable too — D9).</summary>
    public string Variable { get; }

    /// <summary>The stop decision: break when the watched variable's value <b>changed</b> across a step. The
    /// pure policy half — <see cref="DataBreakpointSet"/> resolves and supplies the before / after values.
    /// NULL and <see cref="System.DBNull"/> are equivalent (a variable's absence / SQL NULL is one state),
    /// matching the Variables panel's change-highlight comparison so a row and a data breakpoint agree.</summary>
    public bool ShouldBreak(object? oldValue, object? newValue) => !ValuesEqual(oldValue, newValue);

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is System.DBNull)
        {
            a = null;
        }
        if (b is System.DBNull)
        {
            b = null;
        }
        if (a is null || b is null)
        {
            return a is null && b is null;
        }
        return a.Equals(b);
    }
}
