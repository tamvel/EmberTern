using System.Collections.Generic;

namespace EmberTern.Core.Settings;

// User-preference section of ApplicationSettings — the home for cross-connection
// presentation choices (grid layouts, appearance) as distinct from connection data
// (Connections / Folders) and session restore data (Workspace). Foundation for
// upcoming milestones; persisted and round-tripped today, no consumers wired yet.
public sealed class UserSettings
{
    // One profile per grid, keyed by GridProfile.GridId.
    public List<GridProfile> GridProfiles { get; set; } = new();

    public AppearanceSettings Appearance { get; set; } = new();
}
