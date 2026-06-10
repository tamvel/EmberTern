using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.ViewModels;
using EmberTern.Core.Query;

namespace EmberTern.App.Views;

public partial class TableDetailTabView : UserControl
{
    private TextEditor? _ddlEditor;
    private DataGrid? _dataPreviewGrid;
    private TableDetailTabViewModel? _currentVm;

    public TableDetailTabView()
    {
        InitializeComponent();
        _ddlEditor = this.FindControl<TextEditor>("TableDetailDdlEditor");
        _dataPreviewGrid = this.FindControl<DataGrid>("DataPreviewGrid");
        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged -= OnVmPropertyChanged;
        }
        _currentVm = DataContext as TableDetailTabViewModel;
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            PushDdl();
            PopulateDataGrid(_currentVm.DataResult);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableDetailTabViewModel.DdlText))
        {
            PushDdl();
        }
        else if (e.PropertyName == nameof(TableDetailTabViewModel.DataResultVersionTag))
        {
            PopulateDataGrid(_currentVm?.DataResult);
        }
    }

    // DataGrid columns are imperative — we can't bind them from XAML. Mirrors
    // MainWindow.PopulateResultGrid: clear, rebuild typed columns from
    // QueryResult.Columns, point ItemsSource at the object?[] row list.
    private void PopulateDataGrid(QueryResult? result)
    {
        if (_dataPreviewGrid is null) return;

        _dataPreviewGrid.Columns.Clear();
        _dataPreviewGrid.ItemsSource = null;

        if (result is null || !result.HasResultSet) return;

        for (int i = 0; i < result.Columns.Count; i++)
        {
            var column = result.Columns[i];
            _dataPreviewGrid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Name,
                Binding = new Binding($"[{i}]")
                {
                    StringFormat = "{0}",
                    FallbackValue = string.Empty,
                    TargetNullValue = string.Empty,
                },
            });
        }

        _dataPreviewGrid.ItemsSource = result.Rows;
    }

    private void PushDdl()
    {
        if (_ddlEditor is null || _currentVm is null) return;
        var text = _currentVm.DdlText ?? string.Empty;
        if (_ddlEditor.Text != text)
        {
            _ddlEditor.Text = text;
        }
    }

    private void OnDependencyNodeDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DependencyLeafNode leaf } && _currentVm is not null)
        {
            _currentVm.RequestOpen(leaf);
            e.Handled = true;
        }
    }

    private void ApplyEditorTheme()
    {
        if (_ddlEditor is null) return;
        var theme = ActualThemeVariant;
        var name = theme == ThemeVariant.Light
            ? App.FirebirdSyntaxLightName
            : App.FirebirdSyntaxName;
        var syntax = HighlightingManager.Instance.GetDefinition(name);
        _ddlEditor.SyntaxHighlighting = syntax;

        if (Application.Current?.Resources.TryGetResource("SelectionBrush", theme, out var res) == true
            && res is IBrush brush)
        {
            _ddlEditor.TextArea.SelectionBrush = brush;
        }
    }
}
