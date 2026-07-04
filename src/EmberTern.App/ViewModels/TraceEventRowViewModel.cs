using System;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Trace;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One row of the chronological event grid. Holds the immutable <see cref="TraceEvent"/>
/// plus the presentation-only state the grid mutates live: <see cref="IsHighlighted"/>
/// (a lens selected this event's transaction/fingerprint) and <see cref="BandKey"/> (the
/// 2-shade "operation band" that flips on each transaction change — chronology-preserving
/// transaction boundaries, no rainbow, no reorder).
/// </summary>
public sealed partial class TraceEventRowViewModel : ObservableObject
{
    public TraceEventRowViewModel(TraceEvent e, string bandKey)
    {
        Event = e;
        BandKey = bandKey;
    }

    public TraceEvent Event { get; }

    public long Sequence => Event.Sequence;
    public string TimeText => Event.StartTime.ToString("HH:mm:ss.fff");
    public string DeltaText => Event.DeltaMs is { } d ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    private TraceSqlOperation? _operation;
    /// <summary>The SQL operation (SELECT/UPDATE/…) for a statement event; <c>None</c> for routines
    /// and unclassifiable SQL. Cached; drives the Event-column label and the operation filter.</summary>
    public TraceSqlOperation Operation => _operation ??= Event.Kind == TraceEventKind.Statement
        ? TraceSqlOperationClassifier.Classify(CleanSql(Event.Sql))
        : TraceSqlOperation.None;

    /// <summary>Event-column label: the SQL operation for a statement (e.g. "UPDATE" — far more
    /// useful than a generic "Statement"; the icon still conveys the kind), else the kind name.</summary>
    public string KindLabel => DisplayLabelFor(Event);

    /// <summary>Shared Event/detail label: operation for a classifiable statement, else the kind name.</summary>
    internal static string DisplayLabelFor(TraceEvent e)
    {
        if (e.Kind == TraceEventKind.Statement)
        {
            var op = TraceSqlOperationClassifier.Classify(CleanSql(e.Sql));
            if (op != TraceSqlOperation.None) return TraceSqlOperationClassifier.Label(op);
        }
        return e.Kind.ToString();
    }
    public string DurationText => Event.Duration is { } d ? ((long)d.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Duration (ms) at/above which an operation is worth a glance — drives the amber
    /// tint on the Duration cell and the "Slow" quick filter (P2). A session constant for now.</summary>
    internal const long SlowThresholdMs = 100;
    public bool IsSlow => Event.Duration is { } d && d.TotalMilliseconds >= SlowThresholdMs;
    public string RowsText => Event.RowsFetched?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    public string ReadsText => Event.Reads?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    public long? TransactionId => Event.TransactionId;
    public bool IsError => Event.Severity == TraceEventSeverity.Error;
    public bool IsSelfActivity => Event.IsSelfActivity;

    /// <summary>Indentation (px) for the call hierarchy — a child trigger/function sits under its
    /// owning statement (Depth 1). Flat projection, not a TreeView.</summary>
    public double IndentMargin => Event.Depth * 16;
    public bool IsChild => Event.Depth > 0;

    /// <summary>The grid's Object column: for an error, the (shortened) error message so the grid is
    /// self-explanatory at a glance — the message otherwise lives only in the Detail panel; for a
    /// statement, the elided SQL; else the routine name.</summary>
    public string ObjectText => IsError && !string.IsNullOrWhiteSpace(Event.ErrorText)
        ? ShortErrorMessage(Event.ErrorText!)
        : Event.Kind == TraceEventKind.Statement
            ? Elide(Event.Sql)
            : Event.ObjectName ?? string.Empty;

    /// <summary>First status-vector line of the error, stripped of its leading "&lt;gdscode&gt; : "
    /// prefix and elided — e.g. "Input parameter mismatch for procedure …".</summary>
    internal static string ShortErrorMessage(string errorText)
    {
        var first = errorText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')[0].Trim();
        var m = System.Text.RegularExpressions.Regex.Match(first, @"^-?\d+\s*:\s*(?<msg>.+)$");
        var msg = (m.Success ? m.Groups["msg"].Value : first).Trim();
        return msg.Length <= 120 ? msg : msg[..117] + "…";
    }

    /// <summary>Per-kind glyph + colour (reuses the metadata icon system's keys).</summary>
    public string IconGeometryKey => IconGeometryKeyFor(Event.Kind);
    public string IconResourceKey => IconResourceKeyFor(Event.Kind);

    internal static string IconGeometryKeyFor(TraceEventKind kind) => kind switch
    {
        TraceEventKind.Statement => "Icon.Query",
        TraceEventKind.Procedure => "Icon.Procedure",
        TraceEventKind.Trigger => "Icon.Trigger",
        TraceEventKind.Function => "Icon.Function",
        _ => "Icon.Connection",
    };

    internal static string IconResourceKeyFor(TraceEventKind kind) => kind switch
    {
        TraceEventKind.Statement => "IconColor_Query",
        TraceEventKind.Procedure => "IconColor_Procedure",
        TraceEventKind.Trigger => "IconColor_Trigger",
        TraceEventKind.Function => "IconColor_Function",
        _ => "SubtleForegroundBrush",
    };

    /// <summary>"TxBand0"/"TxBand1" — the alternating operation band, assigned at ingest time.
    /// Retained for tests / diagnostics; the grid now marks transaction boundaries with a
    /// subtle rule (<see cref="IsTransactionStart"/>) rather than an alternating fill.</summary>
    public string BandKey { get; }

    /// <summary>True on the first row of each transaction — draws the subtle "new operation"
    /// boundary line in the gutter (chronology-preserving, no flicker on 1-statement txs).</summary>
    public bool IsTransactionStart { get; init; }

    private string? _fingerprint;
    /// <summary>Cached statement fingerprint (empty for SQL-less events), for lens matching.</summary>
    public string Fingerprint => _fingerprint ??= TraceStatementFingerprinter.Fingerprint(Event.Sql);

    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>Strips the technical trace separator lines (rows of "-----") from captured SQL,
    /// preserving line structure — a presentation cleaner. The raw SQL on the event is left
    /// untouched (detail/export stay faithful); only what we show / copy / open is cleaned.</summary>
    internal static string CleanSql(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return string.Empty;
        var lines = sql!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        foreach (var raw in lines)
        {
            var t = raw.Trim();
            if (t.Length >= 4 && t.All(c => c == '-')) continue; // pure trace separator line
            sb.Append(raw.TrimEnd()).Append('\n');
        }
        return sb.ToString().Trim('\n', ' ', '\t');
    }

    internal static string Elide(string? sql)
    {
        var clean = CleanSql(sql);
        if (clean.Length == 0) return string.Empty;
        var flat = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ").Trim();
        return flat.Length <= 140 ? flat : flat[..137] + "…";
    }
}
