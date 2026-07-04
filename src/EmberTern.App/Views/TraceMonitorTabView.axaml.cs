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

    // Right-clicked cell, captured for the copy context menu.
    private TraceEventRowViewModel? _copyRow;
    private string? _copyHeader;

    public TraceMonitorTabView()
    {
        InitializeComponent();
        _detailSql = this.FindControl<TextEditor>("DetailSqlEditor");
        _gridAreaRow = RootGrid.RowDefinitions[1];
        _detailRow = RootGrid.RowDefinitions[3];
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
        if (_copyRow is not null) grid.SelectedItem = _copyRow; // right-click selects (drives the detail too)
    }

    private void OnCopyCellClick(object? sender, RoutedEventArgs e) => _vm?.CopyCell(_copyRow, _copyHeader);
    private void OnCopyRowClick(object? sender, RoutedEventArgs e) => _vm?.CopyRow(_copyRow);
    private void OnCopyRowWithHeadersClick(object? sender, RoutedEventArgs e) => _vm?.CopyRowWithHeaders(_copyRow);
    private void OnCopyAllWithHeadersClick(object? sender, RoutedEventArgs e) => _vm?.CopyAllWithHeaders();
    private void OnCopySqlClick(object? sender, RoutedEventArgs e) => _vm?.CopyRowSql(_copyRow);

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
