using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Performance;
using EmberTern.Core.Query;

namespace EmberTern.App.ViewModels;

/// <summary>One Performance surface bound to ONE host (the SQL Editor, or a single
/// Procedure/Function detail tab). It pairs a <see cref="PerformancePanelViewModel"/> with the
/// last run captured IN THAT host (sql + result + per-table reads) and the shared analysis
/// build function. Recording a run marks the panel stale; the panel rebuilds lazily on view.
///
/// This replaces the former single global <c>MainWindowViewModel.Performance</c> — each host
/// analyzes only its own last execution, so a procedure's metrics never leak into the SQL Editor
/// (or another tab). The build delegate reaches the shared Firebird readers on the owner, but the
/// captured data lives here.
///
/// Named to avoid a collision with the unrelated <see cref="EmberTern.Core.Performance.PerformanceContext"/>
/// (the advisor's analysis context) — this is the App-side per-host owner of the panel + captured run.</summary>
public sealed class HostPerformanceContext
{
    private readonly Func<string?, QueryResult?, IReadOnlyList<PerTableReadRow>?, CancellationToken, Task<PerformanceReport?>> _build;
    private string? _sql;
    private QueryResult? _result;
    private IReadOnlyList<PerTableReadRow>? _reads;

    public PerformancePanelViewModel Panel { get; }

    public HostPerformanceContext(
        Func<string?, QueryResult?, IReadOnlyList<PerTableReadRow>?, CancellationToken, Task<PerformanceReport?>> build,
        Func<string, Task>? clipboardWriteRequested = null)
    {
        _build = build;
        Panel = new PerformancePanelViewModel
        {
            BuildCallback = ct => _build(_sql, _result, _reads, ct),
            ClipboardWriteRequested = clipboardWriteRequested,
        };
    }

    /// <summary>Remember this host's latest execution so the panel can analyze it on view
    /// (no re-run). Marks the analysis stale; the panel refreshes immediately if it's shown.</summary>
    public void Record(string sql, QueryResult? result, IReadOnlyList<PerTableReadRow>? reads)
    {
        _sql = sql;
        _result = result;
        _reads = reads;
        Panel.MarkStale();
    }

    /// <summary>Drop the captured run (e.g. on disconnect) so the panel returns to its empty
    /// state instead of showing a stale report.</summary>
    public void Clear()
    {
        _sql = null;
        _result = null;
        _reads = null;
        Panel.MarkStale();
    }
}
