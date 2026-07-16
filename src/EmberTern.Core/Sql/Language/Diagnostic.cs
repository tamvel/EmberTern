namespace EmberTern.Core.Sql.Language;

/// <summary>The severity of a parser/analysis <see cref="Diagnostic"/>.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational — a note, never a problem.</summary>
    Info,

    /// <summary>A likely problem that does not stop the parse.</summary>
    Warning,

    /// <summary>A definite error at this span.</summary>
    Error,
}

/// <summary>
/// A parser (later: semantic) finding at a source span. Etap 2 exposes the channel but keeps it
/// deliberately quiet: at statement-segmentation depth every byte lands in some statement (an
/// unrecognised one becomes a <see cref="Ast.RawStatement"/>, which is the §0 safety valve, not
/// an error), so there is nothing to report. Real recovery diagnostics arrive with clause/PSQL
/// parsing (later etaps); user-facing squiggles + quick fixes are the Diagnostics Engine (Etap 7).
/// The richer shape (quick fixes, codes) is added then.
/// </summary>
/// <param name="Start">Absolute source offset where the finding begins.</param>
/// <param name="Length">Length of the finding's span, in characters.</param>
/// <param name="Severity">Its severity.</param>
/// <param name="Message">A human-readable description.</param>
/// <param name="Code">A stable short code (e.g. <c>"ET0001"</c>) for future filtering/quick-fixes.</param>
/// <param name="Category">The semantic <see cref="DiagnosticCategory"/> (Stage 7). Additive — defaults
/// to <see cref="DiagnosticCategory.None"/> so the parser-recovery channel (which predates categories)
/// keeps its existing shape; the <see cref="DiagnosticsEngine"/> always sets it.</param>
public readonly record struct Diagnostic(
    int Start,
    int Length,
    DiagnosticSeverity Severity,
    string Message,
    string Code,
    DiagnosticCategory Category = DiagnosticCategory.None)
{
    /// <summary>Absolute source offset just past the finding's span.</summary>
    public int End => Start + Length;
}
