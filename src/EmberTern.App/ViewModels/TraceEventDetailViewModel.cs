using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Trace;

namespace EmberTern.App.ViewModels;

/// <summary>
/// The detail panel (zone ③): everything about the selected event that does NOT belong in
/// the grid (rule 5). Reuses the Performance module's <see cref="TableAccessBarViewModel"/>
/// for the per-table bars. The SQL text is pushed to a read-only AvaloniaEdit by the view.
/// </summary>
public sealed partial class TraceEventDetailViewModel : ObservableObject
{
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _sql = string.Empty;
    [ObservableProperty] private bool _hasSql;
    [ObservableProperty] private string _kindLabel = string.Empty;
    [ObservableProperty] private string _objectName = string.Empty;
    [ObservableProperty] private bool _hasObjectName;
    [ObservableProperty] private string _timingText = string.Empty;
    [ObservableProperty] private string _transactionText = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _hasParameters;
    [ObservableProperty] private bool _hasTableAccess;

    public ObservableCollection<string> Parameters { get; } = new();
    public ObservableCollection<TableAccessBarViewModel> TableAccess { get; } = new();

    public void Clear()
    {
        HasSelection = false;
        Sql = string.Empty; HasSql = false;
        KindLabel = string.Empty; ObjectName = string.Empty; HasObjectName = false;
        TimingText = string.Empty; TransactionText = string.Empty;
        ErrorText = string.Empty; HasError = false;
        Parameters.Clear(); HasParameters = false;
        TableAccess.Clear(); HasTableAccess = false;
    }

    public void Update(TraceEvent e)
    {
        HasSelection = true;
        KindLabel = e.Kind.ToString();
        ObjectName = e.ObjectName ?? string.Empty;
        HasObjectName = !string.IsNullOrEmpty(e.ObjectName);

        Sql = e.Sql ?? string.Empty;
        HasSql = !string.IsNullOrEmpty(e.Sql);

        ErrorText = e.ErrorText ?? string.Empty;
        HasError = !string.IsNullOrEmpty(e.ErrorText);

        TimingText = BuildTiming(e);
        TransactionText = e.TransactionId is { } tx
            ? string.Format(CultureInfo.InvariantCulture, "TRA {0}", tx)
            : string.Empty;

        Parameters.Clear();
        foreach (var p in e.Parameters)
            Parameters.Add($"param{p.Index} = {p.DataType}: {p.Value ?? "<NULL>"}");
        HasParameters = Parameters.Count > 0;

        TableAccess.Clear();
        long max = e.TableAccess.Count > 0 ? e.TableAccess.Max(t => t.TotalReads) : 0;
        foreach (var t in e.TableAccess.OrderByDescending(t => t.SequentialReads).ThenByDescending(t => t.TotalReads))
            TableAccess.Add(new TableAccessBarViewModel(t, max));
        HasTableAccess = TableAccess.Count > 0;
    }

    private static string BuildTiming(TraceEvent e)
    {
        var parts = new List<string>();
        if (e.Duration is { } d) parts.Add($"{(long)d.TotalMilliseconds} ms");
        if (e.RowsFetched is { } r) parts.Add($"{r} rows");
        if (e.Reads is { } reads) parts.Add($"{reads} reads");
        if (e.Writes is { } w) parts.Add($"{w} writes");
        if (e.Fetches is { } f) parts.Add($"{f} fetches");
        return string.Join(" · ", parts);
    }
}
