using System;
using System.Linq;
using EmberTern.Core.Security;
using EmberTern.Core.Settings;

namespace EmberTern.Core.Import;

/// <summary>
/// Section facade over the unified <c>ApplicationSettingsStore</c> (settings.dat), mirroring
/// <c>WatchStore</c> / <c>ParameterHistoryStore</c> / <c>GridProfileStore</c>. Owns
/// <c>UserSettings.ImportProfiles</c>. Every write is read-modify-write on that one section, so it can never
/// clobber Connections / Folders / Workspace / GridProfiles / ParameterHistory / DebugWatches in the shared
/// file.
/// <para>
/// <b>The settings.dat schema version is deliberately NOT bumped</b> for this section (design §4.8.3). Adding a
/// list is additive — an older file simply has none — whereas a version bump would trip the store's downgrade
/// protection and make an older build refuse the WHOLE file. That lesson is already recorded in this codebase.
/// </para>
/// <para>
/// The API is intentionally limited to the implicit "last used" profile, which is what the MVP actually calls
/// (etap I7). Named-profile operations arrive with their UI in etap I11 — adding them now would leave methods
/// nothing calls, and a tested-but-uncalled component is indistinguishable from a regression (gotcha #233).
/// </para>
/// </summary>
public sealed class ImportProfileStore
{
    private readonly ApplicationSettingsStore _settings;

    public ImportProfileStore()
        : this(new ApplicationSettingsStore())
    {
    }

    public ImportProfileStore(SecretProtector protector)
        : this(new ApplicationSettingsStore(protector))
    {
    }

    public ImportProfileStore(string directory)
        : this(new ApplicationSettingsStore(directory))
    {
    }

    public ImportProfileStore(string directory, SecretProtector? protector)
        : this(new ApplicationSettingsStore(directory, protector))
    {
    }

    private ImportProfileStore(ApplicationSettingsStore settings)
    {
        _settings = settings;
    }

    public string FilePath => _settings.FilePath;

    /// <summary>
    /// The configuration last used successfully against this connection, or <c>null</c> when there is none.
    /// <para>
    /// Returns <c>null</c> for a blank <paramref name="connectionId"/> — persistence is simply disabled then
    /// (the same rule <c>WatchStore</c> and <c>ParameterHistoryStore</c> follow, so a test or a session without
    /// a profile stores nothing rather than sharing one global slot).
    /// </para>
    /// <para>
    /// A configuration whose <see cref="ImportConfiguration.Version"/> is from the future is <b>not</b> returned
    /// half-read: this build cannot know what those fields mean, and applying the parts it recognises would be
    /// exactly the silent, partial restore §0.7 forbids.
    /// </para>
    /// </summary>
    public ImportConfiguration? GetLastUsed(string? connectionId)
    {
        if (string.IsNullOrEmpty(connectionId)) return null;

        var entry = _settings.Load()?.UserSettings.ImportProfiles
            .FirstOrDefault(p => IsImplicitFor(p, connectionId));
        if (entry?.Configuration is not { } configuration) return null;

        return configuration.Version > ImportConfiguration.CurrentVersion ? null : configuration;
    }

    /// <summary>
    /// Records <paramref name="configuration"/> as the last used one for this connection, replacing any previous
    /// implicit entry. No-op when <paramref name="connectionId"/> is blank.
    /// </summary>
    public void SaveLastUsed(string? connectionId, ImportConfiguration configuration)
    {
        if (string.IsNullOrEmpty(connectionId)) return;

        var settings = _settings.Load() ?? new ApplicationSettings();
        var list = settings.UserSettings.ImportProfiles;
        var entry = list.FirstOrDefault(p => IsImplicitFor(p, connectionId));
        if (entry is null)
        {
            entry = new ImportProfile { Name = string.Empty, ConnectionId = connectionId };
            list.Add(entry);
        }

        entry.Configuration = configuration;
        entry.LastUsedUtc = DateTime.UtcNow;
        _settings.Save(settings);
    }

    /// <summary>Forgets the implicit entry for this connection — the „Wyczyść" affordance beside the
    /// „restored last configuration" hint (§4.8.4). No-op when there is nothing stored.</summary>
    public void ClearLastUsed(string? connectionId)
    {
        if (string.IsNullOrEmpty(connectionId)) return;

        var settings = _settings.Load();
        if (settings is null) return;

        var list = settings.UserSettings.ImportProfiles;
        var entry = list.FirstOrDefault(p => IsImplicitFor(p, connectionId));
        if (entry is null) return;

        list.Remove(entry);
        _settings.Save(settings);
    }

    private static bool IsImplicitFor(ImportProfile profile, string connectionId)
        => profile.IsImplicit
           && string.Equals(profile.ConnectionId, connectionId, StringComparison.Ordinal);
}
