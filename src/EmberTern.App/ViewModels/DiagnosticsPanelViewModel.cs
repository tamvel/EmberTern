using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.App.Localization;
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

    /// <summary>
    /// ⭐⭐ <b>The panel's ONE language subscription (ratified W3, etap C5), and every clause of it is
    /// load-bearing.</b> Core hands up a key plus data, so a row's text only exists once resolved — and a row is
    /// resolved when its <c>Message</c> is read, which after a language change nothing does on its own (#353).
    ///
    /// <para>⛔ <b>The obvious repair — rebuild the rows and republish — cannot work here, and that is the trap
    /// worth knowing:</b> <see cref="Update"/> no-ops when <see cref="Unchanged"/> says the findings are the
    /// same, which after a mere language change they are. The optimisation that protects the user's selection
    /// would swallow the refresh. So this hook does not touch the collection at all: it asks each existing row to
    /// re-read its own text. No rebuild, no <c>CollectionChanged</c>, no lost selection.</para>
    ///
    /// <para>⭐ One subscription for the whole panel rather than one per row: a row-level subscription would be a
    /// leak per finding, and a large script has many. The rows stay ignorant of the language on purpose.</para>
    /// </summary>
    public DiagnosticsPanelViewModel()
    {
        Loc.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, System.EventArgs e)
    {
        foreach (var row in Diagnostics)
        {
            row.RaiseMessageChanged();
        }
    }

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
