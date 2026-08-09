using EmberTern.App.Localization;
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
        // ⭐⭐ THE `field-grid` CLASS IS NO LONGER APPLIED HERE — it moved to
        // Behaviors.EditableGridBehavior.Attach (stabilization sprint S-3, 2026-08-05), and the move IS the
        // fix rather than tidying.
        //
        // The class carries the in-cell editor height role, and applying it here made its scope "whoever
        // calls this builder". Table Detail Fields, New Table Fields and View Detail Columns build their
        // columns in XAML and only INSERT the shared picker column, so they never called it and never got the
        // role — their DataGridTextColumn editing TextBox stayed at MinHeight 0 inside a 34 px row. That is
        // the reported "the TextBox in Table is still too low", and the old comment here even described the
        // scope as "a class on the grid, applied in one place" — which was true, and was the problem: the one
        // place was not every place.
        //
        // ⛔ Do not re-add it here. Two owners of one class means the grids that go through only one of them
        // are silently different again, which is exactly the defect that took two rounds to find.
        grid.Columns.Clear();
        // The function Result is a single, unnamed return value — its grid omits Name.
        if (includeName)
            grid.Columns.Add(TextCol(nameof(UiStrings.TableDetailColumnName), nameof(ProcedureFieldRowBase.Name), 130));
        grid.Columns.Add(TypeComboCol(nameof(UiStrings.TableDetailColumnType), 110));
        grid.Columns.Add(MergedTypeSourceColumn.Build(nameof(UiStrings.FieldTypeSourceHeader), 150));
        grid.Columns.Add(TextEditCol(nameof(UiStrings.TableDetailColumnSize), nameof(ProcedureFieldRowBase.Size), 60, nameof(ProcedureFieldRowBase.IsSizeEnabled)));
        grid.Columns.Add(TextEditCol(nameof(UiStrings.TableDetailColumnScale), nameof(ProcedureFieldRowBase.Scale), 60, nameof(ProcedureFieldRowBase.IsScaleEnabled)));
        grid.Columns.Add(TextEditCol(nameof(UiStrings.ProcedureFieldSubType), nameof(ProcedureFieldRowBase.SubType), 80, nameof(ProcedureFieldRowBase.IsSubTypeEnabled)));
        grid.Columns.Add(TextEditCol(nameof(UiStrings.ProcedureFieldCharset), nameof(ProcedureFieldRowBase.Charset), 90, nameof(ProcedureFieldRowBase.IsCharsetEnabled)));
        grid.Columns.Add(CheckCol(nameof(UiStrings.TableDetailColumnNotNull), nameof(ProcedureFieldRowBase.NotNull), 70));
        grid.Columns.Add(TextCol(nameof(UiStrings.ProcedureFieldCollate), nameof(ProcedureFieldRowBase.Collate), 90));
        if (includeDefault)
            grid.Columns.Add(TextCol(nameof(UiStrings.TableDetailColumnDefault), nameof(ProcedureFieldRowBase.DefaultValue), 110));
        grid.Columns.Add(TextCol(nameof(UiStrings.ProcedureFieldDescription), nameof(ProcedureFieldRowBase.Description), 140));
    }

    private static DataGridTextColumn TextCol(string headerKey, string path, int min)
        => LocalizedColumn.Header(new DataGridTextColumn { Binding = new Binding(path) { Mode = BindingMode.TwoWay }, MinWidth = min }, headerKey);

    private static DataGridCheckBoxColumn CheckCol(string headerKey, string path, int min)
        => LocalizedColumn.Header(new DataGridCheckBoxColumn { Binding = new Binding(path) { Mode = BindingMode.TwoWay }, MinWidth = min }, headerKey);

    // Always-visible TextBox in the cell (IsReadOnly column) so the per-row IsEnabled
    // gate can disable it — a DataGridTextColumn supports only per-COLUMN IsReadOnly
    // (gotcha #83/#124).
    private static DataGridTemplateColumn TextEditCol(string headerKey, string path, int min, string enabledPath)
        => LocalizedColumn.Header(new DataGridTemplateColumn
        {
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
        }, headerKey);

    // Type: small closed dictionary → plain ComboBox with type-ahead (no filtering).
    // Always-visible in CellTemplate (gotcha #56), bound to the null-safe SelectedTypeItem
    // wrapper, disabled when a domain/TYPE OF governs the type (#4).
    private static DataGridTemplateColumn TypeComboCol(string headerKey, int min)
        => LocalizedColumn.Header(new DataGridTemplateColumn
        {
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
        }, headerKey);

}
