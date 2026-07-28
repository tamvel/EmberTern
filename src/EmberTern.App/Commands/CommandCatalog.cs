using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Input;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Commands;

/// <summary>
/// The ONE declaration of EmberTern's user-facing commands and their gestures. Built once, at type
/// initialisation, from the literal table below — no reflection, no scanning, and nothing to recompute
/// when a menu opens or a key is pressed.
///
/// <para>⭐ <b>Why this holds descriptions and never <see cref="System.Windows.Input.ICommand"/> instances.</b>
/// The command that "Go" must invoke belongs to whichever workspace tab is selected right now, and the
/// commands of the Object Explorer belong to whichever node is selected — <c>MetadataNodeViewModel</c>
/// declares fifteen of them and is instantiated per tree node. None of those objects exist when this table
/// is built, so a gesture → <c>ICommand</c> map is not expressible. The catalog therefore describes; the
/// router resolves the instance at invoke time through the live view models. This is also what keeps
/// <see cref="Avalonia.Input.KeyGesture"/> out of the view models: they answer questions about a
/// <see cref="CommandId"/>, the view layer owns the gestures.</para>
///
/// <para>The catalog is <b>curated, not exhaustive</b> — see <see cref="CommandId"/>.</para>
///
/// <para><b>Deliberately undeclared gestures.</b> <c>Escape</c> is a universal dismiss implemented by every
/// popup, dialog and filter box, and <c>Ctrl+V</c> in Data Import means "re-read the clipboard source",
/// i.e. paste semantics that must yield to a focused text box. Declaring either would invent collisions
/// that do not exist and take a keystroke away from the control that should have it.</para>
/// </summary>
public static class CommandCatalog
{
    private static KeyGesture G(Key key, KeyModifiers modifiers = KeyModifiers.None) => new(key, modifiers);

    private const KeyModifiers Ctrl = KeyModifiers.Control;
    private const KeyModifiers Shift = KeyModifiers.Shift;
    private const KeyModifiers Alt = KeyModifiers.Alt;

    private static readonly WorkspaceTabKind[] QueryTab = [WorkspaceTabKind.Query];
    private static readonly WorkspaceTabKind[] DebuggerTab = [WorkspaceTabKind.Debugger];
    private static readonly WorkspaceTabKind[] ImportTab = [WorkspaceTabKind.DataImport];

    private static readonly CommandDescriptor[] AllDescriptors =
    [
        // ── Tab scope ────────────────────────────────────────────────────────────────────────────────
        new(CommandId.Go, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.F5), TabKinds:
            [
                WorkspaceTabKind.Query,
                WorkspaceTabKind.Debugger,
                WorkspaceTabKind.ScriptExecutor,
                WorkspaceTabKind.DataImport,
            ]),
        new(CommandId.ExecuteQuery, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.Enter, Ctrl), TabKinds: QueryTab),
        new(CommandId.ExecuteQueryFull, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.F5, Shift), TabKinds: QueryTab),
        // Etap 2 keeps Alt+F and the SQL-editor-only reach of the window binding it replaces, byte for
        // byte. Etap 3 moves the gesture to Ctrl+K (the user has retired Alt+letter) and extends the
        // command to the five object editors, deleting their five local Alt+F handlers with it.
        new(CommandId.FormatSql, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.F, Alt), TabKinds: QueryTab),
        new(CommandId.ImportValidate, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.F5, Ctrl), TabKinds: ImportTab),
        new(CommandId.ImportRefresh, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.R, Ctrl), TabKinds: ImportTab),
        new(CommandId.ImportBrowse, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.O, Ctrl), TabKinds: ImportTab),

        // The debugger's stepping surface keeps its own dispatch: several of these are VIEW actions that
        // need the source editor's caret (Run To Cursor, Toggle Breakpoint), not view-model commands.
        // Only F5 moved to the router — it was the one gesture with two competing owners.
        new(CommandId.DebuggerStepOver, CommandScope.Tab, CommandDispatch.Reserved,
            G(Key.F10), TabKinds: DebuggerTab),
        new(CommandId.DebuggerRunToCursor, CommandScope.Tab, CommandDispatch.Reserved,
            G(Key.F10, Ctrl), TabKinds: DebuggerTab),
        new(CommandId.DebuggerStepInto, CommandScope.Tab, CommandDispatch.Reserved,
            G(Key.F11), TabKinds: DebuggerTab),
        new(CommandId.DebuggerStepOut, CommandScope.Tab, CommandDispatch.Reserved,
            G(Key.F11, Shift), TabKinds: DebuggerTab),
        new(CommandId.DebuggerStop, CommandScope.Tab, CommandDispatch.Reserved,
            G(Key.F5, Shift), TabKinds: DebuggerTab),
        new(CommandId.DebuggerRestart, CommandScope.Tab, CommandDispatch.Reserved,
            G(Key.F5, Ctrl | Shift), TabKinds: DebuggerTab),
        new(CommandId.DebuggerToggleBreakpoint, CommandScope.Tab, CommandDispatch.Reserved,
            G(Key.F9), TabKinds: DebuggerTab),
        new(CommandId.DebuggerEvaluateSelection, CommandScope.Tab, CommandDispatch.Reserved,
            G(Key.F9, Shift), TabKinds: DebuggerTab),
        new(CommandId.DebuggerSaveSource, CommandScope.Tab, CommandDispatch.Reserved,
            G(Key.S, Ctrl), TabKinds: DebuggerTab),

        // ── Editor scope ─────────────────────────────────────────────────────────────────────────────
        new(CommandId.EditorFind, CommandScope.Editor, CommandDispatch.Routed, G(Key.F, Ctrl)),
        new(CommandId.EditorReplace, CommandScope.Editor, CommandDispatch.Routed, G(Key.H, Ctrl)),

        // Typing mechanics + navigation the editor controllers own on the tunnel (#224 / #228).
        new(CommandId.EditorCompletion, CommandScope.Editor, CommandDispatch.Reserved, G(Key.Space, Ctrl)),
        new(CommandId.EditorParameterHelper, CommandScope.Editor, CommandDispatch.Reserved,
            G(Key.Space, Ctrl | Shift)),
        new(CommandId.EditorRename, CommandScope.Editor, CommandDispatch.Reserved, G(Key.F2)),
        new(CommandId.EditorPeekDefinition, CommandScope.Editor, CommandDispatch.Reserved, G(Key.F12, Alt)),
        new(CommandId.EditorQuickFix, CommandScope.Editor, CommandDispatch.Reserved, G(Key.OemPeriod, Ctrl)),
        new(CommandId.EditorExpandConstruct, CommandScope.Editor, CommandDispatch.Reserved, G(Key.Tab)),
        new(CommandId.EditorNextDiagnostic, CommandScope.Editor, CommandDispatch.Reserved, G(Key.F8)),
        new(CommandId.EditorPreviousDiagnostic, CommandScope.Editor, CommandDispatch.Reserved,
            G(Key.F8, Shift)),

        // ── Global scope ─────────────────────────────────────────────────────────────────────────────
        new(CommandId.GlobalSearch, CommandScope.Global, CommandDispatch.Routed, G(Key.F, Ctrl | Shift)),
        // Ctrl+F twice over is not a collision: Editor outranks Global, so the caret decides. That used to
        // be a hand-written focus probe in MainWindow's key handler; now it is the resolution order.
        new(CommandId.FocusSidebarFilter, CommandScope.Global, CommandDispatch.Routed, G(Key.F, Ctrl)),
    ];

    private static readonly Dictionary<CommandId, CommandDescriptor> ById =
        AllDescriptors.ToDictionary(d => d.Id);

    /// <summary>Every declared command.</summary>
    public static IReadOnlyList<CommandDescriptor> All => AllDescriptors;

    /// <summary>The descriptor for <paramref name="id"/>, or null when the id is not declared.</summary>
    public static CommandDescriptor? For(CommandId id) => ById.GetValueOrDefault(id);

    /// <summary>
    /// Every command claiming this key stroke, <b>most specific scope first</b> — the order the router
    /// must try them in. Within one scope the order is the table's, which only matters for Tab-scoped
    /// commands whose tab kinds are disjoint (so at most one can resolve anyway).
    /// </summary>
    public static IReadOnlyList<CommandDescriptor> Match(Key key, KeyModifiers modifiers)
        => AllDescriptors
            .Where(d => d.Matches(key, modifiers))
            .OrderByDescending(d => (int)d.Scope)
            .ToArray();

    /// <summary>
    /// Every gesture claimed by two commands that could both be live at once — empty in a healthy catalog.
    /// Two commands may share a gesture across different scopes (the resolution order decides), and two
    /// Tab-scoped commands may share one when no single tab kind offers both. Anything else is ambiguous,
    /// and which command ran would depend on table order.
    /// <para>Returns human-readable lines so a failing test names the clash instead of just counting it.</para>
    /// </summary>
    public static IReadOnlyList<string> Collisions()
    {
        var clashes = new List<string>();

        foreach (var group in AllDescriptors
                     .Where(d => d.Gesture is not null || d.AlternateGesture is not null)
                     .SelectMany(Gestures)
                     .GroupBy(x => (x.Descriptor.Scope, x.Gesture))
                     .Where(g => g.Count() > 1))
        {
            var members = group.Select(x => x.Descriptor).ToArray();
            for (int i = 0; i < members.Length; i++)
            {
                for (int j = i + 1; j < members.Length; j++)
                {
                    if (SharesATabKind(members[i], members[j]))
                    {
                        clashes.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} on {1} is claimed by both {2} and {3}",
                            group.Key.Gesture, group.Key.Scope, members[i].Id, members[j].Id));
                    }
                }
            }
        }

        return clashes;
    }

    private static IEnumerable<(CommandDescriptor Descriptor, KeyGesture Gesture)> Gestures(CommandDescriptor d)
    {
        if (d.Gesture is not null) yield return (d, d.Gesture);
        if (d.AlternateGesture is not null) yield return (d, d.AlternateGesture);
    }

    // Outside Tab scope there is no kind to separate two commands, so sharing a gesture is always a clash.
    private static bool SharesATabKind(CommandDescriptor a, CommandDescriptor b)
    {
        if (a.Scope != CommandScope.Tab || b.Scope != CommandScope.Tab) return true;
        if (a.TabKinds is null || b.TabKinds is null) return true;
        return a.TabKinds.Intersect(b.TabKinds).Any();
    }
}
