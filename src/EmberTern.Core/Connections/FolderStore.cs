using System.Collections.Generic;
using EmberTern.Core.Security;
using EmberTern.Core.Settings;

namespace EmberTern.Core.Connections;

// In-memory snapshot of the folder layout. ConnectionFolderMap maps a
// ConnectionProfile.Id to its parent FolderEntry.Id. Connections whose id is
// absent (or whose value is empty) live at the root. ConnectionSortOrders
// carries the per-connection sort key so the tree's ordering is preserved
// after restart — connection profile JSON stays untouched (forward-compatible
// with profiles written by older builds).
public sealed class FolderState
{
    public List<FolderEntry> Folders { get; set; } = new();
    public Dictionary<string, string> ConnectionFolderMap { get; set; } = new();
    public Dictionary<string, int> ConnectionSortOrders { get; set; } = new();
    // Ids of tree nodes (FolderEntry.Id or ConnectionProfile.Id) that should
    // currently render expanded. Presence == expanded, absence == collapsed.
    // The asymmetric defaults between folders (default expanded) and connections
    // (default collapsed) are reconciled by seeding the set with new folder ids
    // on creation and by the one-time legacy migration gated on
    // <see cref="ExpandStateInitialized"/>.
    public HashSet<string> ExpandedNodeIds { get; set; } = new();
    // False on first load after this feature shipped (the field is absent from
    // legacy folders.json so the deserializer leaves it at the default false).
    // The next ReloadConnections seeds ExpandedNodeIds with all known folder ids
    // (since folders were default-expanded pre-feature) and flips this to true so
    // subsequent runs treat the set as fully authoritative.
    public bool ExpandStateInitialized { get; set; }
}

// Section facade over the unified ApplicationSettingsStore (settings.dat). Public API
// unchanged from when this owned folders.json. Save is read-modify-write on the Folders
// section so it preserves Connections / Workspace / UserSettings in the shared file.
public sealed class FolderStore
{
    private readonly ApplicationSettingsStore _settings;

    public FolderStore()
        : this(new ApplicationSettingsStore())
    {
    }

    public FolderStore(SecretProtector protector)
        : this(new ApplicationSettingsStore(protector))
    {
    }

    public FolderStore(string directory)
        : this(new ApplicationSettingsStore(directory))
    {
    }

    public FolderStore(string directory, SecretProtector? protector)
        : this(new ApplicationSettingsStore(directory, protector))
    {
    }

    private FolderStore(ApplicationSettingsStore settings)
    {
        _settings = settings;
    }

    public string FilePath => _settings.FilePath;

    // Missing / corrupt / unreadable file → fresh empty state (same forgiving stance as
    // before), via ApplicationSettingsStore.Load returning null.
    public FolderState Load()
        => _settings.Load()?.Folders ?? new FolderState();

    // ⚠ Through Update, so a transient read failure cannot substitute defaults for the rest of the aggregate
    // (connection profiles, passwords, workspace) and write them over the file.
    public void Save(FolderState state)
        => _settings.Update(settings => settings.Folders = state);
}
