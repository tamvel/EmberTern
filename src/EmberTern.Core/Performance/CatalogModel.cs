using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Performance;

/// <summary>One index as the advisor needs to reason about it: the (ordered) segment
/// columns, uniqueness, whether it's usable, its selectivity, and whether it's an
/// expression / partial index. Produced by the Firebird layer, so it stays a plain DTO.</summary>
public sealed record IndexModel
{
    public required string Name { get; init; }

    /// <summary>Segment columns in position order — <see cref="LeadingColumn"/> is the one
    /// that decides whether the index can serve a predicate on that column.</summary>
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();

    public bool IsUnique { get; init; }

    public bool IsPrimary { get; init; }

    /// <summary>Inactive indexes cannot be used by the optimizer — the advisor must not
    /// treat one as covering a predicate.</summary>
    public bool IsInactive { get; init; }

    /// <summary>Index selectivity from <c>RDB$INDICES.RDB$STATISTICS</c> in [0, 1]; null when
    /// never computed (Firebird's -1 sentinel is normalized to null by the reader). Lower =
    /// more selective (fewer duplicate keys).</summary>
    public double? Selectivity { get; init; }

    /// <summary>Expression source for an expression index (e.g. <c>UPPER(NAME)</c>); null for
    /// a plain column index. A plain-column predicate cannot use an expression index.</summary>
    public string? Expression { get; init; }

    /// <summary>Partial-index condition (FB5 <c>RDB$CONDITION_SOURCE</c>); null for a full
    /// index. A partial index only covers rows matching its condition.</summary>
    public string? Condition { get; init; }

    /// <summary>The leading segment column, or null for an expression-only index.</summary>
    public string? LeadingColumn => Columns.Count > 0 ? Columns[0] : null;

    public bool IsExpression => !string.IsNullOrWhiteSpace(Expression);

    public bool IsPartial => !string.IsNullOrWhiteSpace(Condition);

    /// <summary>Usable by the optimizer as a plain-column index: active, and not an
    /// expression index (whose usability depends on the predicate matching the expression).</summary>
    public bool IsUsablePlainIndex => !IsInactive && !IsExpression;

    /// <summary>True when this index can serve a predicate on <paramref name="column"/>
    /// (active, plain, and <paramref name="column"/> is its leading segment). Case-insensitive.</summary>
    public bool CoversLeading(string column)
        => IsUsablePlainIndex
           && LeadingColumn is { } lead
           && string.Equals(lead, column, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Catalog facts for one table the query touched: an estimated row count (scale
/// context) plus its indexes. Plain DTO.</summary>
public sealed record TableCatalogInfo
{
    public required string Table { get; init; }

    /// <summary>Best-effort row-count estimate derived from index statistics (≈ 1 ÷ the
    /// smallest positive selectivity of a unique index — a unique key's selectivity is 1/N),
    /// so it costs NO table scan; null when no computed unique-index statistics are available
    /// (e.g. uninitialized stats — R5). Used for the "% of table scanned" scale context and the
    /// "table not tiny" gate.</summary>
    public long? RowCountEstimate { get; init; }

    public IReadOnlyList<IndexModel> Indexes { get; init; } = Array.Empty<IndexModel>();
}

/// <summary>The catalog slice the advisor needs — one <see cref="TableCatalogInfo"/> per table
/// referenced by the profiled statement. Produced by the Firebird catalog reader (which holds
/// all Fb* internally) and fed into the rule engine; also serves as the "catalog capture" (no
/// separate wrapper — nothing consumes an extra layer). Pure Core DTO.</summary>
public sealed record CatalogModel
{
    public IReadOnlyList<TableCatalogInfo> Tables { get; init; } = Array.Empty<TableCatalogInfo>();

    public static CatalogModel Empty { get; } = new();

    /// <summary>Case-insensitive table lookup; null when the table wasn't captured.</summary>
    public TableCatalogInfo? ForTable(string table)
        => Tables.FirstOrDefault(t => string.Equals(t.Table, table, StringComparison.OrdinalIgnoreCase));
}
