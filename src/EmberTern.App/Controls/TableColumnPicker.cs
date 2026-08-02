using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;

namespace EmberTern.App.Controls;

/// <summary>
/// Two-pane "Table column" tab for the merged Domena/Kolumna picker (B1): a filterable
/// table list on the left, the selected table's columns (lazily loaded) on the right.
/// Picking a column commits a <see cref="ColumnRef"/> to the host SearchableComboBox →
/// the field's TypeOf becomes <c>COLUMN TABLE.COLUMN</c> (TYPE OF COLUMN). Never eager-
/// loads every column of every table.
/// </summary>
public sealed class TableColumnPicker : UserControl, ISearchableComboBoxContent
{
    public static readonly StyledProperty<IEnumerable?> TablesProperty =
        AvaloniaProperty.Register<TableColumnPicker, IEnumerable?>(nameof(Tables));

    public IEnumerable? Tables
    {
        get => GetValue(TablesProperty);
        set => SetValue(TablesProperty, value);
    }

    public static readonly StyledProperty<IColumnsLoader?> ColumnsLoaderProperty =
        AvaloniaProperty.Register<TableColumnPicker, IColumnsLoader?>(nameof(ColumnsLoader));

    public IColumnsLoader? ColumnsLoader
    {
        get => GetValue(ColumnsLoaderProperty);
        set => SetValue(ColumnsLoaderProperty, value);
    }

    public Action<object?>? CommitRequested { get; set; }

    private readonly TextBox _tableFilter;
    private readonly TextBox _columnFilter;
    private readonly ListBox _tableList;
    private readonly ListBox _columnList;
    private List<ColumnSpec> _columns = new();
    private string? _selectedTable;

    public TableColumnPicker()
    {
        // ⚠ TA SAMA REGUŁA CO W `SearchableComboBox` — filtr JEST polem wyszukiwania. Krok 11 nadał
        // klasę tylko zakładce Domain i użytkownik natychmiast znalazł pominiętą zakładkę Column:
        // reguła była poprawna, brakowało JEDNEJ instancji. Oba filtry tej kontrolki biorą ją teraz
        // w jednym miejscu, więc nie da się już rozjechać ich pojedynczo.
        _tableFilter = new TextBox { PlaceholderText = "Filter tables…", Margin = new Thickness(4) };
        _tableFilter.Classes.Add("search");
        _tableList = new ListBox { MaxHeight = 320 };
        _columnFilter = new TextBox { PlaceholderText = "Filter columns…", Margin = new Thickness(4) };
        _columnFilter.Classes.Add("search");
        _columnList = new ListBox { MaxHeight = 320, ItemTemplate = ColumnRowTemplate() };

        _tableFilter.AddHandler(TextBox.TextChangedEvent, (_, _) => RefreshTables());
        _columnFilter.AddHandler(TextBox.TextChangedEvent, (_, _) => RefreshColumns());
        _tableList.SelectionChanged += OnTableSelected;
        _columnList.AddHandler(InputElement.PointerReleasedEvent, OnColumnReleased, handledEventsToo: true);
        _columnList.AddHandler(InputElement.KeyDownEvent, OnColumnKeyDown);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), MinWidth = 460, Height = 380 };
        var left = Pane("Tables", _tableFilter, _tableList);
        var right = Pane("Columns", _columnFilter, _columnList);
        right[Grid.ColumnProperty] = 2;
        var splitter = new GridSplitter { Width = 1, [Grid.ColumnProperty] = 1 };
        root.Children.Add(left);
        root.Children.Add(splitter);
        root.Children.Add(right);
        Content = root;
    }

    // Per-instance reaction to Tables changing — NOT AddClassHandler in the ctor (that
    // registers a NEW global class handler for every cell the DataGrid realizes → leak +
    // O(n²) refilters).
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TablesProperty) RefreshTables();
    }

    private static Control Pane(string header, TextBox filter, ListBox list)
    {
        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        var caption = new TextBlock
        {
            Text = header, FontSize = 10, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(6, 4, 6, 0), [Grid.RowProperty] = 0,
        };
        caption[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SubtleForegroundBrush");
        filter[Grid.RowProperty] = 1;
        list[Grid.RowProperty] = 2;
        g.Children.Add(caption);
        g.Children.Add(filter);
        g.Children.Add(list);
        return g;
    }

    private static FuncDataTemplate<ColumnSpec> ColumnRowTemplate()
        => new((_, _) =>
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(8, 2) };
            var name = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            name[!TextBlock.TextProperty] = new Binding(nameof(ColumnSpec.Name));
            var type = new TextBlock
            {
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0), [Grid.ColumnProperty] = 1,
            };
            type[!TextBlock.TextProperty] = new Binding(nameof(ColumnSpec.Type));
            type[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SubtleForegroundBrush");
            g.Children.Add(name);
            g.Children.Add(type);
            return g;
        });

    private void RefreshTables()
        => _tableList.ItemsSource = SearchableComboBox.FilterItems(Tables, null, _tableFilter.Text);

    private void OnTableSelected(object? sender, SelectionChangedEventArgs e)
    {
        _selectedTable = _tableList.SelectedItem as string;
        // Defer: RefreshColumns changes _columnList.ItemsSource, which is illegal synchronously
        // inside the table list's selection-model update ("Cannot change source while update is
        // in progress" → unhandled → silent app crash). Same hazard as the tab-change path.
        var table = _selectedTable;
        Dispatcher.UIThread.Post(() => LoadColumnsForAsync(table));
    }

    private async void LoadColumnsForAsync(string? table)
    {
        _columns = new List<ColumnSpec>();
        RefreshColumns();
        if (table is null || ColumnsLoader is null) return;
        try
        {
            var cols = await ColumnsLoader.LoadColumnsAsync(table).ConfigureAwait(true);
            // Ignore a stale load if the user moved to a different table while awaiting.
            if (!string.Equals(table, _selectedTable, StringComparison.Ordinal)) return;
            _columns = cols.ToList();
            RefreshColumns();
        }
        catch
        {
            _columns = new List<ColumnSpec>();
            RefreshColumns();
        }
    }

    private void RefreshColumns()
        => _columnList.ItemsSource = SearchableComboBox.FilterItems(_columns, nameof(ColumnSpec.Name), _columnFilter.Text);

    private void OnColumnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (e.Source is Visual v && v.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: ColumnSpec col })
            CommitColumn(col);
    }

    private void OnColumnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _columnList.SelectedItem is ColumnSpec col) { CommitColumn(col); e.Handled = true; }
    }

    private void CommitColumn(ColumnSpec col)
    {
        if (_selectedTable is null) return;
        CommitRequested?.Invoke(new ColumnRef(_selectedTable, col.Name, col.Type));
    }
}
