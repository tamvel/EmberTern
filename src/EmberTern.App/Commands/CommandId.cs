namespace EmberTern.App.Commands;

/// <summary>
/// The stable identity of a user-facing command. Every surface — gesture, tooltip, context menu, and
/// later the Command Palette and the shortcut editor — refers to a command by this id, never by its
/// gesture or its label.
///
/// <para>⚠ <b>Names are the persistence contract</b> for a future user keymap, so a member is renamed
/// only with a migration. Numeric values are NOT persisted and carry no meaning.</para>
///
/// <para>⚠ <b>This enum is deliberately NOT a mirror of every command in the app.</b> There are ~365
/// <c>[RelayCommand]</c>s and ~73 <c>Click</c> handlers; a registry that tried to list them all would be
/// a table nobody maintains. A command earns a <c>CommandId</c> only when a shared surface must speak
/// about it — a gesture, a menu entry with a shown shortcut, or (later) a palette entry.</para>
/// </summary>
public enum CommandId
{
    // ── Tab scope ───────────────────────────────────────────────────────────────────────────────────
    // Resolved through WorkspaceTabViewModel.ResolveCommand, which maps tab kind → the tab's own command.

    /// <summary>
    /// The main action of the active tab — F5. Execute in the SQL editor, Start/Continue in the debugger,
    /// Run in the Script Executor, Import in Data Import, and <b>nothing at all</b> anywhere else.
    /// <para>That last clause is the whole point: F5 used to fall through to "execute the SQL editor's
    /// text" from every tab that did not claim it, which ran the user's editor content — in the user's
    /// working transaction — from tabs like Security Manager or a Table editor.</para>
    /// </summary>
    Go,

    /// <summary>Execute the SQL editor's query — Ctrl+Enter. Unlike <see cref="Go"/> this always means
    /// the query, so it stays unambiguous on a tab that has its own main action.</summary>
    ExecuteQuery,

    /// <summary>Execute the SQL editor's query without the preview row ceiling — Shift+F5.</summary>
    ExecuteQueryFull,

    /// <summary>Format the SQL of the active tab's editor — Ctrl+K.</summary>
    FormatSql,

    /// <summary>Compile the object the active editor tab is editing — F7. The application's most-used
    /// action after Execute, and it had no shortcut at all before this sprint.</summary>
    Compile,

    /// <summary>Data Import: run the whole pipeline except the write — Ctrl+F5.</summary>
    ImportValidate,

    /// <summary>Data Import: re-read the source — Ctrl+R, or Ctrl+V for the clipboard source.</summary>
    ImportRefresh,

    /// <summary>Data Import: pick the source file — Ctrl+O.</summary>
    ImportBrowse,

    // Debugger — declared so the collision validator and, later, menus and tooltips can see them.
    // Dispatch stays inside DebuggerTabView, which owns the stepping surface.
    DebuggerStepOver,
    DebuggerStepInto,
    DebuggerStepOut,
    DebuggerRunToCursor,
    DebuggerStop,
    DebuggerRestart,
    DebuggerToggleBreakpoint,
    DebuggerEvaluateSelection,
    DebuggerSaveSource,

    // ── Editor scope ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Open the focused editor's Find bar — Ctrl+F. Outranks
    /// <see cref="FocusSidebarFilter"/> by scope, not by a focus probe at the window.</summary>
    EditorFind,

    /// <summary>Open the focused editor's Replace bar — Ctrl+H. Refused on a read-only editor.</summary>
    EditorReplace,

    // The typing mechanics. Declared, never routed: they are intentionally local and tunnelled
    // (gotchas #224 / #228), and moving their dispatch here would break the guarantee that they see a
    // keystroke before anything else does. Declaring them is what stops a future global gesture from
    // silently stealing one.
    EditorCompletion,
    EditorParameterHelper,
    EditorRename,
    EditorPeekDefinition,
    EditorQuickFix,
    EditorExpandConstruct,
    EditorNextDiagnostic,
    EditorPreviousDiagnostic,

    // ── Tree scope (the Object Explorer) ────────────────────────────────────────────────────────────

    /// <summary>Create a new object of the selected category — F3.</summary>
    NewObject,

    /// <summary>Drop the selected object — F8. Routes to the existing confirmed delete, so a single
    /// keystroke opens the confirmation dialog and never drops anything on its own.</summary>
    DeleteObject,

    /// <summary>Re-read the object tree — F4.</summary>
    RefreshMetadata,

    // ── Grid scope (the collection lists) ───────────────────────────────────────────────────────────
    // Both route through the application's EXISTING unified collection router (the toolbar's + / −),
    // whose ActiveCollection() already answers "which collection is the user editing". Nothing here
    // needs to know about a particular grid.

    /// <summary>Add a row to the collection the focused grid belongs to — F3 (or Insert).</summary>
    CollectionAdd,

    /// <summary>
    /// Edit the selected row of that collection — F2. Exists only where the collection has an edit action
    /// (the table fields grid, whose items open a dialog); elsewhere it resolves to null and the gesture
    /// falls through, because a grid edited in place needs no separate Edit.
    /// </summary>
    CollectionEdit,

    /// <summary>Remove the selected row from that collection — F8 (or Delete).</summary>
    CollectionRemove,

    // ── Global scope ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Global Search over metadata names and source — Ctrl+Shift+F.</summary>
    GlobalSearch,

    /// <summary>Focus the Object Explorer's filter box — Ctrl+F, when the caret is not in an editor.</summary>
    FocusSidebarFilter,

    /// <summary>Commit the working transaction — F6. Binds the very command the toolbar button does.</summary>
    Commit,

    /// <summary>Roll the working transaction back — Shift+F6.</summary>
    Rollback,

    /// <summary>Close the active workspace tab — Ctrl+W. Routes through the confirming close, so a tab
    /// with unsaved work still offers Save / Discard / Cancel.</summary>
    CloseTab,
}
