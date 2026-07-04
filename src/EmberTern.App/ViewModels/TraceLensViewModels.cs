using System.Globalization;
using EmberTern.Core.Trace;

namespace EmberTern.App.ViewModels;

/// <summary>Which lens (navigator rail) is active. The lens NEVER reorders the chronological
/// grid — it selects / scrolls / highlights (and optionally narrows) into it.</summary>
public enum TraceGroupMode
{
    None,
    Transaction,
    Statement,
}

/// <summary>A transaction in the Transactions lens — an ERP business operation. Labelled by a
/// representative statement (the id is secondary, per the design), with aggregates.</summary>
public sealed class TraceTransactionLensItem
{
    public TraceTransactionLensItem(TraceTransactionGroup group)
    {
        TransactionId = group.TransactionId;
        EventCount = group.EventCount;
        StatementCount = group.StatementCount;
        Label = BuildLabel(group);
        SubText = string.Format(CultureInfo.InvariantCulture, "{0} · {1} stmt · {2} ms",
            TransactionId is { } tx ? "TRA " + tx : "no tx",
            StatementCount,
            (long)group.TotalDuration.TotalMilliseconds);
    }

    public long? TransactionId { get; }
    public string Label { get; }
    public string SubText { get; }
    public int EventCount { get; }
    public int StatementCount { get; }

    // Representative operation: first statement's SQL, else first routine name, else the id.
    private static string BuildLabel(TraceTransactionGroup g)
    {
        foreach (var e in g.Events)
            if (e.Kind == TraceEventKind.Statement && !string.IsNullOrWhiteSpace(e.Sql))
                return TraceEventRowViewModel.Elide(e.Sql);
        foreach (var e in g.Events)
            if (!string.IsNullOrWhiteSpace(e.ObjectName))
                return e.ObjectName!;
        return g.TransactionId is { } tx ? "Transaction " + tx : "System events";
    }
}

/// <summary>A fingerprint group in the Statements lens — identical queries collapsed, with the
/// Count / Total / Avg / Max aggregates (the redundant/slow-call diagnostic).</summary>
public sealed class TraceFingerprintLensItem
{
    public TraceFingerprintLensItem(TraceFingerprintGroup group)
    {
        Fingerprint = group.Fingerprint;
        Sql = TraceEventRowViewModel.Elide(group.RepresentativeSql);
        Count = group.Count;
        TotalText = Ms(group.TotalDuration);
        AverageText = Ms(group.AverageDuration);
        MaxText = Ms(group.MaxDuration);
        SubText = string.Format(CultureInfo.InvariantCulture, "×{0} · {1} ms total · {2} avg · {3} max",
            Count, Ms(group.TotalDuration), Ms(group.AverageDuration), Ms(group.MaxDuration));
    }

    public string Fingerprint { get; }
    public string Sql { get; }
    public int Count { get; }
    public string TotalText { get; }
    public string AverageText { get; }
    public string MaxText { get; }
    public string SubText { get; }

    private static string Ms(System.TimeSpan t) => ((long)t.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
}
