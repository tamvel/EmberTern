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

    // Data Import configurations (etap I1). Today this holds the implicit "last used" entry per
    // connection, which the import surface restores when it opens; named profiles (etap I11) are more
    // entries in this same list, so that milestone is UI over an already-exercised store.
    // Additive — an old settings.dat simply has an empty list, and the container schema version is
    // deliberately NOT bumped for it (see ImportProfileStore).
    public List<Import.ImportProfile> ImportProfiles { get; set; } = new();

    // ⭐ The scalar user preferences (theme, language, formatter casing, …). See PreferencesStore.
    //
    // Until this landed, this class held four LISTS and not one scalar — which is why every scalar
    // preference the app already had ended up in WorkspaceState beside window bounds: it was the only class
    // that ever accepted one. Preferences is where they belong; layout stays in WorkspaceState.
    //
    // Additive, and the container schema version is deliberately NOT bumped for it (the same reasoning as
    // ImportProfiles above): an older settings.dat has no Preferences key and deserializes to a default
    // instance, whereas a version bump would make an older build refuse the WHOLE file.
    public Preferences Preferences { get; set; } = new();

    // ⭐ The licence clock high-water mark (licensing design §16.3). The highest system time this
    // installation has ever seen; every start uses `max(systemNow, highWater)` as the effective moment.
    //
    // ⭐⭐ WHY IT EXISTS AT ALL: in V1 the expiry date is the ENTIRE enforcement mechanism, so leaving the
    // clock unguarded would make it a no-op — set the machine back a year and the licence lives again.
    // ⛔ It WARNS, it never blocks: a user legitimately correcting a badly wrong clock must not be locked
    // out of their tool (Architecture rule 11 governs licensing exactly as it governs the formatter), and
    // the tolerance is 48 h because time zones, DST, VM suspends, dead CMOS batteries and travelling
    // laptops are all normal.
    //
    // ⚠ It lives HERE rather than beside the licence file on purpose: this value is OURS, not the
    // customer's. The licence itself is deliberately outside settings.dat so it survives a settings reset
    // and can be copied by support; the high-water mark is the opposite — it should be as awkward to edit
    // as the rest of settings.dat already is (DPAPI, per user).
    //
    // ❌ It NEVER travels in a settings export — see SettingsExportContentTests. It is machine state, and
    // carrying it to another machine would import a stranger's clock. Nullable so that "never recorded"
    // stays distinguishable from "recorded as the epoch".
    public System.DateTimeOffset? LicenseClockHighWater { get; set; }
}
