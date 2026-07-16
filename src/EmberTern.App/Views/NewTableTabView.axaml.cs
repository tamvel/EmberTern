using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace EmberTern.App.Views;

public partial class NewTableTabView : UserControl
{
    public NewTableTabView()
    {
        InitializeComponent();

        // Faza 4 / Krok 3: replace the XAML single-Domain column with the shared merged
        // "Domena/Kolumna" picker (Domain + Table-column/TYPE OF COLUMN tabs). Built in code
        // because the picker's sections aren't in the visual tree (can't bind per-row).
        if (this.FindControl<DataGrid>("NewTableFieldsGrid") is { } grid)
        {
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                if (Equals(grid.Columns[i].Header, UiStrings.NewTableFieldDomain))
                {
                    grid.Columns.RemoveAt(i);
                    grid.Columns.Insert(i, MergedTypeSourceColumn.Build(UiStrings.FieldTypeSourceHeader, 150));
                    break;
                }
            }
        }
    }

    // Select the row under a right-click on the Fields grid so the context-menu Remove /
    // Move act on the clicked row (Avalonia DataGrid doesn't auto-select on right-click,
    // gotcha #16). Handled stays false so the ContextMenu still opens.
    private void OnEasyGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (!e.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;
        if (e.Source is not Visual v) return;
        var row = v.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.DataContext is { } item) grid.SelectedItem = item;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
