using System.Collections.Generic;

namespace EmberTern.Core.Settings;

// User-preference section of ApplicationSettings — the home for cross-connection
// presentation choices (grid layouts) as distinct from connection data
// (Connections / Folders) and session restore data (Workspace).
public sealed class UserSettings
{
    // One profile per grid, keyed by GridProfile.GridId.
    public List<GridProfile> GridProfiles { get; set; } = new();

    // Last-used Execute Procedure / Execute Function parameter sets, one entry per
    // (ConnectionId, ObjectKind, ObjectName). See ParameterHistoryStore.
    public List<ParameterHistoryEntry> ParameterHistory { get; set; } = new();

    // Debugger Watch expressions, one entry per (ConnectionId, ObjectName). See WatchStore
    // (Stage X / D5). Additive — an old settings.dat simply has an empty list.
    public List<DebugWatchEntry> DebugWatches { get; set; } = new();

    // Data Import configurations (etap I1). Today this holds the implicit "last used" entry per
    // connection, which the import surface restores when it opens; named profiles (etap I11) are more
    // entries in this same list, so that milestone is UI over an already-exercised store.
    // Additive — an old settings.dat simply has an empty list, and the container schema version is
    // deliberately NOT bumped for it (see ImportProfileStore).
    public List<Import.ImportProfile> ImportProfiles { get; set; } = new();
}
