using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Security;

namespace EmberTern.Core.Settings;

// Section facade over the unified ApplicationSettingsStore (settings.dat), mirroring
// WorkspaceStore / FolderStore / GridProfileStore. Owns UserSettings.ParameterHistory:
// the last-used Execute Procedure / Execute Function parameter sets. Each write is
// read-modify-write on that section so it never clobbers Connections / Folders /
// Workspace / GridProfiles in the shared file.
public sealed class ParameterHistoryStore
{
    // Per (connection, kind, name): keep at most this many past executions (FIFO).
    public const int MaxSets = 20;

    private readonly ApplicationSettingsStore _settings;

    public ParameterHistoryStore()
        : this(new ApplicationSettingsStore())
    {
    }

    public ParameterHistoryStore(SecretProtector protector)
        : this(new ApplicationSettingsStore(protector))
    {
    }

    public ParameterHistoryStore(string directory)
        : this(new ApplicationSettingsStore(directory))
    {
    }

    public ParameterHistoryStore(string directory, SecretProtector? protector)
        : this(new ApplicationSettingsStore(directory, protector))
    {
    }

    private ParameterHistoryStore(ApplicationSettingsStore settings)
    {
        _settings = settings;
    }

    public string FilePath => _settings.FilePath;

    // Past executions for this object, most-recent-first. Empty when nothing is saved
    // yet (or when connectionId/objectName is blank — history is disabled then).
    public IReadOnlyList<ParameterSet> Get(string? connectionId, string objectKind, string? objectName)
    {
        if (string.IsNullOrEmpty(connectionId) || string.IsNullOrEmpty(objectName))
        {
            return Array.Empty<ParameterSet>();
        }

        var entry = _settings.Load()?.UserSettings.ParameterHistory
            .FirstOrDefault(e => Matches(e, connectionId, objectKind, objectName));
        return entry?.Executions ?? (IReadOnlyList<ParameterSet>)Array.Empty<ParameterSet>();
    }

    // Records a just-run parameter set at the front of this object's history.
    // Dedup: if the newest stored set has identical values, its timestamp is refreshed
    // instead of adding a duplicate. Otherwise the new set is prepended and the list is
    // trimmed to MaxSets. No-op when connectionId/objectName is blank.
    public void Record(string? connectionId, string objectKind, string? objectName, IReadOnlyList<ParameterValue> values)
    {
        if (string.IsNullOrEmpty(connectionId) || string.IsNullOrEmpty(objectName))
        {
            return;
        }

        // ⚠ Through Update — see ApplicationSettingsStore.Update.
        _settings.Update(settings =>
        {
            var list = settings.UserSettings.ParameterHistory;
            var entry = list.FirstOrDefault(e => Matches(e, connectionId, objectKind, objectName));
            if (entry is null)
            {
                entry = new ParameterHistoryEntry
                {
                    ConnectionId = connectionId,
                    ObjectKind = objectKind,
                    ObjectName = objectName,
                };
                list.Add(entry);
            }

            var stamped = new ParameterSet
            {
                ExecutedAt = DateTime.Now,
                // TypeText is carried like every other field: dropping it here would store a value whose
                // compatibility can never again be proven, which is exactly what the restore rule needs it for.
                Values = values
                    .Select(v => new ParameterValue { Name = v.Name, IsNull = v.IsNull, Text = v.Text, TypeText = v.TypeText })
                    .ToList(),
            };

            if (entry.Executions.Count > 0 && ValuesEqual(entry.Executions[0].Values, stamped.Values))
            {
                // Same as the most recent run — just refresh its timestamp.
                entry.Executions[0].ExecutedAt = stamped.ExecutedAt;
            }
            else
            {
                entry.Executions.Insert(0, stamped);
                if (entry.Executions.Count > MaxSets)
                {
                    entry.Executions.RemoveRange(MaxSets, entry.Executions.Count - MaxSets);
                }
            }
        });
    }

    private static bool Matches(ParameterHistoryEntry e, string connectionId, string objectKind, string objectName)
        => string.Equals(e.ConnectionId, connectionId, StringComparison.Ordinal)
           && string.Equals(e.ObjectKind, objectKind, StringComparison.OrdinalIgnoreCase)
           && string.Equals(e.ObjectName, objectName, StringComparison.OrdinalIgnoreCase);

    private static bool ValuesEqual(List<ParameterValue> a, List<ParameterValue> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            // The type is part of what makes two runs "the same set". The same text entered under a different
            // type is a DIFFERENT set: treating it as a repeat would refresh the old entry's timestamp and keep
            // its stale type, so the value the user just used could never be proven restorable again.
            if (!string.Equals(a[i].Name, b[i].Name, StringComparison.OrdinalIgnoreCase)
                || a[i].IsNull != b[i].IsNull
                || !string.Equals(a[i].Text, b[i].Text, StringComparison.Ordinal)
                || !string.Equals(a[i].TypeText, b[i].TypeText, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }
}
