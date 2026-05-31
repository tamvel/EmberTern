using System.Collections.Generic;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Workspace;

public enum WorkspaceTabKind
{
    Query,
    Ddl,
    TableDetail,
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
}
