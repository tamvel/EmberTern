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

    /// <summary>
    /// Whether <c>settings.dat</c> is readable, and what is wrong when it is not (audit A-03).
    /// <para>Exposed here because this facade is the one the App constructs and holds, and because the App has
    /// an obligation this layer cannot discharge: when the file cannot be read, EVERY store silently stops
    /// persisting — <see cref="ApplicationSettingsStore.Save"/> refuses rather than destroy data it cannot
    /// see — and a user who is not told that would reasonably believe their work is being saved.</para>
    /// </summary>
    public SettingsLoadResult CheckSettingsHealth() => _settings.LoadWithStatus();

    /// <summary>
    /// The last load diagnostic as a <see cref="Localization.LocalizableMessage"/> (D‑3), for the banner that
    /// shows it. ⚠ Read AFTER <see cref="CheckSettingsHealth"/> — it describes that call's outcome.
    ///
    /// <para>⛔ Deliberately not a member of <see cref="SettingsLoadResult"/>: that type is a
    /// <c>readonly record struct</c> whose value equality would degrade to a reference comparison of the
    /// message's argument list, which is the trap the C0 audit measured on <c>Diagnostic</c>.</para>
    /// </summary>
    public Localization.LocalizableMessage? SettingsMessage => _settings.LastLoadMessage;

    public IReadOnlyList<ConnectionProfile> LoadAll()
        => _settings.Load()?.Connections ?? new List<ConnectionProfile>();

    // ⚠ These three read, change one section and write. They go through ApplicationSettingsStore.Update so the
    // read and the write are one locked operation: `Load() ?? new ApplicationSettings()` turned a transient
    // read failure into DEFAULTS, and saving those replaced every profile and password in the file.
    public void SaveAll(IEnumerable<ConnectionProfile> profiles)
        => _settings.Update(settings => settings.Connections = profiles.ToList());

    public void Upsert(ConnectionProfile profile)
        => _settings.Update(settings =>
        {
            var existing = settings.Connections.FindIndex(p => p.Id == profile.Id);
            if (existing >= 0)
            {
                settings.Connections[existing] = profile;
            }
            else
            {
                settings.Connections.Add(profile);
            }
        });

    public void Delete(string id)
        => _settings.Update(
            settings => settings.Connections = settings.Connections.Where(p => p.Id != id).ToList());
}
