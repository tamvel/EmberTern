using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>
/// Code-behind for the Data Import surface. It does exactly one thing the VM cannot: build the source
/// preview's columns, whose SHAPE is only known once a source has been read.
/// <para>
/// This is the same dynamic-column pattern the SQL results and Table Data grids already use — the VM
/// publishes the fields, the view turns them into <see cref="DataGridColumn"/>s. A column is an Avalonia
/// type, so it cannot live in a ViewModel (rule #1).
/// </para>
/// </summary>
public partial class DataImportTabView : UserControl
{
    private DataImportTabViewModel? _bound;

    public DataImportTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null) _bound.PreviewSchemaChanged -= OnPreviewSchemaChanged;

        _bound = DataContext as DataImportTabViewModel;
        if (_bound is null) return;

        _bound.PreviewSchemaChanged += OnPreviewSchemaChanged;
        RebuildPreviewColumns();
    }

    private void OnPreviewSchemaChanged(object? sender, EventArgs e) => RebuildPreviewColumns();

    /// <summary>
    /// Rebuilds the preview grid to match the source's current shape.
    /// <para>
    /// The row number comes first and is the SOURCE's own numbering — the number the user can find in their
    /// file — because that is the number every error message will quote (§0.6).
    /// </para>
    /// </summary>
    private void RebuildPreviewColumns()
    {
        if (this.FindControl<DataGrid>("SourcePreviewGrid") is not { } grid || _bound is null) return;

        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = UiStrings.ImportRowNumberColumn,
            Binding = new Binding(nameof(ImportSourceRecordRowViewModel.SourceRowNumber)),
            IsReadOnly = true,
        });

        for (var i = 0; i < _bound.PreviewFields.Count; i++)
        {
            var field = _bound.PreviewFields[i];
            grid.Columns.Add(new DataGridTextColumn
            {
                // A generated positional label is shown as-is; a real header keeps the source's own spelling,
                // because that is what the user will look for when mapping.
                Header = field.Name,
                Binding = new Binding($"Values[{i.ToString(CultureInfo.InvariantCulture)}]"),
                IsReadOnly = true,
            });
        }
    }
}
