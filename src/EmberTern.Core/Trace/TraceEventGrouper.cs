using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Trace;

/// <summary>
/// Turns a flat <see cref="TraceEvent"/> stream into the module's diagnostic views.
/// Priority order (per the design): <see cref="GroupByTransaction"/> FIRST — a
/// Firebird transaction is the unit that maps to one ERP business operation
/// ("issue an invoice"); then <see cref="GroupByFingerprint"/> (redundant/duplicate
/// call detection with aggregates); then <see cref="WithCallHierarchy"/> (a
/// conservative reconstruction — transaction grouping matters more than full depth).
/// Pure; no UI, no driver.
/// </summary>
public static class TraceEventGrouper
{
    /// <summary>Groups events by <see cref="TraceEvent.TransactionId"/>, preserving the order in
    /// which each transaction first appears (business operations happen in time order) and each
    /// event's order within its group. Events with no transaction id (e.g. <c>TRACE_INIT</c>)
    /// form a trailing null-keyed group so nothing is lost.</summary>
    public static IReadOnlyList<TraceTransactionGroup> GroupByTransaction(IEnumerable<TraceEvent> events)
    {
        var order = new List<long?>();
        var buckets = new Dictionary<long, List<TraceEvent>>();
        List<TraceEvent>? nullBucket = null;
        foreach (var e in events)
        {
            if (e.TransactionId is { } tx)
            {
                if (!buckets.TryGetValue(tx, out var list))
                {
                    buckets[tx] = list = new List<TraceEvent>();
                    order.Add(tx);
                }
                list.Add(e);
            }
            else
            {
                if (nullBucket is null) { nullBucket = new List<TraceEvent>(); order.Add(null); }
                nullBucket.Add(e);
            }
        }
        return order
            .Select(tx => new TraceTransactionGroup(tx, tx is { } t ? buckets[t] : nullBucket!))
            .ToList();
    }

    /// <summary>Groups SQL statement events by their <see cref="TraceStatementFingerprinter"/>
    /// fingerprint (identical query regardless of params/whitespace/formatting), with the
    /// Count / Total / Average / Max duration aggregates. Ordered most-expensive-first
    /// (TotalDuration desc, then Count desc) so the worst offender and the 300×-redundant call
    /// surface at the top. Non-statement / SQL-less events are excluded (routine dedup by name
    /// is a later add).</summary>
    public static IReadOnlyList<TraceFingerprintGroup> GroupByFingerprint(IEnumerable<TraceEvent> events)
    {
        var order = new List<string>();
        var buckets = new Dictionary<string, List<TraceEvent>>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (string.IsNullOrEmpty(e.Sql)) continue;
            var fp = TraceStatementFingerprinter.Fingerprint(e.Sql);
            if (fp.Length == 0) continue;
            if (!buckets.TryGetValue(fp, out var list))
            {
                buckets[fp] = list = new List<TraceEvent>();
                order.Add(fp);
            }
            list.Add(e);
        }

        return order
            .Select(fp => new TraceFingerprintGroup(fp, buckets[fp]))
            .OrderByDescending(g => g.TotalDuration)
            .ThenByDescending(g => g.Count)
            .ToList();
    }

    /// <summary>Stamps <see cref="TraceEvent.Fingerprint"/> on every SQL statement event (returns new
    /// records; the input is not mutated). For display/selection; grouping uses the fingerprinter
    /// directly and does not require this.</summary>
    public static IReadOnlyList<TraceEvent> WithFingerprints(IEnumerable<TraceEvent> events)
        => events
            .Select(e => string.IsNullOrEmpty(e.Sql)
                ? e
                : e with { Fingerprint = TraceStatementFingerprinter.Fingerprint(e.Sql) })
            .ToList();

    /// <summary>
    /// Assigns a conservative call hierarchy (<see cref="TraceEvent.ParentEventId"/> +
    /// <see cref="TraceEvent.Depth"/>) so the reverse-engineering view can show which routines
    /// belong to which statement. Heuristic, and deliberately shallow: within one execution
    /// context (<see cref="TraceEvent.ContextToken"/>, ordered), the most recent Statement is the
    /// current parent; the routine events (Procedure/Function/Trigger) that follow — until the next
    /// Statement or a context break — become its direct children (Depth 1). This lives in the
    /// GROUPER, not the parser (the parser stays heuristic-free). True nested depth (proc→func→…)
    /// needs the pre-fold <c>*_START</c>/<c>*_FINISH</c> windows and is a later enhancement.
    /// </summary>
    public static IReadOnlyList<TraceEvent> WithCallHierarchy(IEnumerable<TraceEvent> events)
    {
        var currentStatement = new Dictionary<string, long>(StringComparer.Ordinal);
        const string NoToken = "\0";

        return events.Select(e =>
        {
            var token = e.ContextToken ?? NoToken;
            switch (e.Kind)
            {
                case TraceEventKind.Statement:
                    currentStatement[token] = e.Id; // becomes the parent for the routines that follow
                    return e with { ParentEventId = null, Depth = 0 };

                case TraceEventKind.Procedure:
                case TraceEventKind.Function:
                case TraceEventKind.Trigger:
                    return currentStatement.TryGetValue(token, out var parentId)
                        ? e with { ParentEventId = parentId, Depth = 1 }
                        : e with { ParentEventId = null, Depth = 0 };

                default: // System / Connection / Transaction break the current context
                    currentStatement.Remove(token);
                    return e with { ParentEventId = null, Depth = 0 };
            }
        }).ToList();
    }
}

/// <summary>One transaction's worth of events — the ERP-business-operation unit.</summary>
public sealed record TraceTransactionGroup
{
    public TraceTransactionGroup(long? transactionId, IReadOnlyList<TraceEvent> events)
    {
        TransactionId = transactionId;
        Events = events;
    }

    public long? TransactionId { get; }
    public IReadOnlyList<TraceEvent> Events { get; }

    public int EventCount => Events.Count;
    public int StatementCount => Events.Count(e => e.Kind == TraceEventKind.Statement);
    public TimeSpan TotalDuration => Events.Aggregate(TimeSpan.Zero, (a, e) => a + (e.Duration ?? TimeSpan.Zero));
    public DateTimeOffset StartTime => Events.Count == 0 ? default : Events.Min(e => e.StartTime);
    public DateTimeOffset EndTime => Events.Count == 0 ? default : Events.Max(e => e.SpanEnd);

    /// <summary>Wall-clock span of the whole operation (first start → last end).</summary>
    public TimeSpan Span => Events.Count == 0 ? TimeSpan.Zero : EndTime - StartTime;
}

/// <summary>Aggregated stats for one statement fingerprint — the redundant/duplicate-call view.</summary>
public sealed record TraceFingerprintGroup
{
    public TraceFingerprintGroup(string fingerprint, IReadOnlyList<TraceEvent> events)
    {
        Fingerprint = fingerprint;
        Events = events;
    }

    public string Fingerprint { get; }
    public IReadOnlyList<TraceEvent> Events { get; }

    /// <summary>The SQL of the first member — shown as the group's representative text.</summary>
    public string RepresentativeSql => Events.Count > 0 ? Events[0].Sql ?? string.Empty : string.Empty;

    public int Count => Events.Count;
    public TimeSpan TotalDuration => Events.Aggregate(TimeSpan.Zero, (a, e) => a + (e.Duration ?? TimeSpan.Zero));
    public TimeSpan AverageDuration => Count == 0 ? TimeSpan.Zero : TotalDuration / Count;
    public TimeSpan MaxDuration => Events.Count == 0 ? TimeSpan.Zero : Events.Max(e => e.Duration ?? TimeSpan.Zero);
}
