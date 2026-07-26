using System;
using System.Collections.Generic;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Import;

/// <summary>
/// One field the provider found in the source. A FACT about the world, not a user decision — which is why it
/// never enters <see cref="ImportConfiguration"/> and is re-read every time the source is opened (§4.8.2).
/// </summary>
/// <param name="Index">0-based position in the record.</param>
/// <param name="Name">Header name, or a generated positional label (<c>A</c>, <c>B</c>, …) when the source has
/// no header. A generated label is still a usable mapping key for a headerless source, but it is NOT an
/// identity that survives column reordering — which is why <see cref="HasRealName"/> exists.</param>
/// <param name="HasRealName"><c>true</c> when <see cref="Name"/> came from the source itself.</param>
public sealed record SourceField(int Index, string Name, bool HasRealName)
{
    /// <summary>Positional label for a headerless source: 0 → <c>A</c>, 25 → <c>Z</c>, 26 → <c>AA</c>. Matches
    /// the spreadsheet column labels the user already sees in Excel and in the source preview.</summary>
    public static string PositionalName(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

        var label = string.Empty;
        var n = index;
        while (true)
        {
            label = (char)('A' + n % 26) + label;
            n = n / 26 - 1;
            if (n < 0) break;
        }
        return label;
    }
}

/// <summary>The shape of a source as the provider read it, plus an optional row-count hint.</summary>
/// <param name="Fields">Fields in positional order.</param>
/// <param name="HasHeader">Whether the first record was consumed as a header.</param>
/// <param name="EstimatedRows">Advisory row count when the source can offer one cheaply (a spreadsheet's
/// declared dimension, a file's size/line estimate) — <c>null</c> when unknown. <b>A hint, never truth:</b> I0
/// measured that a workbook's dimension is present in Excel output and absent in programmatically written
/// files, so progress must survive not knowing (design R8).</param>
public sealed record SourceSchema(IReadOnlyList<SourceField> Fields, bool HasHeader, long? EstimatedRows)
{
    public static readonly SourceSchema Empty =
        new(Array.Empty<SourceField>(), false, null);

    /// <summary>Finds a field by source name, case-insensitively; <c>null</c> when absent.</summary>
    public SourceField? FindByName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var f in Fields)
        {
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
        }
        return null;
    }
}

/// <summary>
/// One record as the provider produced it — values still RAW (text for a delimited source, native cell values
/// for a spreadsheet). This is the single currency that makes the pipeline source-agnostic: past this point
/// nothing knows whether it is reading CSV, a workbook or the clipboard.
/// </summary>
/// <param name="SourceRowNumber">The row number to show the user. Taken from the source's OWN numbering (a
/// worksheet's <c>RowIndex</c>, a file's physical record number), never from a running counter — I0 measured
/// that spreadsheets simply omit empty rows, so a counter would make the error report point at the wrong row,
/// i.e. lie (§0.6).</param>
/// <param name="Values">Raw values, positionally aligned to <see cref="SourceSchema.Fields"/>. A short array is
/// legal (a ragged record) and is NOT padded here — the mapper decides what an absent field means.</param>
public sealed record RawRecord(int SourceRowNumber, object?[] Values)
{
    /// <summary>The raw value at <paramref name="index"/>, or <c>null</c> when the record does not reach it.</summary>
    public object? ValueAt(int index)
        => index >= 0 && index < Values.Length ? Values[index] : null;
}

/// <summary>
/// The resolved write target: its name and its columns as the catalog reports them. Read on the Metadata lane
/// and re-read whenever the target changes — a fact, so (like <see cref="SourceSchema"/>) it is not part of the
/// stored configuration.
/// <para>
/// Reuses <see cref="ColumnSpec"/> rather than defining a second column model: it already carries the type,
/// nullability, default, identity kind and computed flag — including the ALWAYS/BY DEFAULT distinction that
/// decides whether a generated INSERT needs <c>OVERRIDING SYSTEM VALUE</c>.
/// </para>
/// </summary>
/// <param name="TableName">Catalog-cased table name.</param>
/// <param name="Columns">Columns in catalog order.</param>
/// <param name="BeforeInsertTriggers">Names of the target's active BEFORE INSERT triggers. Surfaced because a
/// trigger can overwrite an imported value, and a user who does not know that cannot understand the result
/// (design R6) — never used to change what the import does.</param>
public sealed record ImportTarget(
    string TableName,
    IReadOnlyList<ColumnSpec> Columns,
    IReadOnlyList<string> BeforeInsertTriggers)
{
    public static ImportTarget Empty { get; } =
        new(string.Empty, Array.Empty<ColumnSpec>(), Array.Empty<string>());

    /// <summary>Finds a column by name, case-insensitively; <c>null</c> when absent.</summary>
    public ColumnSpec? FindColumn(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var c in Columns)
        {
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
        }
        return null;
    }

    /// <summary>True when the column cannot be written at all: a <c>COMPUTED BY</c> column. Firebird rejects an
    /// INSERT naming one, so it is blocked in the mapping WITH the reason shown, rather than hidden (§3.5).</summary>
    public static bool IsNeverWritable(ColumnSpec column) => column.IsComputed;

    /// <summary>True when naming the column in an INSERT requires <c>OVERRIDING SYSTEM VALUE</c>.</summary>
    public static bool RequiresOverridingSystemValue(ColumnSpec column)
        => column.Identity == IdentityKind.Always;
}

/// <summary>
/// One row ready for the writer: values already converted and validated, positionally aligned to the writer's
/// column list. Carries its <see cref="SourceRowNumber"/> so the error report can name the row the user sees —
/// the writer never has to know how rows are numbered.
/// </summary>
public sealed record ImportRow(int SourceRowNumber, object?[] Values);

/// <summary>
/// Why one row could not be written, in terms the report can render.
/// </summary>
/// <param name="SourceRowNumber">The row as the user sees it in the source.</param>
/// <param name="Kind">Structured cause — Core holds no UI strings (rule #6); App maps this to <c>UiStrings</c>.</param>
/// <param name="ColumnName">Target column at fault, or <c>null</c> when the failure is row-wide (e.g. a
/// foreign-key violation).</param>
/// <param name="RawValue">The value as it appeared in the SOURCE, kept verbatim so the report shows what the
/// user actually has rather than a post-conversion approximation (§0.2 / §0.6).</param>
/// <param name="ServerMessage">Raw server text when the engine refused the row, else <c>null</c>. Kept so the
/// user is never left with less information than Firebird gave.</param>
/// <param name="Limit">Declared limit when the engine reported one (I0 measured that the truncation GDS vector
/// carries both the limit and the actual length as numbers — so "26 chars, limit 20" comes from the server,
/// never from parsing its message text).</param>
/// <param name="ActualLength">Actual length when the engine reported one.</param>
public sealed record ImportRowError(
    int SourceRowNumber,
    ImportErrorKind Kind,
    string? ColumnName = null,
    string? RawValue = null,
    string? ServerMessage = null,
    int? Limit = null,
    int? ActualLength = null);

/// <summary>Live counters for the progress bar. Reported on a throttle, never per row.</summary>
public sealed record ImportProgress(long RowsRead, long RowsWritten, long RowsFailed, TimeSpan Elapsed);

/// <summary>Result of one element of a flushed batch, positionally aligned to the order rows were queued.
/// <para>
/// The alignment is not an assumption: I0 measured that the driver returns one result per queued row, in order,
/// with the failing element at the same index it was added (design §2.3). <c>ImportPipeline</c> maps that index
/// back to a source row number — the report never sees a batch index.
/// </para>
/// </summary>
public sealed record ImportBatchItemResult(
    bool IsSuccess,
    int RecordsAffected = 0,
    ImportErrorKind Kind = ImportErrorKind.None,
    string? ServerMessage = null,
    int? Limit = null,
    int? ActualLength = null)
{
    public static readonly ImportBatchItemResult Success = new(true, 1);

    public static ImportBatchItemResult Failure(
        ImportErrorKind kind, string? serverMessage = null, int? limit = null, int? actualLength = null)
        => new(false, 0, kind, serverMessage, limit, actualLength);
}

/// <summary>What a writer did over its whole lifetime, returned by <c>CompleteAsync</c>.</summary>
/// <param name="RowsWritten">Rows the target accepted.</param>
/// <param name="RowsFailed">Rows the target refused.</param>
/// <param name="TransactionLeftOpen">True when the caller still has to Commit or Rollback — the honest answer
/// that keeps the report from claiming success for work that is not yet persisted (§0.6).</param>
public sealed record ImportWriteSummary(long RowsWritten, long RowsFailed, bool TransactionLeftOpen);

/// <summary>
/// The aggregate outcome of one import run.
/// </summary>
/// <param name="RowsRead">Records the provider produced (before mapping/conversion).</param>
/// <param name="RowsWritten">Rows the target accepted.</param>
/// <param name="RowsFailed">Rows rejected locally or by the engine.</param>
/// <param name="Errors">The collected errors, capped at <see cref="MaxCollectedErrors"/>.</param>
/// <param name="ErrorsTruncated">True when more errors occurred than were kept — so the report can say so
/// instead of implying the list is complete.</param>
/// <param name="TransactionLeftOpen">True when the user still has to Commit or Rollback.</param>
/// <param name="Cancelled">True when the user cancelled; rows already written stay in the open transaction and
/// the report says exactly that (§2.3).</param>
/// <param name="CreatedTable">Name of a table this run created and COMMITTED (Ddl lane), else <c>null</c> —
/// the one effect a Rollback cannot undo (§0.5).</param>
public sealed record ImportOutcome(
    long RowsRead,
    long RowsWritten,
    long RowsFailed,
    IReadOnlyList<ImportRowError> Errors,
    bool ErrorsTruncated,
    bool TransactionLeftOpen,
    bool Cancelled,
    string? CreatedTable)
{
    /// <summary>
    /// Rows that were WRITTEN but had a value shortened to fit, each carrying its ORIGINAL value.
    /// <para>
    /// §0.2 permits trimming only as an explicit choice, and only if every shortened row is still reported —
    /// so these are not errors (the row went in) and not silence (data was lost). They are their own list
    /// precisely so the report cannot fold them into either, and so <see cref="RowsFailed"/> never counts a row
    /// that actually succeeded.
    /// </para>
    /// <para>
    /// Their <see cref="ImportRowError.Kind"/> is <see cref="ImportErrorKind.ValueTooLong"/>, the same kind a
    /// REFUSED over-long value carries: the cause is identical, and which list the entry is in says what was
    /// done about it. Added in etap I3.
    /// </para>
    /// </summary>
    public IReadOnlyList<ImportRowError> Warnings { get; init; } = Array.Empty<ImportRowError>();

    /// <summary>True when more warnings occurred than were kept.</summary>
    public bool WarningsTruncated { get; init; }

    /// <summary>Cap on retained errors. A million-row import of a malformed file must not become a
    /// million-entry list in memory; the counters stay exact regardless. Applies to
    /// <see cref="Warnings"/> too.</summary>
    public const int MaxCollectedErrors = 1_000;

    public static ImportOutcome Nothing { get; } =
        new(0, 0, 0, Array.Empty<ImportRowError>(), false, false, false, null);
}
