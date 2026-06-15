using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.ViewModels;
using EmberTern.Core.Query;

namespace EmberTern.App.Views;

public partial class ViewDetailTabView : UserControl
{
    private TextEditor? _sqlEditor;
    private TextEditor? _ddlEditor;
    private DataGrid? _dataPreviewGrid;
    private ViewDetailTabViewModel? _currentVm;
    private readonly List<string> _dataPreviewColumnNames = new();
    // Guards the editor↔VM feedback loop: while we push SourceText INTO the
    // editor, the TextChanged handler must not write it back. Same pattern as
    // the main SqlEditor sync.
    private bool _suppressSourceSync;

    public ViewDetailTabView()
    {
        InitializeComponent();
        _sqlEditor = this.FindControl<TextEditor>("ViewSqlEditor");
        _ddlEditor = this.FindControl<TextEditor>("ViewDdlEditor");
        _dataPreviewGrid = this.FindControl<DataGrid>("DataPreviewGrid");
        if (_sqlEditor is not null)
        {
            _sqlEditor.TextChanged += OnSqlEditorTextChanged;
            // Alt+F formats the source — same gesture as the SQL Editor. Handled
            // in code-behind because the global window-level Alt+F binding targets
            // the SQL Editor's VM; a focused-editor KeyDown is the reliable route.
            _sqlEditor.KeyDown += OnSqlEditorKeyDown;
        }
        if (_dataPreviewGrid is not null)
        {
            _dataPreviewGrid.Sorting += OnDataPreviewSorting;
        }
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
        _currentVm = DataContext as ViewDetailTabViewModel;
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            // Wire the same selection-aware format callbacks the SQL Editor uses,
            // so FormatSqlCommand formats the selection-or-all against this editor.
            _currentVm.SelectedTextProvider = GetSqlEditorSelection;
            _currentVm.ReplaceSelectedOrAllText = ReplaceSqlEditorSelectionOrAll;
            PushSource();
            PushDdl();
            PopulateDataGrid();
        }
    }

    private void OnSqlEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Alt && _currentVm is not null)
        {
            if (_currentVm.FormatSqlCommand.CanExecute(null))
            {
                _currentVm.FormatSqlCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    // Selection in the SQL editor, or null when nothing is selected.
    private string? GetSqlEditorSelection()
    {
        if (_sqlEditor is null) return null;
        var sel = _sqlEditor.SelectedText;
        return string.IsNullOrEmpty(sel) ? null : sel;
    }

    // Replace the selection with the formatted text (re-selecting it), or
    // overwrite the whole document when there's no selection. Editor TextChanged
    // then syncs SourceText back to the VM.
    private void ReplaceSqlEditorSelectionOrAll(string text)
    {
        if (_sqlEditor is null) return;
        if (_sqlEditor.SelectionLength > 0)
        {
            var start = _sqlEditor.SelectionStart;
            _sqlEditor.Document.Replace(start, _sqlEditor.SelectionLength, text);
            _sqlEditor.Select(start, text.Length);
        }
        else
        {
            _sqlEditor.Text = text;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewDetailTabViewModel.SourceText))
        {
            PushSource();
        }
        else if (e.PropertyName == nameof(ViewDetailTabViewModel.DdlText))
        {
            PushDdl();
        }
        else if (e.PropertyName == nameof(ViewDetailTabViewModel.DataResultVersionTag))
        {
            PopulateDataGrid();
        }
    }

    private void OnSqlEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressSourceSync || _currentVm is null || _sqlEditor is null) return;
        _currentVm.SourceText = _sqlEditor.Text;
    }

    private void PushSource()
    {
        if (_sqlEditor is null || _currentVm is null) return;
        var text = _currentVm.SourceText ?? string.Empty;
        if (_sqlEditor.Text == text) return;
        _suppressSourceSync = true;
        try { _sqlEditor.Text = text; }
        finally { _suppressSourceSync = false; }
    }

    private void PushDdl()
    {
        if (_ddlEditor is null || _currentVm is null) return;
        var text = _currentVm.DdlText ?? string.Empty;
        if (_ddlEditor.Text != text) _ddlEditor.Text = text;
    }

    // Read-only data preview — imperative columns (can't bind DataGrid columns
    // from XAML) over object?[] rows, same shape as MainWindow.PopulateResultGrid
    // and TableDetailTabView. No editing template — a view's data is read-only.
    private void PopulateDataGrid()
    {
        if (_dataPreviewGrid is null) return;
        var result = _currentVm?.DataResult;

        if (result is null || !result.HasResultSet)
        {
            _dataPreviewGrid.Columns.Clear();
            _dataPreviewGrid.ItemsSource = null;
            _dataPreviewColumnNames.Clear();
            return;
        }

        bool sameStructure = _dataPreviewColumnNames.Count == result.Columns.Count;
        if (sameStructure)
        {
            for (int i = 0; i < result.Columns.Count; i++)
            {
                if (!string.Equals(result.Columns[i].Name, _dataPreviewColumnNames[i], StringComparison.Ordinal))
                {
                    sameStructure = false;
                    break;
                }
            }
        }

        if (!sameStructure)
        {
            _dataPreviewGrid.Columns.Clear();
            _dataPreviewColumnNames.Clear();
            for (int i = 0; i < result.Columns.Count; i++)
            {
                int columnIndex = i; // closure capture
                var columnName = result.Columns[i].Name;
                _dataPreviewColumnNames.Add(columnName);
                _dataPreviewGrid.Columns.Add(new DataGridTemplateColumn
                {
                    Header = columnName,
                    SortMemberPath = columnName,
                    CustomSortComparer = new RowIndexComparer(columnIndex),
                    CanUserSort = true,
                    CellTemplate = BuildTextCellTemplate(columnIndex),
                });
            }
        }

        _dataPreviewGrid.ItemsSource = result.Rows;
    }

    private static IDataTemplate BuildTextCellTemplate(int columnIndex)
        => new FuncDataTemplate<object?[]>((row, _) =>
        {
            var tb = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0),
            };
            if (row is null || columnIndex >= row.Length) return tb;
            var value = row[columnIndex];
            if (value is null)
            {
                tb.Text = UiStrings.TableDetailDataPreviewNullPlaceholder;
                tb.Classes.Add("null-cell");
            }
            else
            {
                tb.Text = value.ToString();
            }
            return tb;
        });

    private void OnDataPreviewSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (_currentVm is null || _dataPreviewGrid is null) return;
        var index = _dataPreviewGrid.Columns.IndexOf(e.Column);
        if (index < 0 || index >= _dataPreviewColumnNames.Count) return;
        // Leave Handled=false so Avalonia paints the header arrow + does the local
        // in-memory sort; the VM kicks off a server-side ORDER BY reload (same
        // ordering, so the row swap is visually a no-op).
        _ = _currentVm.ApplyColumnSortAsync(_dataPreviewColumnNames[index]);
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
        var theme = ActualThemeVariant;
        var name = theme == ThemeVariant.Light
            ? App.FirebirdSyntaxLightName
            : App.FirebirdSyntaxName;
        var syntax = HighlightingManager.Instance.GetDefinition(name);
        IBrush? selection = null;
        if (Application.Current?.Resources.TryGetResource("SelectionBrush", theme, out var res) == true
            && res is IBrush brush)
        {
            selection = brush;
        }
        ApplyToEditor(_sqlEditor, syntax, selection);
        ApplyToEditor(_ddlEditor, syntax, selection);
    }

    private static void ApplyToEditor(TextEditor? editor, IHighlightingDefinition? syntax, IBrush? selection)
    {
        if (editor is null) return;
        editor.SyntaxHighlighting = syntax;
        if (selection is not null) editor.TextArea.SelectionBrush = selection;
    }
}
