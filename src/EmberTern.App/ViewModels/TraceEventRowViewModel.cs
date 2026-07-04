using System;
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
    public string KindLabel => Event.Kind.ToString();
    public string DurationText => Event.Duration is { } d ? ((long)d.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
    public string RowsText => Event.RowsFetched?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    public string ReadsText => Event.Reads?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    public long? TransactionId => Event.TransactionId;
    public bool IsError => Event.Severity == TraceEventSeverity.Error;
    public bool IsSelfActivity => Event.IsSelfActivity;

    /// <summary>Indentation (px) for the call hierarchy — a child trigger/function sits under its
    /// owning statement (Depth 1). Flat projection, not a TreeView.</summary>
    public double IndentMargin => Event.Depth * 16;
    public bool IsChild => Event.Depth > 0;

    /// <summary>The grid's Object column: elided SQL for a statement, else the routine name.</summary>
    public string ObjectText => Event.Kind == TraceEventKind.Statement
        ? Elide(Event.Sql)
        : Event.ObjectName ?? string.Empty;

    /// <summary>Per-kind glyph + colour (reuses the metadata icon system's keys).</summary>
    public string IconGeometryKey => Event.Kind switch
    {
        TraceEventKind.Statement => "Icon.Query",
        TraceEventKind.Procedure => "Icon.Procedure",
        TraceEventKind.Trigger => "Icon.Trigger",
        TraceEventKind.Function => "Icon.Function",
        _ => "Icon.Connection",
    };

    public string IconResourceKey => Event.Kind switch
    {
        TraceEventKind.Statement => "IconColor_Query",
        TraceEventKind.Procedure => "IconColor_Procedure",
        TraceEventKind.Trigger => "IconColor_Trigger",
        TraceEventKind.Function => "IconColor_Function",
        _ => "SubtleForegroundBrush",
    };

    /// <summary>"TxBand0"/"TxBand1" — the alternating operation band, assigned at ingest time.</summary>
    public string BandKey { get; }

    private string? _fingerprint;
    /// <summary>Cached statement fingerprint (empty for SQL-less events), for lens matching.</summary>
    public string Fingerprint => _fingerprint ??= TraceStatementFingerprinter.Fingerprint(Event.Sql);

    [ObservableProperty]
    private bool _isHighlighted;

    internal static string Elide(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return string.Empty;
        var flat = System.Text.RegularExpressions.Regex.Replace(sql!.Trim(), @"\s+", " ");
        return flat.Length <= 140 ? flat : flat[..137] + "…";
    }
}
