namespace EmberTern.App.ViewModels;

/// <summary>
/// One data breakpoint in the debugger's Breakpoints panel (Stage X / D12, spec §9.8.4) — "break when this
/// variable changes". A <b>pure immutable projection</b> of the Core <see cref="EmberTern.Core.Sql.Debugging.DataBreakpoint"/>:
/// it carries the watched name (the key back to the Core set for removal) and the variable's display label.
/// A data breakpoint has no editable policy (the change decision lives entirely in Core), so the row is a
/// read-only label — the panel presents it and offers removal, nothing more.
/// </summary>
public sealed class DataBreakpointRowViewModel
{
    public DataBreakpointRowViewModel(string watchedName, string displayName)
    {
        WatchedName = watchedName;
        DisplayName = displayName;
    }

    /// <summary>The name the Core <see cref="EmberTern.Core.Sql.Debugging.DataBreakpointSet"/> watches (a plain
    /// variable name, or a trigger context row's synthetic) — the key used to remove it.</summary>
    public string WatchedName { get; }

    /// <summary>The label shown to the user (the variable's display name, e.g. <c>ACC</c> or <c>NEW.STATUS</c>).</summary>
    public string DisplayName { get; }
}
