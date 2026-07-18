using System.Collections.Generic;

namespace EmberTern.Core.Settings;

// The debugger's persisted Watch expressions for one routine (Stage X / D5 seam b, spec §9.5 / §10 — Watches
// are "persisted per routine"). One entry per (ConnectionId, ObjectName); Expressions is the ordered watch
// list. See WatchStore. Additive to UserSettings — an old settings.dat simply has none.
public sealed class DebugWatchEntry
{
    public string ConnectionId { get; set; } = string.Empty;

    // The routine (procedure) the watches belong to. D5 scope is standalone procedures.
    public string ObjectName { get; set; } = string.Empty;

    // The watch expressions, in display order.
    public List<string> Expressions { get; set; } = new();
}
