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
}
