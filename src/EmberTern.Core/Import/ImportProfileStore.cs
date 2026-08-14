using System;
using System.Collections.Generic;
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
/// <b>Etap I11 added the named-profile half</b>, exactly where the MVP's own comment reserved it: the API was
/// limited to the implicit "last used" entry because named operations would have been methods nothing called,
/// and a tested-but-uncalled component is indistinguishable from a regression (gotcha #233). They ship here
/// together with the UI that calls them. <b>Nothing about the stored shape changed</b> — a named profile is a
/// row in the list that already existed, differing from the implicit one only in having a
/// <see cref="ImportProfile.Name"/>, which is what <see cref="ImportProfile.IsImplicit"/> has always meant.
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

        return IsReadable(entry) ? configuration : null;
    }

    /// <summary>
    /// ⭐ Whether THIS build can honour the whole of a stored profile.
    /// <para>
    /// A configuration written by a newer build may carry fields whose meaning this one does not know, and
    /// applying the parts it recognises would be exactly the silent, partial restore §0.7 forbids. It lives here,
    /// as one predicate, because both the implicit restore and the named-profile list must answer it the same
    /// way — two copies of "is this too new" would eventually disagree, and the disagreement would show up as a
    /// profile that the list calls usable and the loader refuses.
    /// </para>
    /// </summary>
    public static bool IsReadable(ImportProfile profile)
        => profile.Configuration.Version <= ImportConfiguration.CurrentVersion;

    /// <summary>
    /// Records <paramref name="configuration"/> as the last used one for this connection, replacing any previous
    /// implicit entry. No-op when <paramref name="connectionId"/> is blank.
    /// </summary>
    public void SaveLastUsed(string? connectionId, ImportConfiguration configuration)
    {
        if (string.IsNullOrEmpty(connectionId)) return;

        // ⚠ Through Update — see ApplicationSettingsStore.Update.
        _settings.Update(settings =>
        {
            var list = settings.UserSettings.ImportProfiles;
            var entry = list.FirstOrDefault(p => IsImplicitFor(p, connectionId));
            if (entry is null)
            {
                entry = new ImportProfile { Name = string.Empty, ConnectionId = connectionId };
                list.Add(entry);
            }

            entry.Configuration = configuration;
            entry.LastUsedUtc = DateTime.UtcNow;
        });
    }

    /// <summary>Forgets the implicit entry for this connection — the „Wyczyść" affordance beside the
    /// „restored last configuration" hint (§4.8.4). No-op when there is nothing stored.</summary>
    public void ClearLastUsed(string? connectionId)
    {
        if (string.IsNullOrEmpty(connectionId)) return;

        // ⚠ Through Update (class B): this already declined to fabricate defaults, but its read still happened
        // outside the write lock, so it could act on a stale list. One locked operation now.
        _settings.Update(settings =>
        {
            var list = settings.UserSettings.ImportProfiles;
            var entry = list.FirstOrDefault(p => IsImplicitFor(p, connectionId));
            if (entry is not null)
            {
                list.Remove(entry);
            }
        });
    }

    // ── Named profiles (etap I11) ───────────────────────────────────────────────────────────────────────
    //
    // ⭐ Everything below reads and writes the SAME list the implicit entry has lived in since the MVP. There is
    // no named-profile record, no named-profile section and no second file: a named profile is an ImportProfile
    // whose Name is not empty, which is precisely what IsImplicit has always meant.

    /// <summary>
    /// The named profiles this connection may use, ordered by name.
    /// <para>
    /// <b>Scope, stated once and shown on screen:</b> a profile belongs to the connection it was saved on, plus
    /// any whose <see cref="ImportProfile.ConnectionId"/> is <c>null</c>. A profile made against another database
    /// is deliberately <b>not</b> offered — it names a table, and a table name borrowed from elsewhere is a
    /// promise this connection may not be able to keep.
    /// </para>
    /// <para>
    /// ⚠ The <c>null</c> branch is not speculative machinery: <see cref="ImportProfile.ConnectionId"/> is a
    /// nullable field of a persisted record, so that state can exist in a settings file, and a profile no query
    /// returns is unreachable data rather than an absent feature. Nothing in the UI writes one today — the
    /// <c>.json</c> exchange that §4.8.3 names as its source is not built — so this is the honest reading of a
    /// model state, not a producer waiting to be found.
    /// </para>
    /// <para>
    /// A profile this build cannot fully read is still RETURNED, not filtered out: hiding it would look exactly
    /// like it had been deleted. The caller asks <see cref="IsReadable"/> and says so instead (§4.8.3).
    /// </para>
    /// </summary>
    public IReadOnlyList<ImportProfile> ListNamed(string? connectionId)
    {
        var settings = _settings.Load();
        if (settings is null) return Array.Empty<ImportProfile>();

        return settings.UserSettings.ImportProfiles
            .Where(p => !p.IsImplicit && IsVisibleTo(p, connectionId))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The profile with this id, or <c>null</c>. Identity is the id, never the name — which is why a
    /// rename cannot orphan anything.</summary>
    public ImportProfile? GetById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        return _settings.Load()?.UserSettings.ImportProfiles
            .FirstOrDefault(p => !p.IsImplicit && string.Equals(p.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Stores <paramref name="configuration"/> under <paramref name="name"/>, <b>replacing</b> an existing
    /// profile of that name in the same scope rather than adding a second one — two identically named rows in
    /// the selector would be a list the user cannot read. The caller confirms the overwrite; this method only
    /// performs it.
    /// </summary>
    public ImportProfile SaveNamed(string? connectionId, string name, ImportConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A profile needs a name.", nameof(name));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var trimmed = name.Trim();

        // ⚠ Through Update — see ApplicationSettingsStore.Update. The entry is built inside the locked section
        // and handed back out, so the caller still receives the profile it asked for.
        ImportProfile? saved = null;
        _settings.Update(settings =>
        {
            var list = settings.UserSettings.ImportProfiles;

            var entry = list.FirstOrDefault(p => !p.IsImplicit
                                                 && IsVisibleTo(p, connectionId)
                                                 && NameMatches(p, trimmed));
            if (entry is null)
            {
                entry = new ImportProfile { Name = trimmed, ConnectionId = connectionId };
                list.Add(entry);
            }

            entry.Name = trimmed;
            entry.Configuration = configuration;
            entry.LastUsedUtc = DateTime.UtcNow;
            saved = entry;
        });

        // ⚠ A refused write (unreadable settings.dat) still owes the caller a profile object — the same shape
        // it always returned — but nothing was persisted; LastSaveDiagnostic on the store carries the reason.
        return saved ?? new ImportProfile
        {
            Name = trimmed,
            ConnectionId = connectionId,
            Configuration = configuration,
            LastUsedUtc = DateTime.UtcNow,
        };
    }

    /// <summary>True when a profile of this name already exists in the same scope — what turns "Save as…" into
    /// an overwrite the user is asked about first.</summary>
    public bool NameExists(string? connectionId, string name, string? exceptId = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var trimmed = name.Trim();
        var settings = _settings.Load();
        if (settings is null) return false;

        return settings.UserSettings.ImportProfiles.Any(
            p => !p.IsImplicit
                 && IsVisibleTo(p, connectionId)
                 && NameMatches(p, trimmed)
                 && !string.Equals(p.Id, exceptId, StringComparison.Ordinal));
    }

    /// <summary>Renames a profile. Returns <c>false</c> when the id is unknown or the new name is already taken
    /// in that profile's own scope — the caller reports it; the store never silently picks a different name.</summary>
    public bool Rename(string id, string newName)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(newName)) return false;

        var trimmed = newName.Trim();

        // ⚠ Through Update (class B). "Renamed" now means "renamed AND persisted": the check and the write are
        // one locked operation, so a name cannot be taken between deciding it was free and storing it.
        var renamed = false;
        var persisted = _settings.Update(settings =>
        {
            var list = settings.UserSettings.ImportProfiles;
            var entry = list.FirstOrDefault(p => !p.IsImplicit && string.Equals(p.Id, id, StringComparison.Ordinal));
            if (entry is null) return;

            var taken = list.Any(p => !p.IsImplicit
                                      && !ReferenceEquals(p, entry)
                                      && IsVisibleTo(p, entry.ConnectionId)
                                      && NameMatches(p, trimmed));
            if (taken) return;

            entry.Name = trimmed;
            renamed = true;
        });

        return renamed && persisted;
    }

    /// <summary>Removes a named profile. Destructive, so the caller confirms first (§0).</summary>
    public bool Delete(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;

        // ⚠ Through Update (class B). Same shape as Rename: true means the profile is gone FROM THE FILE.
        var removed = false;
        var persisted = _settings.Update(settings =>
        {
            var list = settings.UserSettings.ImportProfiles;
            var entry = list.FirstOrDefault(p => !p.IsImplicit && string.Equals(p.Id, id, StringComparison.Ordinal));
            if (entry is null) return;

            list.Remove(entry);
            removed = true;
        });

        return removed && persisted;
    }

    // ⚠ There is deliberately NO "mark this profile as used" here. ImportProfile.LastUsedUtc exists and is
    // stamped on save, but the selector is ordered by NAME — a list you look things up in — so a write that
    // touched only that field would change nothing anybody can see. A stored write with no observable effect is
    // the same shape as gotcha #233 from the other side: it looks like a working feature and proves nothing.
    // Ordering by recency is a decision to take when something actually asks for it.

    private static bool IsVisibleTo(ImportProfile profile, string? connectionId)
        => profile.ConnectionId is null
           || string.Equals(profile.ConnectionId, connectionId, StringComparison.Ordinal);

    private static bool NameMatches(ImportProfile profile, string name)
        => string.Equals(profile.Name, name, StringComparison.CurrentCultureIgnoreCase);

    private static bool IsImplicitFor(ImportProfile profile, string connectionId)
        => profile.IsImplicit
           && string.Equals(profile.ConnectionId, connectionId, StringComparison.Ordinal);
}
