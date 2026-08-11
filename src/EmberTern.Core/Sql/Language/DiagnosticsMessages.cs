using EmberTern.Core.Localization;

namespace EmberTern.Core.Sql.Language;

/// <summary>
/// What the <see cref="DiagnosticsEngine"/> says about a finding — decision <b>D‑3</b>'s producer for the
/// editor's semantic diagnostics (etap C5).
///
/// <para>⭐ <b>Nine keys for eight codes, and the extra one is the point.</b> Eight of the nine map one-to-one
/// onto a <see cref="DiagnosticCategory"/>; <c>ET0008</c> needs <b>two</b> — see
/// <see cref="SuspendInTrigger"/>. That is also why the key lives on <see cref="Diagnostic"/> rather than being
/// derived from the category: the category answers <i>what KIND of problem this is</i> (which is what
/// <c>QuickFixEngine</c> switches on) and the key answers <i>which SENTENCE</i>. Two questions, two fields.</para>
///
/// <para>⚠ <b>Every <c>{n}</c> is DATA</b> — an identifier the user wrote, or a count. Nothing here substitutes
/// one of EmberTern's own words into a sentence.</para>
///
/// <para>⚠ <b>Unlike C3/C4a/C4b there is no English twin, and that is measured rather than a shortcut:</b>
/// <c>DiagnosticsEngineTests</c> asserts <c>Category</c>, <c>Code</c>, <c>Severity</c>, <c>Start</c> and
/// <c>Length</c> and never touches the message text, so there was no shipped wording pinned to preserve. This is
/// the C2 (Quick Info) shape — the type changes — not the C4a one.</para>
/// </summary>
public static class DiagnosticsMessages
{
    /// <summary>ET0001 — the object's name as <c>{0}</c>.</summary>
    public static readonly MessageKey UnknownObject = new("Sql.Diagnostics.UnknownObject");

    /// <summary>ET0002 — the column's name as <c>{0}</c>.</summary>
    public static readonly MessageKey UnknownColumn = new("Sql.Diagnostics.UnknownColumn");

    /// <summary>ET0003 — the variable's name as <c>{0}</c>.</summary>
    public static readonly MessageKey UnresolvedVariable = new("Sql.Diagnostics.UnresolvedVariable");

    /// <summary>ET0004 — the parameter's name as <c>{0}</c>.</summary>
    public static readonly MessageKey UnresolvedParameter = new("Sql.Diagnostics.UnresolvedParameter");

    /// <summary>ET0005 — the column's name as <c>{0}</c>.</summary>
    public static readonly MessageKey AmbiguousColumn = new("Sql.Diagnostics.AmbiguousColumn");

    /// <summary>
    /// ET0006 — the column count as <c>{0}</c>, the value count as <c>{1}</c>.
    ///
    /// <para>⚠ These travel as NUMBERS and therefore follow the reader's culture (the ratified convention,
    /// gotcha #354). ⭐ C4b's invariant-string discipline does <b>not</b> apply here and the difference is
    /// structural: that existed to keep two representations of one sentence byte-identical, and this module has
    /// no English twin to stay equal to (gotcha #357). ⛔ Do not "make it consistent with C4b" — it would
    /// silently stop a count from being formatted for the reader.</para>
    ///
    /// <para>⚠ The English hedges with <c>column(s)</c> / <c>value(s)</c> rather than choosing a plural form, so
    /// this is translatable as it stands and is deliberately NOT added to the plural-case counter.</para>
    /// </summary>
    public static readonly MessageKey InsertCountMismatch = new("Sql.Diagnostics.InsertCountMismatch");

    /// <summary>ET0007 — the cursor's name as <c>{0}</c>.</summary>
    public static readonly MessageKey UnknownCursor = new("Sql.Diagnostics.UnknownCursor");

    /// <summary>
    /// ET0008 in a trigger — <b>no arguments</b>.
    ///
    /// <para>⛔⛔ <b>Two keys, not one key with the context as an argument.</b> The producer used to interpolate
    /// the word <i>"trigger"</i> or <i>"function"</i> into one sentence, and substituting a NOUN works in English
    /// and breaks in a language that inflects — the argument cannot know which case the sentence needs. This is
    /// the ratified C3 shape (<c>FirebirdConnectionMessages.UnsupportedServerUnknownVersion</c>): every sentence
    /// the engine can utter gets its own entry, and only genuine data travels as an argument.</para>
    /// </summary>
    public static readonly MessageKey SuspendInTrigger = new("Sql.Diagnostics.SuspendInTrigger");

    /// <inheritdoc cref="SuspendInTrigger"/>
    public static readonly MessageKey SuspendInFunction = new("Sql.Diagnostics.SuspendInFunction");
}
