using System.Collections.Generic;

namespace EmberTern.App.ViewModels;

/// <summary>Smart SQL parameters to collect before an F5 run: the ordered (name, type) specs for
/// the reused Execute dialog, plus a stable per-statement history key so re-running the same
/// ad-hoc query recalls its last values.</summary>
public sealed record SmartParametersRequest(
    IReadOnlyList<(string Name, string TypeText)> Params,
    string HistoryKey);
