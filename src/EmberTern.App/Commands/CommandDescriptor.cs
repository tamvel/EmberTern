using System.Collections.Generic;
using Avalonia.Input;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Commands;

/// <summary>
/// How a declared command is dispatched.
/// </summary>
public enum CommandDispatch
{
    /// <summary>
    /// <c>CommandRouter</c> resolves and invokes it. The registry is the only thing that binds the gesture.
    /// </summary>
    Routed,

    /// <summary>
    /// Declared for collision validation (and, later, for tooltips and menus) but dispatched by the control
    /// that owns it — the editor's typing mechanics and the debugger's stepping surface.
    /// <para>⚠ The router must never invoke a reserved command, and must never mark its gesture handled:
    /// the owner has already had, or will have, its chance at the keystroke.</para>
    /// </summary>
    Reserved,
}

/// <summary>
/// The immutable description of one user-facing command: what it is, where it lives, how it is dispatched
/// and which gesture(s) invoke it. Deliberately <b>not</b> a holder of an <see cref="System.Windows.Input.ICommand"/>
/// — see <see cref="CommandCatalog"/> for why that would not work here.
/// </summary>
/// <param name="Id">Stable identity; the only thing other layers refer to.</param>
/// <param name="Scope">Which context the command belongs to, and so how specific its gesture claim is.</param>
/// <param name="Dispatch">Whether the router invokes it or merely knows about it.</param>
/// <param name="Gesture">The primary gesture, or null for a command reachable only from a menu/toolbar.</param>
/// <param name="AlternateGesture">A second accepted gesture — a retained standard beside a new default.</param>
/// <param name="TabKinds">
/// For <see cref="CommandScope.Tab"/> only: the tab kinds on which this command exists. It is what makes
/// collision validation exact — two Tab-scoped commands may share a gesture precisely when no tab kind can
/// offer both (Shift+F5 is Execute-Full on a query tab and Stop on a debugger tab, and never both at once).
/// <para>⚠ It duplicates knowledge that <c>WorkspaceTabViewModel.ResolveCommand</c> also encodes, and that
/// is intentional: the switch is needed to reach the command instance, this is needed to validate the
/// gesture map without constructing every tab. They are pinned to agree by a test, so they cannot drift.</para>
/// </param>
public sealed record CommandDescriptor(
    CommandId Id,
    CommandScope Scope,
    CommandDispatch Dispatch,
    KeyGesture? Gesture,
    KeyGesture? AlternateGesture = null,
    IReadOnlyList<WorkspaceTabKind>? TabKinds = null)
{
    /// <summary>True when either declared gesture matches the pressed key and modifiers.</summary>
    public bool Matches(Key key, KeyModifiers modifiers)
        => Is(Gesture, key, modifiers) || Is(AlternateGesture, key, modifiers);

    private static bool Is(KeyGesture? gesture, Key key, KeyModifiers modifiers)
        => gesture is not null && gesture.Key == key && gesture.KeyModifiers == modifiers;
}
