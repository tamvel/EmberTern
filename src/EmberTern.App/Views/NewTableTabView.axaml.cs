using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using AvaloniaEdit;

using EmberTern.App.Behaviors;
using EmberTern.App.Completion;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

public partial class NewTableTabView : UserControl
{
    public NewTableTabView()
    {
        InitializeComponent();

        // The Live DDL preview is the app's shared read-only SQL surface (see the comment at its
        // declaration): one call wires the semantic + lexical layers, keeps them following the theme, and
        // pushes the VM's DdlPreview in. The same call serves the four constraint/index dialogs.
        if (this.FindControl<TextEditor>("DdlEditor") is { } ddl)
        {
            SqlEditorBehavior.AttachDdlPreview(ddl, this);
        }

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

            // The ONE seam (Enter gesture + cell-editor height role). This grid declares its columns in XAML,
            // so it never went through FieldGridColumns.Build and never received the height role — the same
            // silent miss as Table Detail's fields grid (S-1a + S-3).
            EditableGridBehavior.Attach(grid);
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
