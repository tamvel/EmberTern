using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Export;
using EmberTern.Core.Export.Sql;

namespace EmberTern.App.Export;

/// <summary>
/// Export adapter for a server-paged grid (Table Data / View Data), where the grid holds only the
/// current page. <see cref="ExportScope.CurrentView"/> = the current page (what's on screen);
/// <see cref="ExportScope.AllRows"/> re-fetches the WHOLE set page-by-page straight to the sink —
/// honouring the grid's current filter + order, never materializing it all in memory. The AllRows
/// estimate is the source's bounded row-count probe (approximate when it hit the cap).
/// </summary>
public sealed class ServerPagedExportSource : IExportDataSource
{
    // Larger than the on-screen page: fewer round-trips when streaming the full set to a file.
    public const int DefaultFetchPageSize = 5000;

    private readonly IReadOnlyList<object?[]> _currentPage;
    private readonly Func<int, int, CancellationToken, Task<IReadOnlyList<object?[]>>> _fetchPage;
    private readonly int _fetchPageSize;

    public ServerPagedExportSource(
        IReadOnlyList<ExportColumn> columns,
        IReadOnlyList<object?[]> currentPageRows,
        RowEstimate allRowsEstimate,
        Func<int, int, CancellationToken, Task<IReadOnlyList<object?[]>>> fetchPage,
        int fetchPageSize,
        string defaultBaseFileName,
        ResultOrigin? origin = null)
    {
        Columns = columns;
        _currentPage = currentPageRows;
        _fetchPage = fetchPage;
        _fetchPageSize = fetchPageSize;

        // Table Data supplies OriginShape.DirectTable — that grid IS a table, which makes it STRICTLY
        // SAFER than the SQL Editor: nothing is inferred from a statement, so signal B is satisfied by
        // construction. View Data supplies its view name and is refused by the catalog check, which is
        // the correct outcome at this stage.
        Origin = origin ?? ResultOrigin.None(
            new ExportUnavailableReason(ExportUnavailableCode.NotATable));

        // SelectedRows is not offered — a data grid's selection isn't a meaningful export scope here
        // (the snapshot intent is the current page or the whole set).
        Capabilities = new ExportCapabilities(
            new[] { ExportScope.CurrentView, ExportScope.AllRows },
            defaultBaseFileName,
            new Dictionary<ExportScope, RowEstimate>
            {
                [ExportScope.CurrentView] = RowEstimate.Exact(currentPageRows.Count),
                [ExportScope.AllRows] = allRowsEstimate,
            });
    }

    public IReadOnlyList<ExportColumn> Columns { get; }

    public ExportCapabilities Capabilities { get; }

    public ResultOrigin Origin { get; }

    public async IAsyncEnumerable<object?[]> GetRowsAsync(ExportScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (scope == ExportScope.CurrentView)
        {
            foreach (var row in _currentPage)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
            }
            yield break;
        }

        // AllRows: page through the full (filtered + ordered) result straight to the writer.
        int page = 1;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = await _fetchPage(page, _fetchPageSize, cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
            }
            if (rows.Count < _fetchPageSize) break; // short page → last page reached
            page++;
        }
    }
}
