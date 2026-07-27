using System.Globalization;

namespace EmberTern.Core.Import;

/// <summary>
/// How loudly one finding should read. Mirrors <c>DebugPreflightSeverity</c>'s three levels on purpose: the
/// readiness strip is modelled on the debugger's pre-flight (design §3.2), and App maps this onto the shared
/// <c>MessageBanner</c> severity so the strip and the banner cannot paint the same idea differently (§9.3).
/// <para>
/// It lives in Core because a finding is produced by pure analysis; it carries no brush and no text.
/// </para>
/// </summary>
public enum ImportSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Which part of the working surface a finding belongs to. The readiness strip renders one chip per section
/// and a click jumps to it (§3.2), so this is the finding's own answer to "whose fault is this" — the view
/// never re-derives it.
/// </summary>
public enum ImportSection
{
    Source,
    Format,
    Target,
    Mapping,

    /// <summary>Transaction mode, the working transaction's state, and connection state — the things decided
    /// in the command bar rather than in a configuration section.</summary>
    Transaction,
}

/// <summary>
/// ⭐ Every structural finding the import module can produce, as a code — <b>never a message</b>. Core holds no
/// UI strings (rule #6); App maps these onto <c>UiStrings</c> exactly as it does for
/// <c>ExportUnavailableReason</c> and <c>Diagnostic</c>.
/// <para>
/// The numeric values are the published <c>IMP####</c> codes (see <see cref="ImportDiagnosticCodes.ToCode"/>),
/// so they are <b>stable</b>: renumbering one would rename a code the user may have seen in a report. Add new
/// members at the end.
/// </para>
/// <para>
/// One catalog serves both the mapping planner and the readiness evaluation, because they answer the same
/// questions about the same configuration — two catalogs would let "this column is required and unmapped"
/// exist twice and drift.
/// </para>
/// </summary>
public enum ImportDiagnosticCode
{
    None = 0,

    // ── Mapping (produced by ImportMappingPlanner) ───────────────────────────────────────────────────

    /// <summary>IMP0001 — a writable target column has no source field. A warning: leaving a column out is
    /// legal, and is often deliberate.</summary>
    TargetColumnNotMapped = 1,

    /// <summary>IMP0002 — a column that the INSERT cannot omit is unmapped: NOT NULL, no DEFAULT, not an
    /// identity and not computed. Blocking, because every single row would fail.</summary>
    RequiredColumnNotMapped = 2,

    /// <summary>IMP0003 — a source field is not used by any column. Informational, and the single most useful
    /// line in the panel: it is how "I forgot a column" becomes visible (§3.5).</summary>
    SourceFieldUnused = 3,

    /// <summary>IMP0004 — two or more source fields match one target column by name, so no automatic match is
    /// made. Ambiguity is handed back to the user rather than resolved by picking the first (§0).</summary>
    AmbiguousNameMatch = 4,

    /// <summary>IMP0005 — a previously mapped source field no longer exists in the source, so the mapping was
    /// dropped. Reported because §0.7 forbids letting a re-read quietly change what will be imported.</summary>
    MappingDropped = 5,

    /// <summary>IMP0006 — a <c>COMPUTED BY</c> column can never be written. Shown with its reason rather than
    /// hidden, so the column's absence is explained (§3.5).</summary>
    ColumnNotWritable = 6,

    /// <summary>IMP0007 — a <c>GENERATED ALWAYS</c> identity column is mapped, so the INSERT will carry
    /// <c>OVERRIDING SYSTEM VALUE</c>. An accent, not a fault — but never silent.</summary>
    IdentityOverrideRequired = 7,

    /// <summary>IMP0008 — the sole-remaining-pair rule fired: exactly one unmatched column and exactly one
    /// unused field were paired on position alone. Surfaced distinctly ("assumed") because it is the one
    /// automatic pairing that does not rest on identity (§4.7).</summary>
    PairingAssumed = 8,

    /// <summary>IMP0009 — the column's declared type has no faithful import path in this build (ARRAY, INT128,
    /// DECFLOAT, a <c>WITH TIME ZONE</c> type, an unusual BLOB sub type). Blocking when mapped.</summary>
    UnsupportedColumnType = 9,

    // ── Readiness (produced by ImportReadiness) ──────────────────────────────────────────────────────

    /// <summary>IMP0010 — no source chosen yet.</summary>
    NoSource = 10,

    /// <summary>IMP0011 — the configured file is gone. Answered without opening anything, which is why
    /// <c>SourceDescriptor</c> stores a path rather than a handle (§4.8.5).</summary>
    SourceMissing = 11,

    /// <summary>IMP0012 — the source exists but could not be read into a schema.</summary>
    SourceUnreadable = 12,

    /// <summary>IMP0013 — the source produced no fields at all (an empty file, or a delimiter that matches
    /// nothing).</summary>
    SourceHasNoFields = 13,

    /// <summary>IMP0014 — the configuration carries the wrong options block for its source kind (spreadsheet
    /// options for a CSV, or neither). A record cannot express "exactly one of these two", so it is checked
    /// here rather than met as a null by the reader.</summary>
    SourceOptionsMismatch = 14,

    /// <summary>IMP0015 — no target table chosen.</summary>
    NoTarget = 15,

    /// <summary>IMP0016 — the configured table is not in the catalog (renamed, dropped, or a profile written
    /// against a different database).</summary>
    TargetNotFound = 16,

    /// <summary>IMP0017 — target is "new table" but no columns are defined.</summary>
    NewTableHasNoColumns = 17,

    /// <summary>IMP0018 — ⚠ the new table will be CREATED and COMMITTED on the Ddl lane before any row is
    /// written, so Rollback cannot remove it. The single most important honest warning in the module
    /// (§0.5 / gotcha #213).</summary>
    NewTableWillBeCommitted = 18,

    /// <summary>IMP0019 — not one column is mapped, so there is nothing to import.</summary>
    NothingMapped = 19,

    /// <summary>IMP0020 — no active connection.</summary>
    NotConnected = 20,

    /// <summary>IMP0021 — the user's working transaction is already open. Blocking, mirroring the Script
    /// Executor's own run block so the application behaves the same way twice.</summary>
    // 21 was UserTransactionOpen — removed in I7.5 when Data Import got its own transaction, so what the
    // console has open stopped being this module's business. The number is not reused: a code is an
    // identity, and recycling one would make an old report mean something new.

    /// <summary>IMP0022 — the target has active BEFORE INSERT triggers, which can overwrite an imported value
    /// (design R6). Never changes what the import does; only makes the result explicable.</summary>
    TargetHasBeforeInsertTriggers = 22,

    /// <summary>IMP0023 — the target will be emptied first (<c>DELETE FROM</c>, same transaction, decision
    /// D5).</summary>
    TargetWillBeEmptied = 23,

    /// <summary>IMP0024 — <c>Batched</c> is <b>not atomic</b>: a committed batch stays applied even if a later
    /// one fails. Surfaced where the mode is chosen, never hidden (§0.5).</summary>
    BatchedIsNotAtomic = 24,

    /// <summary>IMP0025 — value trimming is on, so over-long text will be SHORTENED rather than refused
    /// (§0.2). Off by default; when on, it is stated up front and every shortened row is reported.</summary>
    TrimmingEnabled = 25,

    /// <summary>IMP0026 — a single transaction is about to hold a very large import open (design R4). About
    /// the transaction's LIFETIME, not the import's speed.</summary>
    LongTransactionRisk = 26,

    /// <summary>IMP0027 — ⭐ the sample contains characters the CONNECTION charset cannot represent. Measured
    /// in I0: those characters would be written as <c>?</c> with no error at all, even into a UTF8 column
    /// (design R1). The remedy is to connect in UTF8, and the strip says so.</summary>
    NotRepresentableInConnectionCharset = 27,

    /// <summary>IMP0028 — the name chosen for a NEW table already belongs to a table in this database.
    /// Blocking, and blocking <em>early</em> is the point: the <c>CREATE</c> runs on the Ddl lane before the
    /// first row, so without this the user would meet a raw Firebird error at the moment the import starts,
    /// having already been told everything was ready. §0 gives two options where the module would otherwise
    /// stumble — ask, or refuse with a reason — and this is the refusal.</summary>
    NewTableAlreadyExists = 28,
}

/// <summary>Renders an <see cref="ImportDiagnosticCode"/> as its published <c>IMP####</c> form.</summary>
public static class ImportDiagnosticCodes
{
    /// <summary>The user-visible code, e.g. <c>IMP0002</c>; empty for <see cref="ImportDiagnosticCode.None"/>.
    /// Derived from the enum value so the two can never disagree.</summary>
    public static string ToCode(this ImportDiagnosticCode code)
        => code == ImportDiagnosticCode.None
            ? string.Empty
            : "IMP" + ((int)code).ToString("D4", CultureInfo.InvariantCulture);
}

/// <summary>
/// One finding about a configuration — a code plus just enough structured detail for App to compose a sentence
/// without Core owning one.
/// </summary>
/// <param name="Code">What was found.</param>
/// <param name="Severity">How loudly it reads.</param>
/// <param name="Subject">The column or field it concerns, when it concerns exactly one; else <c>null</c>.</param>
/// <param name="Count">How many things it concerns, when the finding is a tally ("3 of 4 columns mapped");
/// else <c>null</c>. Numbers, not adjectives (§9.1 point 4).</param>
public sealed record ImportDiagnostic(
    ImportDiagnosticCode Code,
    ImportSeverity Severity,
    string? Subject = null,
    int? Count = null)
{
    /// <summary>The published <c>IMP####</c> code.</summary>
    public string CodeText => Code.ToCode();
}
