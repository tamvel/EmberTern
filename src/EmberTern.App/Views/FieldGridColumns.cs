using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EmberTern.App.Controls;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;

namespace EmberTern.App.Views;

/// <summary>
/// Builds the full field-definition DataGrid columns shared by every editable
/// field/parameter/variable grid (Procedure params + Variables, Trigger Variables).
/// One definition of the 12-column model (Name / Type / TYPE OF / Domain /
/// Size / Scale / Sub Type / Charset / Not Null / Collate / Default / Description) so
/// there's no second type system and the grids stay identical across object editors.
///
/// Type and Domain are <see cref="SearchablePicker"/> (filtering AutoCompleteBox) — the
/// app-wide standard for picking objects (#1). The type-construction cells (Type / TYPE OF /
/// Size / Scale / Sub Type / Charset) disable when a domain or TYPE OF governs the type (#4).
/// </summary>
internal static class FieldGridColumns
{
    public static void Build(DataGrid grid, bool includeDefault)
    {
        grid.Columns.Clear();
        grid.Columns.Add(TextCol(UiStrings.TableDetailColumnName, nameof(ProcedureFieldRowBase.Name), 130));
        grid.Columns.Add(PickerCol(UiStrings.TableDetailColumnType, nameof(ProcedureFieldRowBase.BasicTypes), nameof(ProcedureFieldRowBase.SelectedTypeItem), 110,
            enabledPath: nameof(ProcedureFieldRowBase.IsTypeEnabled), valueMember: null));
        grid.Columns.Add(TextEditCol(UiStrings.ProcedureFieldTypeOf, nameof(ProcedureFieldRowBase.TypeOf), 110, nameof(ProcedureFieldRowBase.IsTypeOfEnabled)));
        grid.Columns.Add(PickerCol(UiStrings.TableDetailColumnDomain, nameof(ProcedureFieldRowBase.AvailableDomains), nameof(ProcedureFieldRowBase.SelectedDomainSpec), 130,
            enabledPath: null, valueMember: nameof(DomainSpec.Name)));
        grid.Columns.Add(TextEditCol(UiStrings.TableDetailColumnSize, nameof(ProcedureFieldRowBase.Size), 60, nameof(ProcedureFieldRowBase.IsSizeEnabled)));
        grid.Columns.Add(TextEditCol(UiStrings.TableDetailColumnScale, nameof(ProcedureFieldRowBase.Scale), 60, nameof(ProcedureFieldRowBase.IsScaleEnabled)));
        grid.Columns.Add(TextEditCol(UiStrings.ProcedureFieldSubType, nameof(ProcedureFieldRowBase.SubType), 80, nameof(ProcedureFieldRowBase.IsSubTypeEnabled)));
        grid.Columns.Add(TextEditCol(UiStrings.ProcedureFieldCharset, nameof(ProcedureFieldRowBase.Charset), 90, nameof(ProcedureFieldRowBase.IsCharsetEnabled)));
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

    // Always-visible TextBox in the cell (IsReadOnly column) so the per-row IsEnabled
    // gate can disable it — a DataGridTextColumn supports only per-COLUMN IsReadOnly
    // (gotcha #83/#124).
    private static DataGridTemplateColumn TextEditCol(string header, string path, int min, string enabledPath)
        => new()
        {
            Header = header,
            MinWidth = min,
            IsReadOnly = true,
            CellTemplate = new FuncDataTemplate<ProcedureFieldRowBase>((_, _) =>
            {
                var tb = new TextBox
                {
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    VerticalAlignment = VerticalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(4, 0),
                };
                tb.Bind(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay });
                tb.Bind(InputElement.IsEnabledProperty, new Binding(enabledPath));
                return tb;
            }),
        };

    // Always-visible filtering picker in the cell (IsReadOnly column) — same always-visible
    // pattern as the table field grids (gotcha #56), but a SearchablePicker (AutoCompleteBox)
    // so the user can type-to-filter a large domain/type list (#1). valueMember = the text
    // member used for filtering/display on object items (Domain → Name); null for string
    // items (Type). enabledPath = optional per-row IsEnabled gate (#4).
    private static DataGridTemplateColumn PickerCol(string header, string itemsPath, string selectedPath, int min, string? enabledPath, string? valueMember)
        => new()
        {
            Header = header,
            MinWidth = min,
            IsReadOnly = true,
            CellTemplate = new FuncDataTemplate<ProcedureFieldRowBase>((_, _) =>
            {
                var picker = new SearchablePicker
                {
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                picker.Bind(AutoCompleteBox.ItemsSourceProperty, new Binding(itemsPath));
                picker.Bind(AutoCompleteBox.SelectedItemProperty, new Binding(selectedPath) { Mode = BindingMode.TwoWay });
                if (valueMember is not null)
                {
                    picker.ValueMemberBinding = new Binding(valueMember);
                    picker.ItemTemplate = new FuncDataTemplate<DomainSpec>((_, _) =>
                        new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(DomainSpec.Name)) });
                }
                if (enabledPath is not null)
                    picker.Bind(InputElement.IsEnabledProperty, new Binding(enabledPath));
                return picker;
            }),
        };
}
