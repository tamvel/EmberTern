using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql;

namespace EmberTern.Core.Performance;

/// <summary>Everything a performance rule reasons over for one statement, assembled once by
/// <see cref="PerformanceContextBuilder"/>: the measured per-table access (Phase 2), the parsed
/// plan, the extracted predicates + their sargability, and the catalog slice. Rules read from
/// this — they do no I/O and no parsing. Measured-first: <see cref="Access"/> is the primary
/// evidence; the plan/predicates/catalog only explain it.</summary>
public sealed record PerformanceContext
{
    public required PerformanceCapture Capture { get; init; }

    public PlanTree? Plan { get; init; }

    /// <summary>Measured per-table reads, or null when reads weren't captured.</summary>
    public TableAccessProfile? Access { get; init; }

    public long RowsReturned { get; init; }

    /// <summary>True for a result-producing SELECT; false for DML / EXECUTE PROCEDURE / BLOCK.</summary>
    public bool HasResultSet { get; init; } = true;

    /// <summary>Rows changed (insert + update + delete) — the meaningful "output" of a non-result
    /// statement (from the MON$ delta), 0 for a SELECT.</summary>
    public long RowsChanged { get; init; }

    /// <summary>Total measured rows read (null when no reads).</summary>
    public long? RowsRead { get; init; }

    /// <summary>Rows read ÷ output rows (null when no reads / zero output).</summary>
    public double? Amplification { get; init; }

    /// <summary>The meaningful "output" of the statement — rows returned for a SELECT, rows
    /// changed for a DML/procedure — so read amplification and finding wording aren't framed
    /// around "returned 0" for a statement that did its work by changing rows.</summary>
    public long OutputRows => HasResultSet ? RowsReturned : RowsChanged;

    /// <summary>Infinitive for "reading N rows to {verb} M rows" — "return" or "change".</summary>
    public string OutputVerb => HasResultSet ? "return" : "change";

    /// <summary>Evidence label for <see cref="OutputRows"/> — "Rows returned" / "Rows changed".</summary>
    public string OutputRowsLabel => HasResultSet ? "Rows returned" : "Rows changed";

    public IReadOnlyList<QueryPredicate> Predicates { get; init; } = Array.Empty<QueryPredicate>();

    public IReadOnlyList<SargabilityVerdict> Sargability { get; init; } = Array.Empty<SargabilityVerdict>();

    public CatalogModel Catalog { get; init; } = CatalogModel.Empty;

    public bool HasReads => Access is not null && Access.Tables.Count > 0;

    public TableAccessStat? AccessForTable(string table)
        => Access?.Tables.FirstOrDefault(t => string.Equals(t.Table, table, StringComparison.OrdinalIgnoreCase));

    public TableCatalogInfo? CatalogForTable(string table) => Catalog.ForTable(table);

    /// <summary>Index names the plan used against <paramref name="table"/> (from index-access
    /// nodes) — lets a rule map measured index reads back to the actual index.</summary>
    public IEnumerable<string> PlanIndexesForTable(string table)
    {
        if (Plan is null)
        {
            yield break;
        }
        foreach (var node in Plan.EnumerateNodes())
        {
            if (!string.IsNullOrEmpty(node.IndexName)
                && node.TableName is { } t
                && string.Equals(t, table, StringComparison.OrdinalIgnoreCase))
            {
                yield return node.IndexName!;
            }
        }
    }

    /// <summary>Number of sub-query roots in the plan (the "spread cost" signal for R6).</summary>
    public int SubqueryCount
        => Plan?.Roots.Count(r => r.RawText.StartsWith("Sub-query", StringComparison.OrdinalIgnoreCase)) ?? 0;
}
