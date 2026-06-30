using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace EmberTern.App.Views;

public partial class SecurityManagerTabView : UserControl
{
    public SecurityManagerTabView()
    {
        InitializeComponent();
        // Select the row under the pointer on press (left OR right) so: (a) clicking a
        // tri-state cell also selects the row → the column panel follows the table;
        // (b) right-click selects before the context menu opens (gotcha #16). Tunnel so
        // it runs before the cell Button consumes the click; leave Handled=false so the
        // Button still cycles.
        var grid = this.FindControl<DataGrid>("PrivilegeGrid");
        grid?.AddHandler(InputElement.PointerPressedEvent, OnPrivilegeGridPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnPrivilegeGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        var row = (e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.DataContext is not null)
            grid.SelectedItem = row.DataContext;
    }
}
