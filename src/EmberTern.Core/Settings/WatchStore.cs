using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Security;

namespace EmberTern.Core.Settings;

// Section facade over the unified ApplicationSettingsStore (settings.dat), mirroring ParameterHistoryStore /
// WorkspaceStore / FolderStore. Owns UserSettings.DebugWatches: the debugger's persisted Watch expressions,
// one entry per (ConnectionId, ObjectName), so a routine's watches survive a Stop / Restart / app restart
// (spec §9.5 — Watches are "persisted per routine"). Each write is read-modify-write on that section so it
// never clobbers Connections / Folders / Workspace / GridProfiles / ParameterHistory in the shared file.
public sealed class WatchStore
{
    private readonly ApplicationSettingsStore _settings;

    public WatchStore()
        : this(new ApplicationSettingsStore())
    {
    }

    public WatchStore(string directory)
        : this(new ApplicationSettingsStore(directory))
    {
    }

    public WatchStore(string directory, SecretProtector? protector)
        : this(new ApplicationSettingsStore(directory, protector))
    {
    }

    private WatchStore(ApplicationSettingsStore settings)
    {
        _settings = settings;
    }

    public string FilePath => _settings.FilePath;

    // The saved watch expressions for this routine, in order. Empty when nothing is saved (or when
    // connectionId/objectName is blank — persistence is disabled then, e.g. in tests without a profile).
    public IReadOnlyList<string> Get(string? connectionId, string? objectName)
    {
        if (string.IsNullOrEmpty(connectionId) || string.IsNullOrEmpty(objectName))
        {
            return Array.Empty<string>();
        }

        var entry = _settings.Load()?.UserSettings.DebugWatches
            .FirstOrDefault(e => Matches(e, connectionId, objectName));
        return entry?.Expressions ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    // Replaces this routine's watch list with expressions (an empty list removes the entry). No-op when
    // connectionId/objectName is blank.
    public void Save(string? connectionId, string? objectName, IReadOnlyList<string> expressions)
    {
        if (string.IsNullOrEmpty(connectionId) || string.IsNullOrEmpty(objectName))
        {
            return;
        }

        var settings = _settings.Load() ?? new ApplicationSettings();
        var list = settings.UserSettings.DebugWatches;
        var entry = list.FirstOrDefault(e => Matches(e, connectionId, objectName));

        if (expressions.Count == 0)
        {
            if (entry is not null)
            {
                list.Remove(entry);
            }
        }
        else
        {
            if (entry is null)
            {
                entry = new DebugWatchEntry { ConnectionId = connectionId, ObjectName = objectName };
                list.Add(entry);
            }
            entry.Expressions = expressions.ToList();
        }

        _settings.Save(settings);
    }

    private static bool Matches(DebugWatchEntry e, string connectionId, string objectName)
        => string.Equals(e.ConnectionId, connectionId, StringComparison.Ordinal)
           && string.Equals(e.ObjectName, objectName, StringComparison.OrdinalIgnoreCase);
}
