using System;
using System.Collections.Generic;

namespace EmberTern.Core.Settings;

// Persisted "last used parameter sets" for Execute Procedure / Execute Function,
// living in UserSettings (the whole-file-encrypted settings.dat). One entry per
// (ConnectionId, ObjectKind, ObjectName); each entry keeps up to
// ParameterHistoryStore.MaxSets past executions, most-recent-first.
//
// Values are stored as canonical invariant strings (ParameterValue.Text) rather than
// boxed CLR objects — the VM re-hydrates them per Firebird type on load. This keeps
// the JSON readable and stable, and round-trips TIMESTAMP fractions losslessly
// (yyyy-MM-dd HH:mm:ss.FFFFFFF — same discipline as the grid filter, gotcha #161).
public sealed class ParameterHistoryEntry
{
    public string ConnectionId { get; set; } = string.Empty;

    // "Procedure" or "Function" — disambiguates a proc and a function sharing a name.
    public string ObjectKind { get; set; } = string.Empty;

    public string ObjectName { get; set; } = string.Empty;

    // Most-recent-first. Capped by ParameterHistoryStore.MaxSets on write.
    public List<ParameterSet> Executions { get; set; } = new();
}

public sealed class ParameterSet
{
    public DateTime ExecutedAt { get; set; }

    public List<ParameterValue> Values { get; set; } = new();
}

public sealed class ParameterValue
{
    public string Name { get; set; } = string.Empty;

    public bool IsNull { get; set; }

    // Canonical invariant-culture string form of the value; null when IsNull.
    public string? Text { get; set; }
}
