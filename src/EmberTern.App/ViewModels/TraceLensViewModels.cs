using System;
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

/// <summary>The session-bar quick filter chips. Fast diagnostic narrowing (All / Errors / Slow) —
/// not a filter builder. Composes with the text filter, hide-self, and the active lens.</summary>
public enum TraceQuickFilter
{
    All,
    Errors,
    Slow,
}

internal static class TraceFormat
{
    /// <summary>Human-readable duration: "0 ms" / "240 ms" / "4.8 s" / "1.2 min".</summary>
    public static string Ms(TimeSpan t)
    {
        var ms = t.TotalMilliseconds;
        if (ms < 1000) return string.Format(CultureInfo.CurrentCulture, UiStrings.TraceFormatMsFormat, (long)ms);
        if (ms < 60_000) return string.Format(CultureInfo.CurrentCulture, UiStrings.TraceFormatSecondsFormat, (ms / 1000).ToString("0.0", CultureInfo.InvariantCulture));
        return string.Format(CultureInfo.CurrentCulture, UiStrings.TraceFormatMinutesFormat, (ms / 60_000).ToString("0.0", CultureInfo.InvariantCulture));
    }
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
        SubText = string.Format(CultureInfo.InvariantCulture, UiStrings.TraceLensTransactionSummaryFormat,
            TransactionId is { } tx ? "TRA " + tx : UiStrings.TraceLensNoTransaction,
            StatementCount,
            TraceFormat.Ms(group.TotalDuration));
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
        return g.TransactionId is { } tx ? UiStrings.TraceLensTransactionPrefix + tx : UiStrings.TraceLensSystemEvents;
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
        CountText = "×" + Count.ToString(CultureInfo.InvariantCulture);
        MetricsText = string.Format(CultureInfo.InvariantCulture, UiStrings.TraceLensDurationSummaryFormat,
            TraceFormat.Ms(group.TotalDuration),
            TraceFormat.Ms(group.AverageDuration),
            TraceFormat.Ms(group.MaxDuration));
    }

    public string Fingerprint { get; }
    public string Sql { get; }
    public int Count { get; }

    /// <summary>The prominent "×N" call-count badge.</summary>
    public string CountText { get; }

    /// <summary>"4.8 s total · 240 ms avg · 900 ms max".</summary>
    public string MetricsText { get; }
}
