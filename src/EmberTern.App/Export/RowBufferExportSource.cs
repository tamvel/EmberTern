using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using EmberTern.Core.Export;

namespace EmberTern.App.Export;

/// <summary>
/// Export adapter for a materialized in-memory grid (rows already projected to <c>object?[]</c>) — no
/// re-fetch. Used by Activity Monitor (a live ring buffer that cannot re-read history) and any future
/// VM-row grid: <see cref="ExportScope.CurrentView"/> = the filtered display, <see cref="ExportScope.AllRows"/>
/// = the whole buffer, <see cref="ExportScope.SelectedRows"/> = the current selection (offered only when
/// a selection is supplied). All estimates are exact.
/// </summary>
public sealed class RowBufferExportSource : IExportDataSource
{
    private readonly IReadOnlyList<object?[]> _currentView;
    private readonly IReadOnlyList<object?[]> _allRows;
    private readonly IReadOnlyList<object?[]>? _selectedRows;

    public RowBufferExportSource(
        IReadOnlyList<ExportColumn> columns,
        IReadOnlyList<object?[]> currentView,
        IReadOnlyList<object?[]> allRows,
        IReadOnlyList<object?[]>? selectedRows,
        string defaultBaseFileName)
    {
        Columns = columns;
        _currentView = currentView;
        _allRows = allRows;
        _selectedRows = selectedRows;

        var scopes = new List<ExportScope> { ExportScope.CurrentView, ExportScope.AllRows };
        var estimates = new Dictionary<ExportScope, RowEstimate>
        {
            [ExportScope.CurrentView] = RowEstimate.Exact(currentView.Count),
            [ExportScope.AllRows] = RowEstimate.Exact(allRows.Count),
        };
        if (selectedRows is not null && selectedRows.Count > 0)
        {
            scopes.Add(ExportScope.SelectedRows);
            estimates[ExportScope.SelectedRows] = RowEstimate.Exact(selectedRows.Count);
        }

        Capabilities = new ExportCapabilities(scopes, defaultBaseFileName, estimates);
    }

    public IReadOnlyList<ExportColumn> Columns { get; }

    public ExportCapabilities Capabilities { get; }

    public async IAsyncEnumerable<object?[]> GetRowsAsync(ExportScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = scope switch
        {
            ExportScope.CurrentView => _currentView,
            ExportScope.SelectedRows => _selectedRows ?? Array.Empty<object?[]>(),
            _ => _allRows,
        };
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
        }
        await System.Threading.Tasks.Task.CompletedTask;
    }
}
