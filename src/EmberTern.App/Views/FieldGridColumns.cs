using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
/// One definition of the field model (Name / Type / Domain-or-Column / Size / Scale /
/// Sub Type / Charset / Not Null / Collate / Default / Description) so there's no second
/// type system and the grids stay identical across object editors.
///
/// Type is a plain <see cref="ComboBox"/> (small closed dictionary). The merged
/// "Domain / Column" cell is a two-tab <see cref="SearchableComboBox"/> (domain list +
/// table-column picker for TYPE OF COLUMN) replacing the separate Domain + TYPE OF
/// columns. The type-construction cells (Type / Size / Scale / Sub Type / Charset)
/// disable when a domain or TYPE OF COLUMN governs the type (#4).
/// </summary>
internal static class FieldGridColumns
{
    public static void Build(DataGrid grid, bool includeDefault, bool includeName = true)
    {
        grid.Columns.Clear();
        // The function Result is a single, unnamed return value — its grid omits Name.
        if (includeName)
            grid.Columns.Add(TextCol(UiStrings.TableDetailColumnName, nameof(ProcedureFieldRowBase.Name), 130));
        grid.Columns.Add(TypeComboCol(UiStrings.TableDetailColumnType, 110));
        grid.Columns.Add(MergedTypeSourceColumn.Build(UiStrings.FieldTypeSourceHeader, 150));
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
                // ⭐⭐ ŻADNEJ CHROMY TUTAJ — całość niesie styl `DataGridCell TextBox`.
                // ⚠⚠ To nie jest porządkowanie, tylko NAPRAWA (§19.9). Ta metoda ustawiała
                // `VerticalAlignment`, `VerticalContentAlignment`, `Padding`, `BorderThickness`
                // i `Background` jako WARTOŚCI LOKALNE, a wartość lokalna BIJE SETTER STYLU — więc
                // styl nie mógł ich dosięgnąć. Zmierzone: komórka 30 px, `TextBox` 12 px, `VA=Center`
                // mimo `Stretch` w stylu. Pole czytało się jak cienki pasek wrzucony w wiersz, obok
                // `ComboBoxa`, który wysokość bierze ze swojego stylu (`Size.Control`).
                // ⛔ Nie przywracać tu ani jednej z tych właściwości — to dokładnie ten mechanizm,
                // przez który `MessageBanner` dorobił się sześciu wariantów chromy per host.
                var tb = new TextBox { Classes = { "field-editor" } };
                tb.Bind(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay });
                tb.Bind(InputElement.IsEnabledProperty, new Binding(enabledPath));
                return tb;
            }),
        };

    // Type: small closed dictionary → plain ComboBox with type-ahead (no filtering).
    // Always-visible in CellTemplate (gotcha #56), bound to the null-safe SelectedTypeItem
    // wrapper, disabled when a domain/TYPE OF governs the type (#4).
    private static DataGridTemplateColumn TypeComboCol(string header, int min)
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
                    IsTextSearchEnabled = true,
                };
                cb.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(ProcedureFieldRowBase.BasicTypes)));
                cb.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(ProcedureFieldRowBase.SelectedTypeItem)) { Mode = BindingMode.TwoWay });
                cb.Bind(InputElement.IsEnabledProperty, new Binding(nameof(ProcedureFieldRowBase.IsTypeEnabled)));
                return cb;
            }),
        };

}
