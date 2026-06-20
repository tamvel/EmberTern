using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;

namespace EmberTern.App.Views;

/// <summary>
/// Builds the full field-definition DataGrid columns shared by every editable
/// field/parameter/variable grid (Procedure params + Variables, Trigger Variables).
/// One definition of the 12-column model (Name / Type combo / TYPE OF / Domain combo /
/// Size / Scale / Sub Type / Charset / Not Null / Collate / Default / Description) so
/// there's no second type system and the grids stay identical across object editors.
/// </summary>
internal static class FieldGridColumns
{
    public static void Build(DataGrid grid, bool includeDefault)
    {
        grid.Columns.Clear();
        grid.Columns.Add(TextCol(UiStrings.TableDetailColumnName, nameof(ProcedureFieldRowBase.Name), 130));
        grid.Columns.Add(ComboCol(UiStrings.TableDetailColumnType, nameof(ProcedureFieldRowBase.BasicTypes), nameof(ProcedureFieldRowBase.BaseType), 110, typeAhead: true, itemAsString: true));
        grid.Columns.Add(TextCol(UiStrings.ProcedureFieldTypeOf, nameof(ProcedureFieldRowBase.TypeOf), 110));
        grid.Columns.Add(ComboCol(UiStrings.TableDetailColumnDomain, nameof(ProcedureFieldRowBase.AvailableDomains), nameof(ProcedureFieldRowBase.SelectedDomainSpec), 130, typeAhead: false, itemAsString: false));
        grid.Columns.Add(TextCol(UiStrings.TableDetailColumnSize, nameof(ProcedureFieldRowBase.Size), 60));
        grid.Columns.Add(TextCol(UiStrings.TableDetailColumnScale, nameof(ProcedureFieldRowBase.Scale), 60));
        grid.Columns.Add(TextCol(UiStrings.ProcedureFieldSubType, nameof(ProcedureFieldRowBase.SubType), 80));
        grid.Columns.Add(TextCol(UiStrings.ProcedureFieldCharset, nameof(ProcedureFieldRowBase.Charset), 90));
        grid.Columns.Add(CheckCol(UiStrings.TableDetailColumnNotNull, nameof(ProcedureFieldRowBase.NotNull), 70));
        grid.Columns.Add(TextCol(UiStrings.ProcedureFieldCollate, nameof(ProcedureFieldRowBase.Collate), 90));
        if (includeDefault)
            grid.Columns.Add(TextCol(UiStrings.TableDetailColumnDefault, nameof(ProcedureFieldRowBase.DefaultValue), 110));
        grid.Columns.Add(TextCol(UiStrings.ProcedureFieldDescription, nameof(ProcedureFieldRowBase.Description), 140));
    }

    private static DataGridTextColumn TextCol(string header, string path, int min)
        => new() { Header = header, Binding = new Binding(path) { Mode = BindingMode.TwoWay }, MinWidth = min };

    private static DataGridCheckBoxColumn CheckCol(string header, string path, int min)
        => new() { Header = header, Binding = new Binding(path) { Mode = BindingMode.TwoWay }, MinWidth = min };

    // Always-visible ComboBox in the cell (IsReadOnly column) — same pattern as the
    // table field grids (gotcha #56). itemsPath = the row's list property; selectedPath
    // = the row's bound value. itemAsString true → string items (Type); false → DomainSpec
    // items shown by Name (Domain).
    private static DataGridTemplateColumn ComboCol(string header, string itemsPath, string selectedPath, int min, bool typeAhead, bool itemAsString)
        => new()
        {
            Header = header,
            MinWidth = min,
            IsReadOnly = true,
            CellTemplate = new FuncDataTemplate<ProcedureFieldRowBase>((_, _) =>
            {
                var cb = new ComboBox
                {
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsTextSearchEnabled = typeAhead,
                };
                cb.Bind(ItemsControl.ItemsSourceProperty, new Binding(itemsPath));
                cb.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(selectedPath) { Mode = BindingMode.TwoWay });
                if (!itemAsString)
                {
                    cb.ItemTemplate = new FuncDataTemplate<DomainSpec>((_, _) =>
                        new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(DomainSpec.Name)) });
                }
                return cb;
            }),
        };
}
