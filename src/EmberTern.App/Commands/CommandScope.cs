namespace EmberTern.App.Commands;

/// <summary>
/// Where a command lives, and therefore how specific its claim on a gesture is.
///
/// <para>⚠ <b>The numeric values are the resolution order and are load-bearing</b>: the router tries
/// candidates from the HIGHEST value down, so a more specific scope wins. Renumbering these changes
/// which command a gesture invokes.</para>
///
/// <para>A scope is only added when a command actually needs it (<c>Tree</c> and <c>Grid</c> arrive with
/// the tree/grid commands in etap 3) — an unreachable scope reads like working behaviour and is not.</para>
/// </summary>
public enum CommandScope
{
    /// <summary>Available whenever the window is. The fallback, always tried last.</summary>
    Global = 0,

    /// <summary>Belongs to the selected workspace tab — resolved through
    /// <c>WorkspaceTabViewModel.ResolveCommand</c>, which returns null for a tab kind that has no such
    /// command. That null is what stops a gesture leaking into a tab it means nothing on.</summary>
    Tab = 1,

    /// <summary>Live only while the keyboard focus is inside an AvaloniaEdit <c>TextEditor</c>.</summary>
    Editor = 2,
}
