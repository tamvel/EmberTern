namespace EmberTern.Core.Import;

/// <summary>
/// Where the bytes/text come from. Drives which options block applies (delimited vs spreadsheet) and which
/// <c>IImportProvider</c> the App resolves — never a branch inside the pipeline, which only ever sees
/// <see cref="RawRecord"/>.
/// </summary>
public enum ImportSourceKind
{
    /// <summary>Delimited text pasted from the clipboard. Not a second parser — the same delimited provider
    /// over a different text origin (design §1.5).</summary>
    Clipboard,

    /// <summary>Delimited text file, tab-delimited by default.</summary>
    Text,

    /// <summary>Delimited text file, <c>;</c> by default (PL locale) with auto-detection.</summary>
    Csv,

    /// <summary>OOXML workbook. Read via SAX only — the DOM costs 77× the heap (design R8, measured in I0).</summary>
    Xlsx,

    /// <summary>Legacy BIFF workbook. Deliberately outside the MVP (decision D2): the shipped OOXML reader
    /// physically cannot read it, and pretending otherwise is refused rather than faked.</summary>
    Xls,
}

/// <summary>
/// What the import does to the target. v1 writes <see cref="Insert"/> only; the enum exists from the first
/// etap so adding UPSERT/UPDATE/MERGE later is additive — it is already carried by
/// <see cref="ImportConfiguration"/> and therefore by a saved profile (design §9.5).
/// </summary>
public enum ImportMode
{
    Insert,
}

/// <summary>
/// How the import finalizes its transaction. Deliberately mirrors <c>ScriptTransactionMode</c>'s vocabulary —
/// the Script Executor already taught the user what these words mean, and import is the same kind of
/// operation on the same lane.
/// </summary>
public enum ImportTransactionMode
{
    /// <summary>DEFAULT (decision D3) — run every row, leave the transaction OPEN, let the user review the
    /// report and then Commit or Rollback. Hard rule #3: auto-<em>begin</em> exists, auto-<em>commit</em>
    /// never does.</summary>
    Manual,

    /// <summary>Commit when nothing failed, else roll back. One decision, taken up front.</summary>
    AutoCommitOnSuccess,

    /// <summary>Commit every N rows. <b>NOT atomic</b> — a committed batch stays applied even if a later one
    /// fails, which is the whole trade-off and is surfaced where the mode is chosen (§0.5), never hidden.
    /// I0 measured commit frequency as nearly free, so this mode costs atomicity and nothing else.</summary>
    Batched,
}

/// <summary>
/// What happens when a row cannot be written. Measured in I0 to map 1:1 onto the driver's batch
/// <c>MultiError</c> flag, so the policy the user picks is enforced by the server round-trip itself rather
/// than re-implemented client-side: <see cref="StopOnFirstError"/> ⇒ <c>MultiError=false</c> (the batch stops
/// AT the offending row), <see cref="SkipInvalidRows"/> ⇒ <c>MultiError=true</c> (every failing index is
/// reported).
/// </summary>
public enum ImportErrorPolicy
{
    /// <summary>DEFAULT (decision D4).</summary>
    StopOnFirstError,
    SkipInvalidRows,
}

/// <summary>
/// Whether the target already exists. A new table is created on the <b>Ddl lane</b> and committed BEFORE any
/// row is written, because a Firebird transaction cannot use an object whose DDL it has not committed
/// (gotcha #213) — which is also why Rollback cannot remove it, a fact the UI states out loud (§0.5).
/// </summary>
public enum ImportTargetKind
{
    ExistingTable,
    NewTable,
}

/// <summary>
/// How a source field ended up paired with a target column. Presentation reuses the debugger's ratified
/// <c>ValueOrigin</c> language, because the underlying question is identical — "did the user choose this, or
/// did the tool?".
/// </summary>
public enum MappingOrigin
{
    /// <summary>Never matched, or explicitly skipped by the user.</summary>
    Unmapped,

    /// <summary>The user chose it. Any manual edit clears an automatic origin, so a marker can never describe
    /// a value the user has since replaced.</summary>
    Manual,

    /// <summary>Matched on a PROVABLE fact — equal (normalized) names. Rendered quietly.</summary>
    Restored,

    /// <summary>Paired by the sole-remaining-pair rule: exactly one unmatched column on each side. Rendered
    /// distinctly ("assumed") because it is the one automatic pairing that rests on position rather than
    /// identity — an accent, not a warning.</summary>
    Assumed,
}

/// <summary>
/// Which field order a bare date text is read in. Explicit and declared, never sniffed per value: <c>03.04.2026</c>
/// is 3 April or 4 March depending on this setting alone, and guessing it per row is exactly the silent
/// corruption §0.1 forbids.
/// </summary>
public enum DateFieldOrder
{
    /// <summary>Day-month-year (PL default).</summary>
    Dmy,
    Mdy,
    Ymd,

    /// <summary>ISO 8601 (<c>yyyy-MM-dd</c>) only — the separator setting is ignored.</summary>
    Iso,
}

/// <summary>
/// Which line terminator the delimited reader honours. <see cref="Auto"/> accepts all three (the reader is
/// terminator-agnostic by construction); the explicit values exist for a file whose embedded, quoted text
/// contains a lone CR or LF that must NOT end a record.
/// </summary>
public enum LineEndingMode
{
    Auto,
    Crlf,
    Lf,
    Cr,
}

/// <summary>
/// Why one value could not be written. <b>Structured, never a message</b> — Core holds no UI strings
/// (rule #6); App maps these to <c>UiStrings</c>, exactly as <c>ExportUnavailableReason</c> does.
/// <para>
/// The server-side kinds are grouped by what I0 actually measured: the GDS codes for truncation, numeric
/// overflow and transliteration are <em>indistinguishable by the leading code</em> (all <c>335544321</c> /
/// SQLSTATE 22000) and separate only on the SECOND element of the GDS vector, and a primary-key violation is
/// indistinguishable from a unique-index violation at any depth. The enum records that reality rather than
/// promising precision the engine does not give (§0.6). Building the actual code → kind map is etap I4's job.
/// </para>
/// </summary>
public enum ImportErrorKind
{
    /// <summary>No error — a written row.</summary>
    None = 0,

    // ── Client-side: the value never left the machine ────────────────────────────────────────────────
    /// <summary>The text is not a whole number under the declared culture.</summary>
    NotAnInteger,

    /// <summary>The text is not a decimal number under the declared culture (wrong decimal separator is the
    /// usual cause — and a guess here is forbidden, §0.1).</summary>
    NotANumber,

    /// <summary>The text is not a date/time under the declared field order and separators.</summary>
    NotADateTime,

    /// <summary>The text matches neither the true nor the false token list.</summary>
    NotABoolean,

    /// <summary>The value is longer than the target column and trimming was not enabled. Firebird rejects
    /// this itself (measured — it never truncates silently), so this kind exists to catch it BEFORE the round
    /// trip, not to substitute for the server.</summary>
    ValueTooLong,

    /// <summary>The target column is NOT NULL and has no default, and the value is null/absent.</summary>
    NullNotAllowed,

    /// <summary>⭐ The value carries a character the <b>connection</b> charset cannot represent. Measured in I0:
    /// the driver would otherwise write <c>?</c> with **no error at all**, even into a UTF8 column — the
    /// connection charset decides, not the column's. This kind is the reason validation is a §0 requirement
    /// rather than a nicety (design R1).</summary>
    NotRepresentableInConnectionCharset,

    /// <summary>An Excel error cell (<c>#N/A</c>, <c>#REF!</c>, …). Never imported as the literal text — that
    /// would put <c>"#N/A"</c> into a VARCHAR pretending to be data (design R20).</summary>
    SourceErrorValue,

    // ── Server-side: Firebird refused the row ────────────────────────────────────────────────────────
    /// <summary>NOT NULL violation reported by the engine (the row got past local validation — e.g. a trigger
    /// nulled the value).</summary>
    ServerNullViolation,

    /// <summary>Unique-index violation. Deliberately NOT split into "primary key" vs "unique": I0 measured
    /// both as GDS <c>335544665</c> with no distinguishing code, so claiming which one it was would be
    /// inventing information.</summary>
    ServerUniqueViolation,

    /// <summary>CHECK constraint (or a domain's CHECK) violation.</summary>
    ServerCheckViolation,

    /// <summary>Foreign-key violation — the referenced row does not exist.</summary>
    ServerForeignKeyViolation,

    /// <summary>String truncation reported by the engine.</summary>
    ServerStringTruncation,

    /// <summary>Numeric value out of range / scale overflow reported by the engine.</summary>
    ServerNumericOverflow,

    /// <summary>The engine could not transliterate the value into the column's charset. Distinct from
    /// <see cref="NotRepresentableInConnectionCharset"/>: that one is caught locally before sending, this one
    /// is Firebird refusing (measured: UTF8 connection → WIN1250 column).</summary>
    ServerTransliterationFailed,

    /// <summary>The engine refused the row for a reason this build does not classify. Carries the raw server
    /// message so the user is never left with less information than the server gave — an honest bucket, not a
    /// swallowed error.</summary>
    ServerError,
}
