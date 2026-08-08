using System;
using System.Globalization;
using EmberTern.Core.Formatting;
using EmberTern.Core.Sql.Debugging;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One entry in the debugger's <b>Executed SQL</b> audit log (Stage X / D5, spec §10.3). Every expression
/// evaluation / Immediate run lands here — it is what makes §F ("what did EmberTern actually send?")
/// checkable rather than a promise. A read-only projection of one <see cref="EvaluationResult"/>: the user
/// fragment, the result (value or error) the server returned, and the generated harness SQL (kept visible,
/// not hidden, so the run is auditable). A statement that wrote frame variables is flagged as a side effect
/// (spec §9.5 — a fragment runs real SQL in the debug transaction and can have side effects).
/// </summary>
public sealed class DebugExecutedSqlRowViewModel
{
    private DebugExecutedSqlRowViewModel(
        string fragment, string kindLabel, string resultText, string sql, bool isError, bool hasSideEffect,
        string? rawError = null)
    {
        Fragment = fragment;
        KindLabel = kindLabel;
        ResultText = resultText;
        Sql = sql;
        IsError = isError;
        HasSideEffect = hasSideEffect;
        // The full Firebird message, kept alongside the friendly ResultText so it stays reachable on a friendly
        // error (D15.4 Seam B — "friendly + raw available"); null / empty when there is nothing extra to show.
        RawError = string.IsNullOrEmpty(rawError) || rawError == resultText ? null : rawError;
        TimestampText = DateTimeDisplay.LogTime(DateTime.Now);
    }

    /// <summary>The fragment the user evaluated / ran.</summary>
    public string Fragment { get; }

    /// <summary>Localized kind label ("expression" / "statement").</summary>
    public string KindLabel { get; }

    /// <summary>The value (Expression) or a short outcome note (Statement), or the raised error text.</summary>
    public string ResultText { get; }

    /// <summary>The generated harness SQL that was actually sent (the audit anchor, §10.3 / §F).</summary>
    public string Sql { get; }

    /// <summary>True when the fragment raised — the row renders in the error colour.</summary>
    public bool IsError { get; }

    /// <summary>The full raw Firebird message behind a friendly <see cref="ResultText"/>, or null when there
    /// is nothing extra (client-side failure, success, or a friendly text identical to the raw). Shown as the
    /// error line's tooltip so the raw message stays reachable (D15.4 Seam B).</summary>
    public string? RawError { get; }

    /// <summary>Whether a raw-message tooltip should be offered on the error line.</summary>
    public bool HasRawError => !string.IsNullOrEmpty(RawError);

    /// <summary>True when a statement wrote frame variables — flagged (spec §9.5 side-effect guard).</summary>
    public bool HasSideEffect { get; }

    public string TimestampText { get; }

    /// <summary>The side-effect marker glyph, or empty when there was no frame write-back.</summary>
    public string SideEffectGlyph => HasSideEffect ? "±" : string.Empty;

    /// <summary>Builds a row from a completed evaluation.</summary>
    public static DebugExecutedSqlRowViewModel ForResult(string fragment, EvaluationKind kind, EvaluationResult result)
    {
        string kindLabel = kind == EvaluationKind.Statement
            ? UiStrings.DebuggerEvalKindStatement
            : UiStrings.DebuggerEvalKindExpression;

        if (!result.Success)
        {
            // Friendly, categorised text (D15.4 Seam B) with the full Firebird message kept as the raw tooltip.
            string message = DebugErrorPresenter.Describe(result.Error);
            string raw = DebugErrorPresenter.Raw(result.Error);
            return new DebugExecutedSqlRowViewModel(
                fragment, kindLabel, message, result.Sql, isError: true, hasSideEffect: false, rawError: raw);
        }

        // A statement ran real SQL against the live frame + debug transaction: it is side-effect-capable by
        // nature (DML / generators / a frame write-back), so it is always flagged (spec §9.5). An expression
        // yields a value and assigns nothing — never flagged. The precise "which variables changed" is the
        // Variables panel's job (it reflects the applied write-back), not the audit flag's.
        bool isStatement = kind == EvaluationKind.Statement;
        string resultText = isStatement ? UiStrings.DebuggerEvalStatementOk : FormatValue(result.Value);
        return new DebugExecutedSqlRowViewModel(
            fragment, kindLabel, resultText, result.Sql, isError: false, hasSideEffect: isStatement);
    }

    /// <summary>Builds a row for a client-side failure (the evaluation could not even be issued).</summary>
    public static DebugExecutedSqlRowViewModel ForException(string fragment, string message)
        => new(fragment, UiStrings.DebuggerEvalKindExpression, message, string.Empty, isError: true, hasSideEffect: false);

    private static string FormatValue(object? value)
    {
        if (value is null || value is DBNull)
        {
            return UiStrings.DebuggerVariableNull;
        }
        return value switch
        {
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? UiStrings.DebuggerVariableNull,
        };
    }
}
