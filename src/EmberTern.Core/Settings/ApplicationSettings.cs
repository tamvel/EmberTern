using System.Collections.Generic;
using EmberTern.Core.Connections;
using EmberTern.Core.Workspace;

namespace EmberTern.Core.Settings;

// The single aggregate persisted as the whole-file-encrypted settings.dat. Replaces
// the three former files: connections.json (Connections), folders.json (Folders),
// workspace.json (Workspace). UserSettings is the foundation for grid/appearance
// preferences that future milestones will populate.
//
// The whole object is serialized to JSON and run through a SecretProtector at the I/O
// boundary (DPAPI in production), so the inner ConnectionProfile.Password is plaintext
// inside the encrypted blob — no per-field protection needed.
public sealed class ApplicationSettings
{
    public int SchemaVersion { get; set; } = ApplicationSettingsStore.CurrentSchemaVersion;

    public List<ConnectionProfile> Connections { get; set; } = new();

    public FolderState Folders { get; set; } = new();

    public WorkspaceState Workspace { get; set; } = new();

    public UserSettings UserSettings { get; set; } = new();
}
