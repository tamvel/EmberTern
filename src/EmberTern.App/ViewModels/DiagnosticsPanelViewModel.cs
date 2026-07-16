using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Sql.Language;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Stage 7 / S4 — the Diagnostics panel: a <b>view of</b> the diagnostics the pure-Core
/// <see cref="DiagnosticsEngine"/> produced for the active document, nothing more.
/// <para>
/// It analyses nothing, filters nothing, sorts nothing and invents nothing — it holds whatever rows the
/// view layer publishes, in the engine's own order (design §8.2: the engine is the single source of
/// truth). The rows arrive from the per-editor language service's <em>cached</em>, version-matched
/// diagnostics on the shared <c>ModelUpdated</c> cycle, so a refresh costs no parse, no model rebuild and
/// no second analysis. Every refresh trigger — a text edit, a model rebuild, a metadata generation bump,
/// an Easy-mode ambient-symbol change — reaches the panel through that one signal.
/// </para>
/// </summary>
public sealed partial class DiagnosticsPanelViewModel : ViewModelBase
{
    /// <summary>The current document's diagnostics, in <see cref="DiagnosticsEngine"/> order.</summary>
    public ObservableCollection<DiagnosticRowViewModel> Diagnostics { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasDiagnostics;

    /// <summary>True when the document is clean — the panel shows a readable "no diagnostics" state
    /// rather than an empty table (UX requirement).</summary>
    public bool ShowEmptyState => !HasDiagnostics;

    public string EmptyHint => UiStrings.DiagnosticsEmptyHint;

    /// <summary>
    /// Replaces the panel's contents with <paramref name="rows"/> (already ordered + projected by the
    /// view layer). No-ops when the findings are unchanged — a keystroke rebuilds the model every debounce
    /// tick, but the diagnostics usually do not change, and rebuilding the list would churn the UI (and,
    /// from S5 on, drop the user's selection) for nothing. <see cref="Diagnostic"/> is a record struct, so
    /// this is a plain value comparison.
    /// </summary>
    public void Update(IReadOnlyList<DiagnosticRowViewModel> rows)
    {
        if (Unchanged(rows)) return;

        Diagnostics.Clear();
        foreach (var row in rows) Diagnostics.Add(row);
        HasDiagnostics = Diagnostics.Count > 0;
    }

    private bool Unchanged(IReadOnlyList<DiagnosticRowViewModel> rows)
    {
        if (rows.Count != Diagnostics.Count) return false;
        for (int i = 0; i < rows.Count; i++)
        {
            // Compare the engine's finding AND the resolved location: an edit above a diagnostic moves it
            // to a new line without changing the record's identity, and the panel must show the new one.
            if (!Diagnostics[i].Diagnostic.Equals(rows[i].Diagnostic)) return false;
            if (Diagnostics[i].Line != rows[i].Line || Diagnostics[i].Column != rows[i].Column) return false;
        }
        return true;
    }
}
