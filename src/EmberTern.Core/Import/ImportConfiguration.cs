using System;
using System.Collections.Generic;

namespace EmberTern.Core.Import;

/// <summary>
/// Where the data is to be read from — the LOCATION, never the content.
/// <para>
/// A path, not a handle: a saved configuration must be able to say "that file is gone" without opening
/// anything (design §4.8.5), which is what <c>IImportSource.StillExists</c> answers. Clipboard configurations
/// carry no path at all — the text is fetched fresh at read time, because clipboard content is not a user
/// decision and has no business in a stored profile.
/// </para>
/// </summary>
public sealed record SourceDescriptor
{
    public ImportSourceKind Kind { get; init; } = ImportSourceKind.Csv;

    /// <summary>Full path for a file source; <c>null</c> for <see cref="ImportSourceKind.Clipboard"/>.</summary>
    public string? Path { get; init; }

    public static SourceDescriptor File(ImportSourceKind kind, string path) => new() { Kind = kind, Path = path };

    public static SourceDescriptor Clipboard() => new() { Kind = ImportSourceKind.Clipboard, Path = null };

    /// <summary>True when this descriptor names a file (so the „does it still exist?" question applies).</summary>
    public bool IsFile => Kind != ImportSourceKind.Clipboard;
}

/// <summary>One column of a table this import is going to CREATE. A serializable subset of what
/// <c>DdlGenerator.BuildCreateTable</c> needs, converted to <c>FieldDefinition</c>/<c>TableSpec</c> only at
/// DDL-generation time.
/// <para>
/// <b>Why not persist <c>TableSpec</c> directly:</b> <c>TableSpec</c> is a builder INPUT with a get-only
/// collection — not a persistence shape. Keeping a small serializable definition here and converting on
/// demand is correct layering, not duplication; the DDL itself still comes from the one shared generator
/// (design §5).
/// </para>
/// <para>
/// The evidence behind an inferred type (how many rows were sampled, how many matched) is deliberately NOT
/// here: it is a fact read from the world, not a user decision, so §4.8.2 keeps it out of the profile. The
/// TYPE is here because the user can edit it.
/// </para>
/// </summary>
public sealed record ImportColumnDefinition
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Base Firebird type — <c>VARCHAR</c>, <c>INTEGER</c>, <c>NUMERIC</c>, <c>DATE</c>, …</summary>
    public string BasicType { get; init; } = "VARCHAR";

    /// <summary>Length for CHAR/VARCHAR, precision for NUMERIC/DECIMAL (see <c>FieldTypeRules.UsesSize</c>).</summary>
    public int? Size { get; init; }

    /// <summary>Scale for NUMERIC/DECIMAL.</summary>
    public int? Scale { get; init; }

    /// <summary>Sub type for BLOB (0 binary / 1 text).</summary>
    public int? BlobSubType { get; init; }

    public bool NotNull { get; init; }
}

/// <summary>Where the data is going.</summary>
public sealed record TargetDescriptor
{
    public ImportTargetKind Kind { get; init; } = ImportTargetKind.ExistingTable;

    public string TableName { get; init; } = string.Empty;

    /// <summary>Set only for <see cref="ImportTargetKind.NewTable"/> — the columns to create, in order.</summary>
    public IReadOnlyList<ImportColumnDefinition> NewTableColumns { get; init; } =
        Array.Empty<ImportColumnDefinition>();

    public static TargetDescriptor Existing(string tableName)
        => new() { Kind = ImportTargetKind.ExistingTable, TableName = tableName };

    public static TargetDescriptor New(string tableName, IReadOnlyList<ImportColumnDefinition> columns)
        => new() { Kind = ImportTargetKind.NewTable, TableName = tableName, NewTableColumns = columns };
}

/// <summary>
/// One target column paired with one source field.
/// <para>
/// ⭐ <b>Identity is the NAME where a name exists</b> (design §4.8.5 point 1). A purely positional mapping
/// would silently re-route data the moment a column moves in the source file — the worst class of defect this
/// project recognises (§0.1) — and fixing it later would mean changing the model, the stored format AND the
/// UI at once. <see cref="SourceFieldIndex"/> is kept as the resolved position (and is the ONLY identity for a
/// headerless source), but <see cref="SourceFieldName"/> wins whenever it can be matched.
/// </para>
/// </summary>
public sealed record ColumnMapping
{
    /// <summary>Target column, catalog-cased.</summary>
    public string TargetColumnName { get; init; } = string.Empty;

    /// <summary>Source field name, or <c>null</c> when the source has no header (position is then the only
    /// identity available) or when nothing is mapped.</summary>
    public string? SourceFieldName { get; init; }

    /// <summary>0-based source field position, or <c>-1</c> when unmapped.</summary>
    public int SourceFieldIndex { get; init; } = -1;

    /// <summary>True when the user deliberately excluded this target column. Distinct from "never matched":
    /// a skip is a decision and must survive a re-read of the source.</summary>
    public bool IsSkipped { get; init; }

    public MappingOrigin Origin { get; init; } = MappingOrigin.Unmapped;

    /// <summary>True when this mapping will actually carry a value.</summary>
    public bool IsMapped => !IsSkipped && SourceFieldIndex >= 0;

    public static ColumnMapping Unmapped(string targetColumnName)
        => new() { TargetColumnName = targetColumnName, Origin = MappingOrigin.Unmapped };

    public static ColumnMapping Skipped(string targetColumnName)
        => new() { TargetColumnName = targetColumnName, IsSkipped = true, Origin = MappingOrigin.Manual };
}

/// <summary>
/// The remaining yes/no decisions. Each one changes what happens to the user's data, which is why every one of
/// them defaults to the conservative answer.
/// </summary>
public sealed record ImportBehaviorOptions
{
    /// <summary>Run <c>DELETE FROM &lt;target&gt;</c> before importing, in the SAME working transaction, so it
    /// is rolled back together with the import (decision D5). Never a <c>TRUNCATE</c>-like shortcut.</summary>
    public bool EmptyTargetBeforeImport { get; init; }

    /// <summary>Shorten a value that exceeds the target column instead of failing the row.
    /// <para>
    /// I0 measured that Firebird REJECTS an over-long string and never truncates on its own, so this option is
    /// purely the user's convenience — not a guard against engine behaviour. When enabled, every shortened row
    /// is still reported as a warning carrying the ORIGINAL value (§0.2).
    /// </para>
    /// </summary>
    public bool TrimTooLongValues { get; init; }

    /// <summary>Treat a blank spreadsheet cell as SQL NULL rather than as an empty string.
    /// <para>
    /// This is the SPREADSHEET half of the "empty means NULL" question — a blank cell carries no literal at
    /// all. The TEXT half is <c>DelimitedOptions.NullToken</c>. Two settings, because they answer the question
    /// for two different source shapes; one owner each.
    /// </para>
    /// </summary>
    public bool TreatEmptyAsNull { get; init; } = true;

    /// <summary>Import an Excel error cell (<c>#N/A</c>, <c>#REF!</c>, …) as NULL. Default <c>false</c> ⇒ the
    /// row is an ERROR, because the alternative is writing the literal text <c>"#N/A"</c> into a column and
    /// calling it data (design R20).</summary>
    public bool ExcelErrorCellsAsNull { get; init; }

    /// <summary>Drop a table this import created if the import then fails. Only meaningful for
    /// <see cref="ImportTargetKind.NewTable"/>, and only because the CREATE had to be committed on the Ddl lane
    /// before any row could be written (gotcha #213) — so Rollback cannot undo it (§0.5).</summary>
    public bool DropTableOnFailure { get; init; }

    // Note: whitespace trimming is NOT here. It is a property of reading a text field, so it lives on
    // DelimitedOptions.TrimWhitespace — the same place the Format section shows it. One question, one owner.
}

/// <summary>
/// ⭐ <b>THE single representation of every decision the user makes about an import.</b>
/// <para>
/// This one record is simultaneously the working surface's state, the pipeline's input and a saved profile's
/// payload. That identity is the whole design (§4.8.1): saving a profile is serializing this, loading one is
/// assigning it, and running an import is passing it to <c>ImportPipeline</c>. <b>There is no second
/// representation</b> — which is why named profiles can arrive later (etap I11) as pure UI over an existing
/// store, with no model or surface rebuild.
/// </para>
/// <para>
/// <b>What is deliberately absent:</b> the data itself, clipboard content, the resolved <c>SourceSchema</c> and
/// the target's <c>ColumnSpec</c>s (facts read from the world, re-read on every load — that re-read is exactly
/// the mechanism that lets a stale profile be caught, §4.8.5), credentials, the connection id (that is profile
/// METADATA, see <see cref="ImportProfile"/>), and every counter or timing.
/// </para>
/// <para>
/// <b>Adding a field:</b> additive only, and it MUST take part in the App's
/// <c>BuildConfiguration</c>/<c>ApplyConfiguration</c> round trip — a reflection test fails the build
/// otherwise (§4.8.6). Bump <see cref="Version"/> only when the MEANING of an existing field changes; a new
/// field is simply absent in an older file and takes its default. <b>Never</b> bump the settings.dat container
/// version for this: that trips the downgrade protection and an older build would refuse the whole file.
/// </para>
/// </summary>
public sealed record ImportConfiguration
{
    /// <summary>Schema version of THIS record (not of settings.dat). v1 = initial shape.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public SourceDescriptor Source { get; init; } = new();

    /// <summary>Set for a delimited source (CSV / TXT / clipboard); <c>null</c> for a spreadsheet. Exactly one
    /// of <see cref="Delimited"/> / <see cref="Spreadsheet"/> is expected to be non-null — see
    /// <see cref="MatchesSourceKind"/>.</summary>
    public DelimitedOptions? Delimited { get; init; } = new();

    /// <summary>Set for a spreadsheet source; <c>null</c> for delimited text.</summary>
    public SpreadsheetOptions? Spreadsheet { get; init; }

    public ImportCultureOptions Culture { get; init; } = new();

    public TargetDescriptor Target { get; init; } = new();

    /// <summary>One entry per target column the user has considered, in target order.</summary>
    public IReadOnlyList<ColumnMapping> Mapping { get; init; } = Array.Empty<ColumnMapping>();

    public ImportMode Mode { get; init; } = ImportMode.Insert;

    public ImportTransactionMode Transaction { get; init; } = ImportTransactionMode.Manual;

    /// <summary>Rows per commit in <see cref="ImportTransactionMode.Batched"/>. Default measured in I0: commit
    /// frequency is nearly free, so this is chosen for report readability, not throughput.</summary>
    public int CommitEveryRows { get; init; } = DefaultCommitEveryRows;

    /// <summary>Rows per server round trip. Default measured in I0 — the optimum sits at 250–1 000 and
    /// degrades sharply above 2 000.</summary>
    public int BatchSize { get; init; } = DefaultBatchSize;

    public ImportErrorPolicy ErrorPolicy { get; init; } = ImportErrorPolicy.StopOnFirstError;

    public ImportBehaviorOptions Behavior { get; init; } = new();

    /// <summary>I0-measured optimum (§4.5).</summary>
    public const int DefaultBatchSize = 500;

    /// <summary>I0-measured: commit cost is negligible, so this serves readability (§4.5).</summary>
    public const int DefaultCommitEveryRows = 10_000;

    /// <summary>True when the options block present matches <see cref="SourceDescriptor.Kind"/>. Not enforced
    /// by the type system (a record cannot express "exactly one of these two"), so the readiness evaluation
    /// asks — and reports it as a blocking item rather than letting the reader meet a null.</summary>
    public bool MatchesSourceKind => Source.Kind switch
    {
        ImportSourceKind.Xlsx or ImportSourceKind.Xls => Spreadsheet is not null,
        _ => Delimited is not null,
    };

    /// <summary>The mappings that will actually carry a value, in target order.</summary>
    public IEnumerable<ColumnMapping> MappedColumns()
    {
        foreach (var m in Mapping)
        {
            if (m.IsMapped) yield return m;
        }
    }

    /// <summary>A configuration for a fresh, unconfigured surface.</summary>
    public static ImportConfiguration Empty { get; } = new();
}
