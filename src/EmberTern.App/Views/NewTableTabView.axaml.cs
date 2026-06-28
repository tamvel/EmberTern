using Avalonia.Controls;
using Avalonia.Markup.Xaml;

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

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
