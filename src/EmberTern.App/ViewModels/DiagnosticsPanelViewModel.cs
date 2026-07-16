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

    /// <summary>The highlighted row (S5). Two-way with the list; navigation also writes it, so the panel
    /// and the caret never disagree about which diagnostic is current. -1 when nothing is selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedRow))]
    private int _selectedIndex = -1;

    /// <summary>The selected row, or null when the selection is empty or stale.</summary>
    public DiagnosticRowViewModel? SelectedRow
        => SelectedIndex >= 0 && SelectedIndex < Diagnostics.Count ? Diagnostics[SelectedIndex] : null;

    /// <summary>True when the document is clean — the panel shows a readable "no diagnostics" state
    /// rather than an empty table (UX requirement).</summary>
    public bool ShowEmptyState => !HasDiagnostics;

    public string EmptyHint => UiStrings.DiagnosticsEmptyHint;

    /// <summary>
    /// Replaces the panel's contents with <paramref name="rows"/> (already ordered + projected by the
    /// view layer). No-ops when the findings are unchanged — a keystroke rebuilds the model every debounce
    /// tick, but the diagnostics usually do not change, and rebuilding the list would churn the UI and drop
    /// the user's selection (S5) for nothing. <see cref="Diagnostic"/> is a record struct, so this is a
    /// plain value comparison.
    /// </summary>
    public void Update(IReadOnlyList<DiagnosticRowViewModel> rows)
    {
        if (Unchanged(rows)) return;

        // The findings genuinely changed, so the old selection describes a list that no longer exists —
        // drop it rather than let an index point at an unrelated row. (Cleared first: a stale index must
        // never be observable against a half-rebuilt list.)
        SelectedIndex = -1;
        Diagnostics.Clear();
        foreach (var row in rows) Diagnostics.Add(row);
        HasDiagnostics = Diagnostics.Count > 0;
    }

    // ── Navigation (S5) — selection only; the view layer owns the caret ──────────────────────────
    //
    // Both lookups scan the panel's OWN order, which is the engine's order (DiagnosticsEngine.Finalize
    // sorts by Start, Length, Code and the panel never re-sorts). Reusing that one order — rather than
    // sorting again here — is what makes "the panel and navigation always agree" true by construction
    // instead of by two implementations happening to match.

    /// <summary>
    /// Index of the first diagnostic starting strictly after <paramref name="caretOffset"/>, wrapping to
    /// the first one when the caret is at or past the last; -1 when the document is clean (a silent no-op
    /// for the caller — never a prompt).
    /// </summary>
    public int IndexAfter(int caretOffset)
    {
        for (int i = 0; i < Diagnostics.Count; i++)
        {
            if (Diagnostics[i].Diagnostic.Start > caretOffset) return i;
        }
        return Diagnostics.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// Index of the last diagnostic starting strictly before <paramref name="caretOffset"/>, wrapping to
    /// the last one when the caret is at or before the first; -1 when the document is clean.
    /// </summary>
    public int IndexBefore(int caretOffset)
    {
        for (int i = Diagnostics.Count - 1; i >= 0; i--)
        {
            if (Diagnostics[i].Diagnostic.Start < caretOffset) return i;
        }
        return Diagnostics.Count > 0 ? Diagnostics.Count - 1 : -1;
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
