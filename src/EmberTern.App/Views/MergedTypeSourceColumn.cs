using EmberTern.App.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using EmberTern.App.Controls;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;

namespace EmberTern.App.Views;

/// <summary>
/// Builds the merged "Domena/Kolumna" DataGrid column shared by EVERY field grid
/// (Procedure/Trigger via <see cref="FieldGridColumns"/>, Table Detail, New Table) so all
/// editors get the identical two-tab picker: a rich Domain list + a two-pane
/// <see cref="TableColumnPicker"/> (TYPE OF COLUMN). The cell binds only the
/// <see cref="SearchableComboBox"/> itself (SelectedTypeSource / TypeSourceDisplay, by
/// name — works for any <see cref="ITypeSourceRow"/>); the sections + picker aren't in the
/// visual tree, so their data is set imperatively from the row on DataContextChanged.
/// </summary>
internal static class MergedTypeSourceColumn
{
    public static DataGridTemplateColumn Build(string headerKey, int minWidth)
        => EmberTern.App.Localization.LocalizedColumn.Header(new DataGridTemplateColumn
        {
            MinWidth = minWidth,
            IsReadOnly = true,
            CellTemplate = new FuncDataTemplate<object>((_, _) => BuildCell()),
        }, headerKey);

    private static Control BuildCell()
    {
        var domainSection = new SearchableComboBoxSection
        {
            DisplayMemberPath = nameof(DomainSpec.Name),
            ItemTemplate = PickerTemplate("DomainRowTemplate"),
            HeaderTemplate = PickerTemplate("DomainHeaderTemplate"),
        };
        domainSection.Bind(SearchableComboBoxSection.HeaderProperty,
            new LocExtension(nameof(UiStrings.FieldTypeSourceDomainTab)).ProvideValue());
        var tablePicker = new TableColumnPicker();
        var columnSection = new SearchableComboBoxSection
        {
            Content = tablePicker,
        };

        columnSection.Bind(SearchableComboBoxSection.HeaderProperty,
            new LocExtension(nameof(UiStrings.FieldTypeSourceColumnTab)).ProvideValue());

        var picker = new SearchableComboBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Watermark = string.Empty,
        };
        picker.Sections.Add(domainSection);
        picker.Sections.Add(columnSection);

        picker.Bind(SearchableComboBox.SelectedItemProperty,
            new Binding("SelectedTypeSource") { Mode = BindingMode.TwoWay });
        picker.Bind(SearchableComboBox.SelectionBoxTextProperty,
            new Binding("TypeSourceDisplay"));

        void Populate()
        {
            if (picker.DataContext is ITypeSourceRow row)
            {
                domainSection.ItemsSource = row.AvailableDomains;
                tablePicker.Tables = row.AvailableTables;
                tablePicker.ColumnsLoader = row.ColumnsLoader;
            }
        }
        picker.DataContextChanged += (_, _) => Populate();
        Populate();
        return picker;
    }

    private static IDataTemplate? PickerTemplate(string key)
        => Application.Current?.Resources.TryGetResource(key, null, out var t) == true ? t as IDataTemplate : null;
}
