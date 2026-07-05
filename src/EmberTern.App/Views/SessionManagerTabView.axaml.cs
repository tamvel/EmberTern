using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

public partial class SessionManagerTabView : UserControl
{
    private const double DefaultBottomHeight = 240;

    private SessionManagerTabViewModel? _vm;

    // Bottom-panel maximize/restore (identical mechanic to the Activity Monitor detail panel).
    // Row 2 = sessions grid area, row 4 = bottom tabs; the code-behind owns the sizing, the VM
    // only holds the display flag.
    private readonly RowDefinition? _gridAreaRow;
    private readonly RowDefinition? _bottomRow;
    private double _bottomHeight = DefaultBottomHeight;
    private bool _bottomMaximized;

    public SessionManagerTabView()
    {
        InitializeComponent();
        _gridAreaRow = RootGrid.RowDefinitions[2];
        _bottomRow = RootGrid.RowDefinitions[4];
        DataContextChanged += (_, _) => _vm = DataContext as SessionManagerTabViewModel;
    }

    // Right-click selects the row under the cursor so the context-menu actions (Cancel / Disconnect
    // / Copy) act on it — same pattern as the trace grid (gotcha #16/#99).
    private void OnSessionCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (!e.PointerPressedEventArgs.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;
        if (e.Row?.DataContext is SessionRowViewModel row) grid.SelectedItem = row;
    }

    private void OnBottomSplitterDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleBottomMaximized();
        e.Handled = true;
    }

    private void OnToggleBottomMaximizeClick(object? sender, RoutedEventArgs e) => ToggleBottomMaximized();

    private void ToggleBottomMaximized()
    {
        if (_gridAreaRow is null || _bottomRow is null) return;

        if (!_bottomMaximized)
        {
            if (_bottomRow.Height.IsAbsolute && _bottomRow.Height.Value > 0)
                _bottomHeight = _bottomRow.Height.Value;
            _gridAreaRow.Height = new GridLength(0);
            _bottomRow.Height = new GridLength(1, GridUnitType.Star);
            _bottomMaximized = true;
        }
        else
        {
            _gridAreaRow.Height = new GridLength(1, GridUnitType.Star);
            _bottomRow.Height = new GridLength(_bottomHeight);
            _bottomMaximized = false;
        }
        if (_vm is not null) _vm.IsDetailMaximized = _bottomMaximized;
    }
}
