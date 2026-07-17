using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Export;

/// <summary>One column of an export — mirrors the <c>QueryColumn</c> shape but keeps the export
/// framework free of any coupling to the query layer (adapters map <c>QueryColumn</c> → this).</summary>
public sealed record ExportColumn(string Name, Type ClrType);

/// <summary>
/// Which set of rows an operation acts on. Export is the first consumer, but this is deliberately
/// the general "what rows?" vocabulary for any future bulk data operation (bulk delete/update,
/// generate-script-for-these-rows, send-to-…) — keep it and the <see cref="ExportCapabilities"/>
/// gating reusable rather than boxed into export-only naming.
/// </summary>
public enum ExportScope
{
    /// <summary>The rows the grid currently shows (client-side filter + sort applied).</summary>
    CurrentView,

    /// <summary>The user's current selection. Only offered when the source can supply one
    /// (a single-select grid advertises this off — see <see cref="ExportCapabilities"/>).</summary>
    SelectedRows,

    /// <summary>The complete result. For a materialized-complete source this is the cached set;
    /// for a truncated preview / server-paged source it is re-fetched.</summary>
    AllRows,
}

/// <summary>The serialization + destination, chosen as one thing (there is no separate File/Clipboard
/// destination control — the format determines it): Excel / CSV / Text write a file, Clipboard copies
/// text, the SQL-Script family copies runnable DML.</summary>
public enum ExportFormat
{
    /// <summary>Excel workbook (.xlsx) — the headline format; typed cells; file-only (never clipboard).</summary>
    Xlsx,
    Csv,
    Text,
    Clipboard,

    /// <summary><c>INSERT INTO … VALUES (…)</c> per row. Unlike every format above, this one can be
    /// <b>unavailable for a given result</b> — it must prove which table the rows belong to first — which
    /// is what <see cref="FormatAvailability"/> exists for.</summary>
    InsertScript,

    /// <summary><c>UPDATE … SET … WHERE &lt;verified key&gt;</c> per row. Needs a key it has proven
    /// identifies exactly one row, so it is unavailable strictly more often than
    /// <see cref="InsertScript"/>.</summary>
    UpdateScript,
}

/// <summary>
/// Whether one format can run on one result, and if not, why. <see cref="ExportCapabilities"/> gates
/// <em>scopes</em>; this gates <em>formats</em>, and it is the difference between a greyed-out menu item
/// and a greyed-out menu item that <b>says why</b>.
/// <para>
/// Saying why is the whole design: a result that combines four tables cannot yield one INSERT, and the
/// alternative — emitting <c>INSERT INTO TABLE_NAME (…)</c> for the user to fix — generates code that is
/// known to be wrong, which the project's rules forbid twice over. Naming the actual obstacle teaches the
/// tool's model instead; it is also strictly more information than the placeholder conveys.
/// </para>
/// </summary>
public sealed record FormatAvailability
{
    private FormatAvailability(bool isAvailable, Sql.ExportUnavailableReason? reason)
    {
        IsAvailable = isAvailable;
        Reason = reason;
    }

    public static readonly FormatAvailability Available = new(true, null);

    public static FormatAvailability Unavailable(Sql.ExportUnavailableReason reason) => new(false, reason);

    public bool IsAvailable { get; }

    /// <summary>Non-null exactly when unavailable. A structured reason, never a message — App maps it to
    /// <c>UiStrings</c> (rule #1: Core has no UI strings).</summary>
    public Sql.ExportUnavailableReason? Reason { get; }
}

/// <summary>A row-count hint for a scope, shown up front in the dialog. <see cref="Count"/> null =
/// unknown (label the scope with no number rather than run a count query just to fill it);
/// <see cref="IsApproximate"/> renders the leading <c>~</c>.</summary>
public readonly record struct RowEstimate(long? Count, bool IsApproximate)
{
    public static readonly RowEstimate Unknown = new(null, false);

    public static RowEstimate Exact(long count) => new(count, false);

    public static RowEstimate Approximate(long count) => new(count, true);
}

/// <summary>What a data source can export: the supported scopes, a base file name hint (no extension),
/// and a per-scope row estimate for the dialog. The dialog gates its scope options on
/// <see cref="Supports"/> — never assume every source supports every scope (Risk #8).</summary>
public sealed class ExportCapabilities
{
    private readonly IReadOnlyDictionary<ExportScope, RowEstimate> _estimates;

    public ExportCapabilities(
        IReadOnlyList<ExportScope> scopes,
        string defaultBaseFileName,
        IReadOnlyDictionary<ExportScope, RowEstimate> estimates)
    {
        Scopes = scopes;
        DefaultBaseFileName = defaultBaseFileName;
        _estimates = estimates;
    }

    public IReadOnlyList<ExportScope> Scopes { get; }

    /// <summary>Suggested file name without extension (e.g. an object name, or "query_result").</summary>
    public string DefaultBaseFileName { get; }

    public bool Supports(ExportScope scope) => Scopes.Contains(scope);

    public RowEstimate EstimateFor(ExportScope scope)
        => _estimates.TryGetValue(scope, out var e) ? e : RowEstimate.Unknown;
}

/// <summary>CSV / Text serialization options. Encoding (BOM) is chosen at the sink, not here —
/// it is a per-format file concern, not part of the row serialization.</summary>
public sealed record DelimitedTextOptions(char Delimiter, bool IncludeHeader, bool UseInvariantCulture);

/// <summary>A single export operation: what format, which rows, and the format's options.
/// <see cref="Delimited"/> is set for <see cref="ExportFormat.Csv"/> / <see cref="ExportFormat.Text"/>;
/// <see cref="IncludeHeader"/> carries the header choice for <see cref="ExportFormat.Clipboard"/>.</summary>
public sealed record ExportRequest
{
    public required ExportFormat Format { get; init; }
    public required ExportScope Scope { get; init; }
    public DelimitedTextOptions? Delimited { get; init; }
    public bool IncludeHeader { get; init; } = true;

    /// <summary>The proven target for <see cref="ExportFormat.InsertScript"/> /
    /// <see cref="ExportFormat.UpdateScript"/> — mirrors how <see cref="Delimited"/> carries the options
    /// CSV/Text require. The <em>caller</em> resolves it, because resolving needs the catalog and the
    /// export service has no business knowing about metadata; by the time a request names a SQL format,
    /// the proof already exists.</summary>
    public Sql.TargetResolution.Resolved? SqlTarget { get; init; }
}

/// <summary>The result of a completed export, returned to the caller so it can report
/// "Exported N rows to …" / "Copied N rows to the clipboard".</summary>
public sealed record ExportOutcome(ExportFormat Format, ExportScope Scope, long RowCount, string? FilePath);
