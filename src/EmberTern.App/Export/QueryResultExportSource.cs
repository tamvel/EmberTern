using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using EmberTern.Core.Export;
using EmberTern.Core.Query;

namespace EmberTern.App.Export;

/// <summary>
/// Export adapter for the SQL Editor results grid (materialized set). Implements the smart
/// cached-vs-re-run rule (B.6):
/// <list type="bullet">
/// <item><see cref="ExportScope.CurrentView"/> → the rows the grid currently shows (client-side
/// filter + sort applied) — instant, no DB hit.</item>
/// <item><see cref="ExportScope.AllRows"/> on a <b>complete</b> result → the cached rows, instant.</item>
/// <item><see cref="ExportScope.AllRows"/> on a <b>partial</b> grid (a truncated preview OR a Full run
/// that hit the safety ceiling) → re-fetch by streaming the full query (<see cref="_streamAll"/> = a
/// Full streaming read), so the export is complete, never materializes the whole set in memory, and is
/// NOT bounded by the grid's row ceiling (A.6 — "streams past the ceiling straight to file").</item>
/// </list>
/// </summary>
public sealed class QueryResultExportSource : IExportDataSource
{
    private readonly IReadOnlyList<object?[]> _currentViewRows;
    private readonly IReadOnlyList<object?[]> _materializedRows;
    private readonly bool _isPartial;
    private readonly Func<CancellationToken, IAsyncEnumerable<object?[]>>? _streamAll;

    public QueryResultExportSource(
        IReadOnlyList<QueryColumn> columns,
        IReadOnlyList<object?[]> currentViewRows,
        IReadOnlyList<object?[]> materializedRows,
        bool isPartial,
        Func<CancellationToken, IAsyncEnumerable<object?[]>>? streamAll,
        string defaultBaseFileName)
    {
        Columns = columns.Select(c => new ExportColumn(c.Name, c.ClrType)).ToList();
        _currentViewRows = currentViewRows;
        _materializedRows = materializedRows;
        _isPartial = isPartial;
        _streamAll = streamAll;
        Capabilities = BuildCapabilities(defaultBaseFileName);
    }

    public IReadOnlyList<ExportColumn> Columns { get; }

    public ExportCapabilities Capabilities { get; }

    private ExportCapabilities BuildCapabilities(string defaultBaseFileName)
    {
        // AllRows count is exact when the materialized result is complete; unknown when the grid is
        // partial (truncated preview / ceiling-hit) — we don't run a COUNT just to label it (B.8).
        var estimates = new Dictionary<ExportScope, RowEstimate>
        {
            [ExportScope.CurrentView] = RowEstimate.Exact(_currentViewRows.Count),
            [ExportScope.AllRows] = _isPartial ? RowEstimate.Unknown : RowEstimate.Exact(_materializedRows.Count),
        };
        // SelectedRows is intentionally omitted — the SQL results grid is single-select (Risk #8).
        return new ExportCapabilities(
            new[] { ExportScope.CurrentView, ExportScope.AllRows },
            defaultBaseFileName,
            estimates);
    }

    public async IAsyncEnumerable<object?[]> GetRowsAsync(
        ExportScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (scope == ExportScope.CurrentView)
        {
            foreach (var row in _currentViewRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
            }
            yield break;
        }

        // AllRows: cached when complete, otherwise stream the full query from the DB.
        if (!_isPartial || _streamAll is null)
        {
            foreach (var row in _materializedRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
            }
            yield break;
        }

        await foreach (var row in _streamAll(cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return row;
        }
    }
}
