using EmberTern.Core.Security;
using EmberTern.Core.Settings;

namespace EmberTern.Core.Workspace;

// Section facade over the unified ApplicationSettingsStore (settings.dat). Public API
// unchanged from when this owned workspace.json — Load still returns null when there is
// no usable saved state (the View treats null as "nothing to restore"). Save is
// read-modify-write on the Workspace section so it preserves Connections / Folders /
// UserSettings in the shared file.
public sealed class WorkspaceStore
{
    private readonly ApplicationSettingsStore _settings;

    public WorkspaceStore()
        : this(new ApplicationSettingsStore())
    {
    }

    public WorkspaceStore(SecretProtector protector)
        : this(new ApplicationSettingsStore(protector))
    {
    }

    public WorkspaceStore(string directory)
        : this(new ApplicationSettingsStore(directory))
    {
    }

    public WorkspaceStore(string directory, SecretProtector? protector)
        : this(new ApplicationSettingsStore(directory, protector))
    {
    }

    private WorkspaceStore(ApplicationSettingsStore settings)
    {
        _settings = settings;
    }

    public string FilePath => _settings.FilePath;

    // Null when the unified file is missing / empty / corrupt / undecryptable — the
    // View's restore path keys off null to mean "no saved workspace".
    public WorkspaceState? Load()
        => _settings.Load()?.Workspace;

    // ⚠ Through Update — see ApplicationSettingsStore.Update for the measured reason.
    public void Save(WorkspaceState state)
        => _settings.Update(settings => settings.Workspace = state);
}
