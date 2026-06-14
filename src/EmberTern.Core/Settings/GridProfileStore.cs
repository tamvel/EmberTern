using System.Linq;
using EmberTern.Core.Security;

namespace EmberTern.Core.Settings;

// Section facade over the unified ApplicationSettingsStore (settings.dat), mirroring
// WorkspaceStore / FolderStore. Owns the UserSettings.GridProfiles list: one GridProfile
// per grid keyed by GridId. Each write is read-modify-write on that section so it never
// clobbers Connections / Folders / Workspace in the shared file.
public sealed class GridProfileStore
{
    private readonly ApplicationSettingsStore _settings;

    public GridProfileStore()
        : this(new ApplicationSettingsStore())
    {
    }

    public GridProfileStore(SecretProtector protector)
        : this(new ApplicationSettingsStore(protector))
    {
    }

    public GridProfileStore(string directory)
        : this(new ApplicationSettingsStore(directory))
    {
    }

    public GridProfileStore(string directory, SecretProtector? protector)
        : this(new ApplicationSettingsStore(directory, protector))
    {
    }

    private GridProfileStore(ApplicationSettingsStore settings)
    {
        _settings = settings;
    }

    public string FilePath => _settings.FilePath;

    // Null when no profile has been saved for this grid yet — the behavior layer treats
    // that as "use defaults" (AutoFit on, no saved order/widths).
    public GridProfile? Get(string gridId)
        => _settings.Load()?.UserSettings.GridProfiles.FirstOrDefault(p => p.GridId == gridId);

    public void Save(GridProfile profile)
    {
        var settings = _settings.Load() ?? new ApplicationSettings();
        var list = settings.UserSettings.GridProfiles;
        var index = list.FindIndex(p => p.GridId == profile.GridId);
        if (index >= 0)
        {
            list[index] = profile;
        }
        else
        {
            list.Add(profile);
        }
        _settings.Save(settings);
    }
}
