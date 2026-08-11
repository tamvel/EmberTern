using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.ViewModels;
using EmberTern.Core.Export;
using EmberTern.Core.Query;

namespace EmberTern.App.Views;

public partial class TraceMonitorTabView : UserControl
{
    private const double DefaultDetailHeight = 240;

    private TraceMonitorTabViewModel? _vm;
    private readonly TextEditor? _detailSql;

    // Detail-panel maximize/restore (mirrors the SQL editor's results panel). Row 1 = grid area,
    // row 3 = detail; the code-behind owns the sizing, the VM only holds the display flag.
    private readonly RowDefinition? _gridAreaRow;
    private readonly RowDefinition? _detailRow;
    private double _detailHeight = DefaultDetailHeight;
    private bool _detailMaximized;

    // Right-clicked cell, captured for the copy + filter-from-cell context menus.
    private TraceEventRowViewModel? _copyRow;
    private string? _copyHeader;
    private DataGridColumn? _clickedColumn;

    public TraceMonitorTabView()
    {
        InitializeComponent();
        _detailSql = this.FindControl<TextEditor>("DetailSqlEditor");
        // Rows: 0 toolbar · 1 conditional-filter panel · 2 grid area · 3 splitter · 4 detail.
        _gridAreaRow = RootGrid.RowDefinitions[2];
        _detailRow = RootGrid.RowDefinitions[4];
        StampFilterColumnTags();
        // Follow-tail auto-pauses when the user scrolls up, and re-arms at the bottom —
        // standard log/trace-viewer behaviour. The grid's inner ScrollViewer bubbles here.
        EventsGrid.AddHandler(ScrollViewer.ScrollChangedEvent, OnGridScrollChanged);
        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
    }

    // ---- grid copy context menu (reuses the shared clipboard channel via the VM) ----

    private void OnEventCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (!e.PointerPressedEventArgs.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;

        _copyRow = e.Row?.DataContext as TraceEventRowViewModel;
        _copyHeader = e.Column?.Header?.ToString();
        _clickedColumn = e.Column;
        if (_copyRow is not null) grid.SelectedItem = _copyRow; // right-click selects (drives the detail too)
    }

    private void OnCopyCellClick(object? sender, RoutedEventArgs e) => _vm?.CopyCell(_copyRow, _copyHeader);
    private void OnCopyRowClick(object? sender, RoutedEventArgs e) => _vm?.CopyRow(_copyRow);
    private void OnCopyRowWithHeadersClick(object? sender, RoutedEventArgs e) => _vm?.CopyRowWithHeaders(_copyRow);
    private void OnCopyAllWithHeadersClick(object? sender, RoutedEventArgs e) => _vm?.CopyAllWithHeaders();
    private void OnCopySqlClick(object? sender, RoutedEventArgs e) => _vm?.CopyRowSql(_copyRow);

    // Export the trace grid through the shared Export Framework. Default scope = the filtered view
    // (what the user narrowed the trace to is usually what they want to export).
    private async void OnExportClick(object? sender, RoutedEventArgs e)
        => await ExportDialog.LaunchAsync(this, _vm?.BuildExportSource(), ExportScope.CurrentView);

    // ---- filter-from-cell context menu (adds a condition to the shared grid filter) ----

    // Map each filterable grid column to its filter-column index (Column.Tag, boxed int — robust to
    // reorder, like the result grids). Non-filterable columns (# / gutter) get no Tag → the verbs
    // no-op on them. Done once; the columns are static.
    private void StampFilterColumnTags()
    {
        foreach (var col in EventsGrid.Columns)
        {
            // ⚠ An if-chain, not a switch expression, and the reason is localization: a switch pattern must be
            // a compile-time CONSTANT, and a localized string is a property resolved at call time. This was the
            // only place in the app where a UiStrings member was used in a const-only position.
            //
            // ⚠ Matching a column by its DISPLAYED text stays correct under translation only because both
            // sides read the same key — the header is set from UiStrings and compared to UiStrings, so they
            // move together. ⛔ Do not "optimise" either side into a literal; that is what would break the
            // moment a language is added, silently, by leaving every Tag null and the filter verbs inert.
            var header = col.Header?.ToString();
            int? idx =
                header == UiStrings.TraceColTime ? 0 :
                header == UiStrings.TraceColEvent ? 1 :
                header == UiStrings.TraceColDuration ? 4 :
                header == UiStrings.TraceColObject ? 3 :
                header == UiStrings.TraceColRows ? 5 :
                header == UiStrings.TraceColReads ? 6 :
                header == UiStrings.TraceColTx ? 7 :
                null;
            if (idx is { } i) col.Tag = i;
        }
    }

    // Resolve the right-clicked cell into a filter context: the filter-column index (from Tag), the
    // projected cell value (round-trip-formatted), null-ness, and the column category.
    private GridCellFilterContext? ResolveClickedCell()
    {
        if (_copyRow is null || _clickedColumn?.Tag is not int idx) return null;
        var cols = TraceMonitorTabViewModel.FilterColumns;
        if (idx < 0 || idx >= cols.Count) return null;
        var cell = TraceMonitorTabViewModel.ProjectRow(_copyRow)[idx];
        bool isNull = cell is null;
        string? value = isNull ? null : GridCellFilter.FormatCellValue(cell!);
        return new GridCellFilterContext(idx, value, isNull, cols[idx].Category);
    }

    private void ApplyTriple((int ColumnIndex, GridFilterOperator Op, string? Value) t)
        => _ = _vm?.GridFilterPanel.ApplyFromCellAsync(t.ColumnIndex, t.Op, t.Value);

    private void OnFilterByValueClick(object? sender, RoutedEventArgs e)
    { if (ResolveClickedCell() is { } c) ApplyTriple(GridCellFilter.FilterByValue(c)); }

    private void OnExcludeValueClick(object? sender, RoutedEventArgs e)
    { if (ResolveClickedCell() is { } c) ApplyTriple(GridCellFilter.ExcludeValue(c)); }

    private void OnFilterContainsClick(object? sender, RoutedEventArgs e)
    { if (ResolveClickedCell() is { } c && GridCellFilter.Contains(c) is { } t) ApplyTriple(t); }

    // ---- detail panel maximize / restore ----

    private void OnDetailSplitterDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleDetailMaximized();
        e.Handled = true;
    }

    private void OnToggleDetailMaximizeClick(object? sender, RoutedEventArgs e) => ToggleDetailMaximized();

    private void ToggleDetailMaximized()
    {
        if (_gridAreaRow is null || _detailRow is null) return;

        if (!_detailMaximized)
        {
            // Capture the live (dragged) detail height so Restore lands back on it.
            if (_detailRow.Height.IsAbsolute && _detailRow.Height.Value > 0)
                _detailHeight = _detailRow.Height.Value;
            _gridAreaRow.Height = new GridLength(0);
            _detailRow.Height = new GridLength(1, GridUnitType.Star);
            _detailMaximized = true;
        }
        else
        {
            _gridAreaRow.Height = new GridLength(1, GridUnitType.Star);
            _detailRow.Height = new GridLength(_detailHeight);
            _detailMaximized = false;
        }
        if (_vm is not null) _vm.IsDetailMaximized = _detailMaximized;
    }

    private void OnGridScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_vm is null || e.Source is not ScrollViewer sv) return;
        // Only vertical position matters; ignore when there's nothing to scroll.
        if (sv.Extent.Height <= sv.Viewport.Height) return;
        bool nearBottom = sv.Extent.Height - sv.Viewport.Height - sv.Offset.Y <= 24;
        if (_vm.FollowTail)
        {
            if (!nearBottom) _vm.FollowTail = false;   // manual scroll-up → pause
        }
        else if (nearBottom)
        {
            _vm.FollowTail = true;                     // scrolled back to bottom → resume
        }
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ScrollToRowRequested -= OnScrollToRowRequested;
            _vm.DetailSqlChanged -= OnDetailSqlChanged;
        }

        _vm = DataContext as TraceMonitorTabViewModel;

        if (_vm is not null)
        {
            _vm.ScrollToRowRequested += OnScrollToRowRequested;
            _vm.DetailSqlChanged += OnDetailSqlChanged;
            OnDetailSqlChanged(_vm.Detail.Sql);
        }
    }

    // Follow-tail + lens "scroll to first occurrence": bring the requested row into view.
    private void OnScrollToRowRequested(TraceEventRowViewModel row)
        => Dispatcher.UIThread.Post(() => EventsGrid.ScrollIntoView(row, null), DispatcherPriority.Background);

    // The detail SQL is pushed (not bound) into the read-only AvaloniaEdit — same pattern as
    // every other detail view. Text is already separator-cleaned by the VM.
    private void OnDetailSqlChanged(string sql)
    {
        if (_detailSql is not null && _detailSql.Text != sql) _detailSql.Text = sql;
    }

    // Firebird syntax highlighting + themed selection brush, swapped on theme toggle —
    // reuses App.FirebirdSyntax(Light)Name and the SelectionBrush resource (gotcha #19/#20).
    private void ApplyEditorTheme()
    {
        if (_detailSql is null) return;
        var theme = ActualThemeVariant;
        var name = theme == ThemeVariant.Light ? App.FirebirdSyntaxLightName : App.FirebirdSyntaxName;
        _detailSql.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(name);
        if (Application.Current?.Resources.TryGetResource("SelectionBrush", theme, out var res) == true
            && res is IBrush brush)
        {
            _detailSql.TextArea.SelectionBrush = brush;
        }
    }
}
