using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.App.Localization;
using EmberTern.Core.Sql.Language;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Stage 7 / S4 — read-only projection of one Core <see cref="Diagnostic"/> for the Diagnostics panel.
/// Same card shape as <see cref="SessionWarningViewModel"/> / <see cref="FindingViewModel"/>: it holds the
/// source record and exposes display-only facets, including a theme brush <em>key</em> (never a brush —
/// no Avalonia types in a VM), resolved by <see cref="IconBrushConverter"/>.
/// <para>
/// Projects only — it never analyses, filters or re-orders. The <see cref="DiagnosticsEngine"/> is the
/// single source of truth (design §8.2); <see cref="Line"/>/<see cref="Column"/> are supplied by the view
/// layer, which owns the document and is the only place that can map an offset to a caret position.
/// </para>
/// </summary>
public sealed class DiagnosticRowViewModel : ObservableObject
{
    /// <param name="diagnostic">The engine's finding — kept verbatim, so a later milestone (S5
    /// navigation) can jump to <see cref="Diagnostic.Start"/> without a second projection.</param>
    /// <param name="line">1-based line of <see cref="Diagnostic.Start"/> in the analysed document.</param>
    /// <param name="column">1-based column of <see cref="Diagnostic.Start"/>.</param>
    public DiagnosticRowViewModel(Diagnostic diagnostic, int line, int column)
    {
        Diagnostic = diagnostic;
        Line = line;
        Column = column;
    }

    public Diagnostic Diagnostic { get; }

    public DiagnosticSeverity Severity => Diagnostic.Severity;

    /// <summary>The stable short code (<c>ET0001</c>, …).</summary>
    public string Code => Diagnostic.Code;

    /// <summary>
    /// Core's finding in the reader's language, resolved <b>at the moment of display</b> (D‑3) — never captured.
    ///
    /// <para>⭐ This row is an <see cref="ObservableObject"/> for this one property, and it deliberately does
    /// <b>not</b> subscribe to <c>Loc.LanguageChanged</c> itself: the panel owns exactly one subscription and
    /// calls <see cref="RaiseMessageChanged"/> on its rows (ratified W3). A subscription per row would be one
    /// leak per finding, and a large script has many.</para>
    /// </summary>
    public string Message => Loc.Format(Diagnostic.Message);

    /// <summary>
    /// Re-reads <see cref="Message"/> in the current language. ⛔ Called only by
    /// <see cref="DiagnosticsPanelViewModel"/>'s single language hook — the row never decides when to do this,
    /// because the row does not know when the language changed and must not find out.
    /// </summary>
    internal void RaiseMessageChanged() => OnPropertyChanged(nameof(Message));

    public int Line { get; }

    public int Column { get; }

    public string SeverityText => Severity switch
    {
        DiagnosticSeverity.Error => UiStrings.DiagnosticSeverityError,
        DiagnosticSeverity.Warning => UiStrings.DiagnosticSeverityWarning,
        _ => UiStrings.DiagnosticSeverityInfo,
    };

    /// <summary>Theme brush key for the severity icon, resolved via <see cref="IconBrushConverter"/>.
    /// Deliberately the SAME mapping the squiggle renderer paints with, so a row and the underline it
    /// describes always read as the same severity.</summary>
    public string SeverityBrushKey => Severity switch
    {
        DiagnosticSeverity.Error => "ErrorBrush",
        DiagnosticSeverity.Warning => "WarningBrush",
        _ => "SubtleForegroundBrush",
    };

    /// <summary>Warning-triangle glyph for a problem (shared with the Exception object icon, as in
    /// <see cref="SessionWarningViewModel"/>); a note glyph for the non-problem Info severity.</summary>
    public string SeverityGeometryKey => Severity == DiagnosticSeverity.Info
        ? "Icon.Comment"
        : "Icon.Exception";

    /// <summary>"Ln 12, Col 5" — the finding's location in the analysed document.</summary>
    public string LocationLabel => string.Format(
        System.Globalization.CultureInfo.CurrentCulture, UiStrings.DiagnosticsLocationFormat, Line, Column);
}
