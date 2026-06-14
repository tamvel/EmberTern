using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Security;
using EmberTern.Core.Settings;

namespace EmberTern.Core.Connections;

// Section facade over the unified ApplicationSettingsStore (settings.dat). The public
// API is unchanged from when this owned connections.json — callers and tests don't
// notice the move. Each write is read-modify-write on the Connections section so it
// never clobbers Folders / Workspace / UserSettings in the shared file.
//
// Password is no longer encrypted per-field: the whole settings file is encrypted by
// the injected SecretProtector, so the password lives as plaintext JSON inside the
// encrypted blob. Migration from the legacy connections.json (v0 plaintext array or v1
// ProtectedPassword container) happens inside ApplicationSettingsStore on first load.
public sealed class ConnectionProfileStore
{
    private readonly ApplicationSettingsStore _settings;

    public ConnectionProfileStore()
        : this(new ApplicationSettingsStore())
    {
    }

    public ConnectionProfileStore(SecretProtector protector)
        : this(new ApplicationSettingsStore(protector))
    {
    }

    public ConnectionProfileStore(string directory)
        : this(new ApplicationSettingsStore(directory))
    {
    }

    public ConnectionProfileStore(string directory, SecretProtector? protector)
        : this(new ApplicationSettingsStore(directory, protector))
    {
    }

    private ConnectionProfileStore(ApplicationSettingsStore settings)
    {
        _settings = settings;
    }

    // Path of the unified settings file. Tests that inspect on-disk content read this.
    public string FilePath => _settings.FilePath;

    // The protector backing the shared file. The VM threads this into FolderStore so
    // every facade over the same dir encrypts (or doesn't) consistently. Public because
    // it crosses the Core→App assembly boundary; it holds no secret (just the delegate
    // pair — DPAPI keys are OS-managed).
    public SecretProtector Protector => _settings.Protector;

    public IReadOnlyList<ConnectionProfile> LoadAll()
        => _settings.Load()?.Connections ?? new List<ConnectionProfile>();

    public void SaveAll(IEnumerable<ConnectionProfile> profiles)
    {
        var settings = _settings.Load() ?? new ApplicationSettings();
        settings.Connections = profiles.ToList();
        _settings.Save(settings);
    }

    public void Upsert(ConnectionProfile profile)
    {
        var settings = _settings.Load() ?? new ApplicationSettings();
        var existing = settings.Connections.FindIndex(p => p.Id == profile.Id);
        if (existing >= 0)
        {
            settings.Connections[existing] = profile;
        }
        else
        {
            settings.Connections.Add(profile);
        }
        _settings.Save(settings);
    }

    public void Delete(string id)
    {
        var settings = _settings.Load() ?? new ApplicationSettings();
        settings.Connections = settings.Connections.Where(p => p.Id != id).ToList();
        _settings.Save(settings);
    }
}
