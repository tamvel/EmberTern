using System.Collections.Generic;

namespace EmberTern.Core.Performance;

/// <summary>Optional per-node optimizer estimates. Firebird 3/4/5 explained plans carry
/// NO cost/cardinality annotations (verified) — these are Firebird 6+ only, so both are
/// null on every currently supported engine. Present so Phase-3/V3 can populate them
/// without changing the node shape.</summary>
public sealed record PlanNodeMetrics(double? Cardinality, double? Cost);

/// <summary>One node in a parsed execution plan tree.</summary>
public sealed record PlanNode
{
    public required AccessMethod Method { get; init; }

    /// <summary>Table name for table-access nodes; null otherwise.</summary>
    public string? TableName { get; init; }

    /// <summary>Table alias when the plan exposed one; null otherwise.</summary>
    public string? Alias { get; init; }

    /// <summary>Index name for index-scan nodes; null otherwise.</summary>
    public string? IndexName { get; init; }

    /// <summary>Free-form scan/access qualifier as printed by the engine
    /// (e.g. "Full Scan", "Unique Scan", "Range Scan (full match)", "NATURAL").</summary>
    public string? Detail { get; init; }

    public PlanNodeMetrics? Metrics { get; init; }

    /// <summary>The raw plan text this node was parsed from — always retained so an
    /// unrecognized node still shows something faithful.</summary>
    public string RawText { get; init; } = string.Empty;

    public IReadOnlyList<PlanNode> Children { get; init; } = new List<PlanNode>();

    /// <summary>True for a full/sequential table scan (NATURAL). The single most
    /// diagnostic node kind — flagged in the plan tree.</summary>
    public bool IsSequentialScan => Method == AccessMethod.FullScan;

    /// <summary>True for a sub-query root — the "cost is spread, not concentrated" signal.
    ///
    /// <para>⛔⛔ <b>The literal is FIREBIRD's word and must never become a catalog entry (#356).</b> Until
    /// etap C7 this predicate existed twice: correctly here in Core as a literal, and incorrectly in the App
    /// as <c>UiStrings.PlanInsightSubquery</c> — a translatable resource matched against the engine's plan
    /// text. Translating that entry would have switched the App's sub-query summary off silently, invisibly in
    /// English, i.e. invisibly today. One owner now; both callers read this property.</para></summary>
    public bool IsSubqueryRoot
        => RawText.StartsWith("Sub-query", System.StringComparison.OrdinalIgnoreCase);
}
