using System.Collections.Generic;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Workspace;

public enum WorkspaceTabKind
{
    Query,
    Ddl,
    TableDetail,
    ViewDetail,
    ProcedureDetail,
    TriggerDetail,
    FunctionDetail,
    GeneratorDetail,
    DomainDetail,
    PackageDetail,
    ExceptionDetail,
    IndexDetail,
    SecurityManager,
}

// Stored verbatim as JSON. Properties are settable so System.Text.Json can
// deserialize without converters. Strings are nullable because Query/Ddl tabs
// only populate a subset of the fields.
public sealed class WorkspaceTab
{
    public WorkspaceTabKind Kind { get; set; }
    public string? SqlText { get; set; }
    public string? ObjectName { get; set; }
    public MetadataObjectKind? ObjectKind { get; set; }
    public string? ConnectionProfileId { get; set; }
    public string? DdlText { get; set; }

    // ── Per-tab UI state (hybrid model) ──────────────────────────────────────
    // Nullable so a legacy tab (without these fields) falls back to the global
    // default applied at tab creation. On restore the per-tab value wins; a
    // freshly opened object uses the global preference instead. All four are
    // unset for Query/Ddl tabs.

    // View + Procedure editor: Source (false) / Easy (true) mode.
    public bool? EasyMode { get; set; }
    // Active main sub-tab index — View (Editor/Fields/Dependencies/…),
    // Procedure (Editor/Description/…/Result), Table (Pola/Ograniczenia/…).
    public int? ActiveSubTabIndex { get; set; }
    // Active inner sub-tab index — Procedure's Easy collection tab
    // (Input/Output/Variables/Cursors/Subprograms) or Table's Constraints
    // sub-tab (PK/FK/Check/Unique). Unused by View.
    public int? ActiveInnerSubTabIndex { get; set; }
    // Table editor: whether the Pola grid is in edit mode.
    public bool? GridEditMode { get; set; }
}

// A named SQL snippet attached to a single connection. The active saved query's
// SqlText mirrors the Query tab's text — clicking a different one swaps the editor
// content; edits in the editor write back to the active SavedQuery.
public sealed class SavedQuery
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SqlText { get; set; } = "";
}

// One workspace per saved ConnectionProfile.Id. Holds the full tab list (Query +
// any DDL tabs the user opened against that connection) and the active-tab index
// at the moment the workspace was last serialized — either by an explicit Capture
// or by switching/disconnecting away from the connection.
public sealed class ConnectionWorkspace
{
    public List<WorkspaceTab> Tabs { get; set; } = new();
    public int ActiveTabIndex { get; set; }
    // Saved SQL snippets for this connection, surfaced in the IBExpert-style side
    // panel next to the editor. Empty on legacy workspaces (pre this milestone);
    // the VM bootstraps a single "Query 1" entry on first Connect in that case.
    public List<SavedQuery> SavedQueries { get; set; } = new();
    // SavedQuery.Id of the entry currently loaded into the editor. Null when the
    // list is empty (legacy state) or before the bootstrap step has run.
    public string? ActiveSavedQueryId { get; set; }
}

public sealed class WindowBounds
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    // Stored as string ("Normal" / "Maximized" / "Minimized" / "FullScreen")
    // so Core stays free of any Avalonia enum dependency.
    public string WindowState { get; set; } = "Normal";
}

public sealed class WorkspaceState
{
    public WindowBounds? WindowBounds { get; set; }
    // Keyed by ConnectionProfile.Id — each connection carries its own tab list,
    // SQL text, and active-tab pointer. Disconnect = stash into this dict;
    // connect = load back out of it. Workspace tabs are not visible without an
    // active connection.
    public Dictionary<string, ConnectionWorkspace> Workspaces { get; set; } = new();
    public string? LastActiveConnectionId { get; set; }
    // Whether the saved-queries side panel is shown. Global (not per-connection)
    // because layout preference is consistent across sessions and ERPs.
    public bool QueryPanelVisible { get; set; } = true;

    // Global layout preferences (like WindowBounds + QueryPanelVisible — not
    // per-connection). The View reads these from the loaded state and writes
    // them back at close, the same way it handles WindowBounds. Defaults match
    // the original fixed sizes so a legacy file (without these fields) restores
    // the exact prior layout.
    public double SidebarWidth { get; set; } = 280;
    public bool SidebarCollapsed { get; set; }
    public double ResultsPanelHeight { get; set; } = 280;

    // ⚠ ProcedureEasyMode / ViewEasyMode / TriggerEasyMode / FunctionEasyMode USED TO LIVE HERE, and were
    // removed by Settings Center etap 6 (§7.6). They were "last-used editor mode" flags rewritten by whatever
    // editor the user last toggled — so opening a procedure in Easy mode because of something done to a
    // DIFFERENT procedure yesterday looked like a bug rather than a preference.
    //
    // They are now Preferences.ProcedureEasyModeDefault & co: a stated default with one home and one way to
    // change it (Settings Center), and toggling a mode inside an editor is a per-tab action that persists
    // nothing globally. This class keeps what the user sets by dragging or clicking the thing itself; a
    // default they would go looking for in a settings dialog belongs in Preferences (design §5.2/5).
    //
    // ⚠ A restored tab's own WorkspaceTab.EasyMode is UNCHANGED and still wins over the default — that half of
    // the hybrid model was never the problem. Do not re-add a global flag here.

    // Whether the SQL editor's results panel is maximized (editor row collapsed).
    // Global layout preference, restored like ResultsPanelHeight.
    public bool ResultsMaximized { get; set; }

    // Selected bottom-panel tab in the SQL editor (0 = Results, 1 = Messages,
    // 2 = Output). Global UI preference; the last-viewed tab at close.
    public int BottomPanelTabIndex { get; set; }

    // Data Import's bottom panel (Source preview / Errors / Report). Global layout
    // preference restored exactly like ResultsPanelHeight — and it HAS to be global:
    // the import tab is deliberately transient (skipped by SnapshotCurrentTabs), so a
    // per-tab home would have nothing to be restored into.
    //
    // Note the boundary this deliberately respects: a panel height is a layout
    // preference, NOT a decision about an import, so it must never travel inside
    // ImportConfiguration (§4.8.2). Putting it there would make the reflection guard
    // demand that saved import profiles carry a pixel height.
    public double ImportPreviewPanelHeight { get; set; } = 190;
    public bool ImportPreviewPanelCollapsed { get; set; }
}
