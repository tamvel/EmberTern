using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// A frame's variable store — the client-side truth for the frame's local variables and parameters
/// (spec §3.7: <c>FrameValues</c> is "the truth"). Values are opaque <see cref="object"/>s: the client
/// owns control flow, the server owns types, so the interpreter never inspects or coerces a value — it
/// injects the read set into the harness and applies the write-back the server returns (spec §3.5).
/// Names are folded case-insensitively (Firebird folds unquoted identifiers).
/// </summary>
public sealed class FrameValues
{
    private readonly Dictionary<string, object?> _values;

    public FrameValues(IReadOnlyDictionary<string, object?>? seed = null)
        => _values = seed is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(seed, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when a variable of this name is defined in this frame (even if its value is null).</summary>
    public bool Contains(string name) => _values.ContainsKey(name);

    /// <summary>The variable's value, or null when it is not defined here (indistinguishable from a
    /// defined-but-null variable — use <see cref="Contains"/> to tell them apart).</summary>
    public object? Get(string name) => _values.TryGetValue(name, out var v) ? v : null;

    public bool TryGet(string name, out object? value) => _values.TryGetValue(name, out value);

    /// <summary>Sets (defining if absent) a variable's value.</summary>
    public void Set(string name, object? value) => _values[name] = value;

    /// <summary>Applies a write-back set (the variables a statement/cursor wrote); no-op when null.</summary>
    public void Apply(IReadOnlyDictionary<string, object?>? writes)
    {
        if (writes is null) return;
        foreach (var kv in writes) _values[kv.Key] = kv.Value;
    }

    /// <summary>The names defined in this frame.</summary>
    public IReadOnlyCollection<string> Names => _values.Keys;

    /// <summary>An immutable snapshot (for change highlighting / step-back — later milestones).</summary>
    public IReadOnlyDictionary<string, object?> Snapshot()
        => new Dictionary<string, object?>(_values, StringComparer.OrdinalIgnoreCase);
}
