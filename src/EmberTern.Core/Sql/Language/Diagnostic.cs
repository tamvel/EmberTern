using EmberTern.Core.Localization;

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
/// <param name="Message">
/// The description as a key plus its data (decision <b>D‑3</b>, etap C5) — <b>never a sentence</b>. Resolve it
/// with <c>Loc.Format</c> at the moment of display; the two surfaces that show it are the Diagnostics panel row
/// and the hover card.
///
/// <para>⭐⭐ <b>This member is why <see cref="Localization.LocalizableMessage"/> has structural equality.</b>
/// This is a <c>readonly record struct</c> and <c>DiagnosticsPanelViewModel.Update</c> skips rebuilding its
/// collection — keeping the user's selection — by comparing findings. A carrier whose argument list compared by
/// REFERENCE would have made two structurally identical diagnostics unequal, so the panel would have churned on
/// every debounce tick with a green build and no failing test. ⛔ Do not replace this with a type whose
/// equality is not structural, and do not add a member of one.</para>
/// </param>
/// <param name="Code">A stable short code (e.g. <c>"ET0001"</c>) for filtering/quick-fix targeting.</param>
/// <param name="Category">The semantic <see cref="DiagnosticCategory"/> (Stage 7). ⚠ Deliberately NOT the
/// source of <paramref name="Message"/>: the category says what KIND of problem this is (which is what
/// <c>QuickFixEngine</c> switches on) while the key says which SENTENCE, and <c>ET0008</c> proves they are not
/// one-to-one — one category, two sentences. Defaults to <see cref="DiagnosticCategory.None"/> for the
/// parser-recovery channel (which has no producer at this grammar depth); the
/// <see cref="DiagnosticsEngine"/> always sets it.</param>
public readonly record struct Diagnostic(
    int Start,
    int Length,
    DiagnosticSeverity Severity,
    LocalizableMessage Message,
    string Code,
    DiagnosticCategory Category = DiagnosticCategory.None)
{
    /// <summary>Absolute source offset just past the finding's span.</summary>
    public int End => Start + Length;
}
