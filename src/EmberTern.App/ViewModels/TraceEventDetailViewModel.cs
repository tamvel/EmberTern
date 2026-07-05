using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Sql;
using EmberTern.Core.Trace;

namespace EmberTern.App.ViewModels;

/// <summary>A label : value row in the detail panel (Session / Timing sections).</summary>
public sealed record TraceDetailKv(string Label, string Value);

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
    [ObservableProperty] private string _iconGeometryKey = "Icon.Query";
    [ObservableProperty] private string _iconResourceKey = "IconColor_Query";
    [ObservableProperty] private string _objectName = string.Empty;
    [ObservableProperty] private bool _hasObjectName;
    [ObservableProperty] private string _timingText = string.Empty;
    [ObservableProperty] private bool _hasTiming;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _hasParameters;
    [ObservableProperty] private bool _hasTableAccess;
    [ObservableProperty] private bool _hasSession;

    public ObservableCollection<string> Parameters { get; } = new();
    public ObservableCollection<TableAccessBarViewModel> TableAccess { get; } = new();

    /// <summary>Executor identity — "who ran this?" (User / Role / Host / Process / Attachment /
    /// Transaction), only the rows that have a value.</summary>
    public ObservableCollection<TraceDetailKv> SessionRows { get; } = new();

    public void Clear()
    {
        HasSelection = false;
        Sql = string.Empty; HasSql = false;
        KindLabel = string.Empty; ObjectName = string.Empty; HasObjectName = false;
        IconGeometryKey = "Icon.Query"; IconResourceKey = "IconColor_Query";
        TimingText = string.Empty; HasTiming = false;
        ErrorText = string.Empty; HasError = false;
        Parameters.Clear(); HasParameters = false;
        TableAccess.Clear(); HasTableAccess = false;
        SessionRows.Clear(); HasSession = false;
    }

    /// <param name="showValues">When true (the default), parameter values are inlined into the
    /// displayed SQL (<c>= ?</c> → <c>= 10036</c>) via <see cref="TraceSqlInliner"/> — a
    /// presentation-only aid; the raw SQL on the event is untouched. When false, the faithful
    /// parameterised source is shown.</param>
    public void Update(TraceEvent e, bool showValues = true)
    {
        HasSelection = true;
        KindLabel = TraceEventRowViewModel.DisplayLabelFor(e); // operation for statements, else kind
        IconGeometryKey = TraceEventRowViewModel.IconGeometryKeyFor(e); // operation-aware (writes pop)
        IconResourceKey = TraceEventRowViewModel.IconResourceKeyFor(e);
        ObjectName = e.ObjectName ?? string.Empty;
        HasObjectName = !string.IsNullOrEmpty(e.ObjectName);

        var cleanSql = TraceEventRowViewModel.CleanSql(e.Sql);
        var display = showValues && e.Parameters.Count > 0
            ? TraceSqlInliner.Inline(cleanSql, e.Parameters)
            : cleanSql;
        Sql = FormatForDisplay(display);
        HasSql = Sql.Length > 0;

        ErrorText = e.ErrorText ?? string.Empty;
        HasError = !string.IsNullOrEmpty(e.ErrorText);

        TimingText = BuildTiming(e);
        HasTiming = TimingText.Length > 0;

        Parameters.Clear();
        foreach (var p in e.Parameters)
            Parameters.Add($"param{p.Index} = {p.DataType}: {p.Value ?? "<NULL>"}");
        HasParameters = Parameters.Count > 0;

        TableAccess.Clear();
        long max = e.TableAccess.Count > 0 ? e.TableAccess.Max(t => t.TotalReads) : 0;
        foreach (var t in e.TableAccess.OrderByDescending(t => t.SequentialReads).ThenByDescending(t => t.TotalReads))
            TableAccess.Add(new TableAccessBarViewModel(t, max));
        HasTableAccess = TableAccess.Count > 0;

        SessionRows.Clear();
        AddRow("User", e.UserName);
        AddRow("Role", e.RoleName);
        AddRow("Host", e.RemoteAddress);
        AddRow("Process", FormatProcess(e.ProcessName, e.ClientProcessId));
        AddRow("Trigger event", e.TriggerEvent);   // "what fired" — only present for triggers
        AddRow("Attachment", e.AttachmentId is { } att ? "ATT " + att.ToString(CultureInfo.InvariantCulture) : null);
        AddRow("Transaction", FormatTransaction(e.TransactionId, e.TransactionParams)); // id · isolation/TPB
        HasSession = SessionRows.Count > 0;
    }

    /// <summary>Always show the SQL formatted — long traced statements arrive as a single line
    /// and are hard to read. Reuses the shared <see cref="SqlFormatter"/> (no second formatter);
    /// presentation-only, and defensive against truncated/odd trace SQL (falls back to the input).</summary>
    internal static string FormatForDisplay(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql;
        try { return SqlFormatter.Format(sql); }
        catch { return sql; }
    }

    private void AddRow(string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) SessionRows.Add(new TraceDetailKv(label, value!));
    }

    private static string? FormatProcess(string? name, int? pid)
    {
        if (string.IsNullOrWhiteSpace(name)) return pid is { } p ? "pid " + p.ToString(CultureInfo.InvariantCulture) : null;
        return pid is { } q ? $"{name} (pid {q.ToString(CultureInfo.InvariantCulture)})" : name;
    }

    private static string? FormatTransaction(long? id, string? txParams)
    {
        if (id is not { } tx) return null;
        var s = "TRA " + tx.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(txParams) ? s : s + " · " + txParams;
    }

    private static string BuildTiming(TraceEvent e)
    {
        var parts = new List<string>();
        if (e.Duration is { } d) parts.Add($"{(long)d.TotalMilliseconds} ms");
        if (e.RowsFetched is { } r) parts.Add($"{r} row{(r == 1 ? "" : "s")}");
        if (e.Reads is { } reads) parts.Add($"{reads} reads");
        if (e.Writes is { } w) parts.Add($"{w} writes");
        if (e.Fetches is { } f) parts.Add($"{f} fetches");
        return string.Join(" · ", parts);
    }
}
