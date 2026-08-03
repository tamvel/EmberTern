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

    // Every tab kind that compiles an object. Compile auto-commits through the Ddl lane, and the DDL
    // change-safety gate stands between it and an overwrite, so F7 needs no confirmation of its own.
    private static readonly WorkspaceTabKind[] CompilableTabs =
    [
        WorkspaceTabKind.NewTable,
        WorkspaceTabKind.TableDetail,
        WorkspaceTabKind.ViewDetail,
        WorkspaceTabKind.ProcedureDetail,
        WorkspaceTabKind.TriggerDetail,
        WorkspaceTabKind.FunctionDetail,
        WorkspaceTabKind.GeneratorDetail,
        WorkspaceTabKind.DomainDetail,
        WorkspaceTabKind.PackageDetail,
        WorkspaceTabKind.ExceptionDetail,
        WorkspaceTabKind.IndexDetail,
    ];

    // The tab kinds with SQL to format: the console plus the five source-bearing object editors — exactly
    // the reach of the window binding and the five local Alt+F handlers this replaced, no wider.
    private static readonly WorkspaceTabKind[] FormattableTabs =
    [
        WorkspaceTabKind.Query,
        WorkspaceTabKind.ViewDetail,
        WorkspaceTabKind.ProcedureDetail,
        WorkspaceTabKind.TriggerDetail,
        WorkspaceTabKind.FunctionDetail,
        WorkspaceTabKind.PackageDetail,
    ];

    private static readonly CommandDescriptor[] AllDescriptors =
    [
        // ── Tab scope ────────────────────────────────────────────────────────────────────────────────
        new(CommandId.Go, UiStrings.CommandTitleGo, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.F5), TabKinds:
            [
                WorkspaceTabKind.Query,
                WorkspaceTabKind.Debugger,
                WorkspaceTabKind.ScriptExecutor,
                WorkspaceTabKind.DataImport,
            ]),
        new(CommandId.ExecuteQuery, UiStrings.CommandTitleExecuteQuery, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.Enter, Ctrl), TabKinds: QueryTab),
        new(CommandId.ExecuteQueryFull, UiStrings.CommandTitleExecuteQueryFull, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.F5, Shift), TabKinds: QueryTab),
        // ⭐⭐ Alt+F IS BACK, AND IT IS THE ONE RATIFIED EXCEPTION TO "no Alt+letter" (user decision,
        // 2026-08-03): Ctrl+K needs two hands for an action used constantly. Ctrl+K stays as the alternate, so
        // nobody's muscle memory breaks.
        //
        // ⚠ This comment used to read "Ctrl+K, not Alt+F: the user retired Alt+letter with no exceptions" —
        // true when written, and exactly the kind of confident stale note that teaches the next reader the
        // wrong rule (gotcha #284's shape, in prose).
        //
        // ⚠ The retirement itself is NOT withdrawn, and its reason is technical rather than stylistic: on the
        // Polish (Programmers) layout AltGr composes ą/ć/ę/ł/ń/ó/ś/ź/ż, so Alt+those letters are unusable.
        // F is not one of them. NoCommandUsesAltPlusALetter still guards every other letter and names this
        // single exception explicitly.
        new(CommandId.FormatSql, UiStrings.CommandTitleFormatSql, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.F, Alt), TabKinds: FormattableTabs, AlternateGesture: G(Key.K, Ctrl)),
        new(CommandId.Compile, UiStrings.CommandTitleCompile, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.F7), TabKinds: CompilableTabs),
        new(CommandId.ImportValidate, UiStrings.CommandTitleImportValidate, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.F5, Ctrl), TabKinds: ImportTab),
        new(CommandId.ImportRefresh, UiStrings.CommandTitleImportRefresh, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.R, Ctrl), TabKinds: ImportTab),
        new(CommandId.ImportBrowse, UiStrings.CommandTitleImportBrowse, CommandScope.Tab, CommandDispatch.Routed,
            G(Key.O, Ctrl), TabKinds: ImportTab),

        // The debugger's stepping surface keeps its own dispatch: several of these are VIEW actions that
        // need the source editor's caret (Run To Cursor, Toggle Breakpoint), not view-model commands.
        // Only F5 moved to the router — it was the one gesture with two competing owners.
        // ⚠ Reserved means "dispatched by the control that owns it", NOT "internal": these are exactly the
        // keys a user wants the Keyboard Shortcuts window to tell them about, so they carry titles too.
        new(CommandId.DebuggerStepOver, UiStrings.CommandTitleDebuggerStepOver, CommandScope.Tab,
            CommandDispatch.Reserved, G(Key.F10), TabKinds: DebuggerTab),
        new(CommandId.DebuggerRunToCursor, UiStrings.CommandTitleDebuggerRunToCursor, CommandScope.Tab,
            CommandDispatch.Reserved, G(Key.F10, Ctrl), TabKinds: DebuggerTab),
        new(CommandId.DebuggerStepInto, UiStrings.CommandTitleDebuggerStepInto, CommandScope.Tab,
            CommandDispatch.Reserved, G(Key.F11), TabKinds: DebuggerTab),
        new(CommandId.DebuggerStepOut, UiStrings.CommandTitleDebuggerStepOut, CommandScope.Tab,
            CommandDispatch.Reserved, G(Key.F11, Shift), TabKinds: DebuggerTab),
        new(CommandId.DebuggerStop, UiStrings.CommandTitleDebuggerStop, CommandScope.Tab,
            CommandDispatch.Reserved, G(Key.F5, Shift), TabKinds: DebuggerTab),
        new(CommandId.DebuggerRestart, UiStrings.CommandTitleDebuggerRestart, CommandScope.Tab,
            CommandDispatch.Reserved, G(Key.F5, Ctrl | Shift), TabKinds: DebuggerTab),
        new(CommandId.DebuggerToggleBreakpoint, UiStrings.CommandTitleDebuggerToggleBreakpoint, CommandScope.Tab,
            CommandDispatch.Reserved, G(Key.F9), TabKinds: DebuggerTab),
        new(CommandId.DebuggerEvaluateSelection, UiStrings.CommandTitleDebuggerEvaluateSelection, CommandScope.Tab,
            CommandDispatch.Reserved, G(Key.F9, Shift), TabKinds: DebuggerTab),
        new(CommandId.DebuggerSaveSource, UiStrings.CommandTitleDebuggerSaveSource, CommandScope.Tab,
            CommandDispatch.Reserved, G(Key.S, Ctrl), TabKinds: DebuggerTab),

        // ── Editor scope ─────────────────────────────────────────────────────────────────────────────
        new(CommandId.EditorFind, UiStrings.CommandTitleEditorFind, CommandScope.Editor, CommandDispatch.Routed,
            G(Key.F, Ctrl)),
        new(CommandId.EditorReplace, UiStrings.CommandTitleEditorReplace, CommandScope.Editor, CommandDispatch.Routed,
            G(Key.H, Ctrl)),

        // Typing mechanics + navigation the editor controllers own on the tunnel (#224 / #228).
        new(CommandId.EditorCompletion, UiStrings.CommandTitleEditorCompletion, CommandScope.Editor,
            CommandDispatch.Reserved, G(Key.Space, Ctrl)),
        new(CommandId.EditorParameterHelper, UiStrings.CommandTitleEditorParameterHelper, CommandScope.Editor,
            CommandDispatch.Reserved, G(Key.Space, Ctrl | Shift)),
        new(CommandId.EditorRename, UiStrings.CommandTitleEditorRename, CommandScope.Editor,
            CommandDispatch.Reserved, G(Key.F2)),
        new(CommandId.EditorPeekDefinition, UiStrings.CommandTitleEditorPeekDefinition, CommandScope.Editor,
            CommandDispatch.Reserved, G(Key.F12, Alt)),
        new(CommandId.EditorQuickFix, UiStrings.CommandTitleEditorQuickFix, CommandScope.Editor,
            CommandDispatch.Reserved, G(Key.OemPeriod, Ctrl)),
        new(CommandId.EditorExpandConstruct, UiStrings.CommandTitleEditorExpandConstruct, CommandScope.Editor,
            CommandDispatch.Reserved, G(Key.Tab)),
        new(CommandId.EditorNextDiagnostic, UiStrings.CommandTitleEditorNextDiagnostic, CommandScope.Editor,
            CommandDispatch.Reserved, G(Key.F8)),
        new(CommandId.EditorPreviousDiagnostic, UiStrings.CommandTitleEditorPreviousDiagnostic, CommandScope.Editor,
            CommandDispatch.Reserved, G(Key.F8, Shift)),

        // ── Tree scope (the Object Explorer) ─────────────────────────────────────────────────────────
        // F3 / F8 are also claimed at Grid scope below. That is not a clash: the focus is in the tree or
        // in a grid, never both, and each scope resolves through the owner of that surface's selection.
        new(CommandId.NewObject, UiStrings.CommandTitleNewObject, CommandScope.Tree, CommandDispatch.Routed,
            G(Key.F3)),
        new(CommandId.DeleteObject, UiStrings.CommandTitleDeleteObject, CommandScope.Tree, CommandDispatch.Routed,
            G(Key.F8)),
        new(CommandId.RefreshMetadata, UiStrings.CommandTitleRefreshMetadata, CommandScope.Tree,
            CommandDispatch.Routed, G(Key.F4)),

        // ── Grid scope (the collection lists) ────────────────────────────────────────────────────────
        // Insert / Delete are the keys the table's fields grid always had, kept as ALTERNATES so long-standing
        // muscle memory still works while the ratified F3 / F8 are what the menus and tooltips display. They
        // used to be three local DataGrid.KeyBindings, which is why that menu was the only place in the app
        // showing a hand-typed gesture; routing them removed the last such literal.
        new(CommandId.CollectionAdd, UiStrings.CommandTitleCollectionAdd, CommandScope.Grid, CommandDispatch.Routed,
            G(Key.F3), G(Key.Insert)),
        new(CommandId.CollectionEdit, UiStrings.CommandTitleCollectionEdit, CommandScope.Grid, CommandDispatch.Routed,
            G(Key.F2)),
        new(CommandId.CollectionRemove, UiStrings.CommandTitleCollectionRemove, CommandScope.Grid,
            CommandDispatch.Routed, G(Key.F8), G(Key.Delete)),

        // ── Global scope ─────────────────────────────────────────────────────────────────────────────
        new(CommandId.GlobalSearch, UiStrings.CommandTitleGlobalSearch, CommandScope.Global, CommandDispatch.Routed,
            G(Key.F, Ctrl | Shift)),
        new(CommandId.Commit, UiStrings.CommandTitleCommit, CommandScope.Global, CommandDispatch.Routed, G(Key.F6)),
        new(CommandId.Rollback, UiStrings.CommandTitleRollback, CommandScope.Global, CommandDispatch.Routed,
            G(Key.F6, Shift)),
        new(CommandId.CloseTab, UiStrings.CommandTitleCloseTab, CommandScope.Global, CommandDispatch.Routed,
            G(Key.W, Ctrl)),
        // Ctrl+F twice over is not a collision: Editor outranks Global, so the caret decides. That used to
        // be a hand-written focus probe in MainWindow's key handler; now it is the resolution order.
        new(CommandId.FocusSidebarFilter, UiStrings.CommandTitleFocusSidebarFilter, CommandScope.Global,
            CommandDispatch.Routed, G(Key.F, Ctrl)),
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
