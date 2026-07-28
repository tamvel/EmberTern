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
using EmberTern.App.Completion;
using EmberTern.App.Sql;
using EmberTern.App.ViewModels;
using EmberTern.Core.Export;
using EmberTern.Core.Query;
using EmberTern.Core.Sql.Templates;

namespace EmberTern.App.Views;

public partial class ViewDetailTabView : UserControl
{
    private TextEditor? _sqlEditor;
    private TextEditor? _bodyEditor;
    private TextEditor? _ddlEditor;
    private DataGrid? _dataPreviewGrid;
    private ViewDetailTabViewModel? _currentVm;
    private readonly List<string> _dataPreviewColumnNames = new();
    // Guards the editor↔VM feedback loop: while we push SourceText/EditableBody INTO
    // an editor, the TextChanged handler must not write it back. Same pattern as the
    // main SqlEditor sync.
    private bool _suppressSourceSync;
    private bool _suppressBodySync;
    private bool _completionAttached;
    // Feeds this view's own Diagnostics sub-tab from the ACTIVE SQL document (S4). Only one of the two
    // editors is usable per mode, so ActiveEditor (already just the mode primary here) is the fallback.
    private readonly DiagnosticsPanelHost _diagnostics;

    public ViewDetailTabView()
    {
        InitializeComponent();
        _diagnostics = new DiagnosticsPanelHost(
            () => _currentVm?.DiagnosticsPanel,
            () => ActiveEditor,
            RevealEditor);
        _sqlEditor = this.FindControl<TextEditor>("ViewSqlEditor");
        _bodyEditor = this.FindControl<TextEditor>("ViewBodyEditor");
        _ddlEditor = this.FindControl<TextEditor>("ViewDdlEditor");
        if (_ddlEditor is not null) SqlEditorBehavior.AttachReadOnlyHighlighting(_ddlEditor);
        // S5: the panel's activation gestures navigate the active SQL document.
        var diagnosticsPanel = this.FindControl<DiagnosticsPanelView>("ViewDiagnosticsPanel");
        if (diagnosticsPanel is not null) diagnosticsPanel.Navigator = _diagnostics;
        _dataPreviewGrid = this.FindControl<DataGrid>("DataPreviewGrid");
        // Format is not wired here any more: it is CommandId.FormatSql (Ctrl+K), declared once in
        // Commands.CommandCatalog for this tab kind and routed to this VM's own FormatSqlCommand. The two
        // local Alt+F handlers this replaced existed only because the old window-level Alt+F binding
        // targeted the SQL Editor's view model and could not reach this one.
        if (_sqlEditor is not null) _sqlEditor.TextChanged += OnSqlEditorTextChanged;
        if (_bodyEditor is not null) _bodyEditor.TextChanged += OnBodyEditorTextChanged;
        if (_dataPreviewGrid is not null)
        {
            _dataPreviewGrid.Sorting += OnDataPreviewSorting;
            _dataPreviewGrid.CellPointerPressed += OnViewDataCellPointerPressed;
        }
        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Attach autocomplete + double-click/Ctrl+Click navigation to the editable
    // editors once the owning MainWindowViewModel is reachable. Reuses the SQL
    // Editor's services via SqlEditorBehavior — same wiring as ProcedureDetailTabView,
    // no second implementation.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_completionAttached) return;
        if (this.FindAncestorOfType<Window>()?.DataContext is MainWindowViewModel mainVm)
        {
            // Each editor is tracked by the Diagnostics host too, so this view's Diagnostics sub-tab
            // reflects whichever of them is the active SQL document (S4).
            if (_sqlEditor is not null)
            {
                _diagnostics.Track(_sqlEditor, SqlEditorBehavior.Attach(_sqlEditor, mainVm));
            }
            if (_bodyEditor is not null)
            {
                _diagnostics.Track(_bodyEditor, SqlEditorBehavior.Attach(_bodyEditor, mainVm));
            }

            // Metadata-object drop → snippet flyout. A view editor is SELECT/DDL, not PSQL.
            if (_sqlEditor is not null) SqlSnippetDropTarget.Attach(_sqlEditor, mainVm, SnippetInsertionContext.PlainSql);
            if (_bodyEditor is not null) SqlSnippetDropTarget.Attach(_bodyEditor, mainVm, SnippetInsertionContext.PlainSql);
            _completionAttached = true;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged -= OnVmPropertyChanged;
        }
        _currentVm = DataContext as ViewDetailTabViewModel;
        // A different view is now in these editors: the sticky diagnostics document belongs to the
        // previous one, so drop it and seed the incoming VM's panel from the cached diagnostics.
        _diagnostics.ResetActiveDocument();
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            // Wire the same selection-aware format callbacks the SQL Editor uses,
            // so FormatSqlCommand formats the selection-or-all against this editor.
            _currentVm.SelectedTextProvider = GetActiveEditorSelection;
            _currentVm.ReplaceSelectedOrAllText = ReplaceActiveEditorSelectionOrAll;
            PushSource();
            PushBody();
            PushDdl();
            PopulateDataGrid();
        }
    }


    // The editor the toolbar Format / selection callbacks act on: the AS-SELECT body
    // editor in Easy mode, the full-source editor in Source mode.
    private TextEditor? ActiveEditor
        => (_currentVm?.EasyMode ?? false) ? _bodyEditor : _sqlEditor;

    // S5 — the Diagnostics panel is a PEER tab, so reading the list hides the editor: a jump has to switch
    // back to it, not just move the caret. Both view editors live directly on the SQL tab (visibility
    // follows the mode), so there is no sub-tab to select.
    private void RevealEditor(TextEditor editor)
    {
        if (_currentVm is not null) _currentVm.ActiveSubTabIndex = ViewDetailTabViewModel.SqlSubTabIndex;
    }

    // Selection in the active editor, or null when nothing is selected.
    private string? GetActiveEditorSelection()
    {
        var ed = ActiveEditor;
        var sel = ed?.SelectedText;
        return string.IsNullOrEmpty(sel) ? null : sel;
    }

    // Replace the selection with the formatted text (re-selecting it), or overwrite
    // the whole document when there's no selection. Editor TextChanged then syncs the
    // active text (SourceText / EditableBody) back to the VM.
    private void ReplaceActiveEditorSelectionOrAll(string text)
    {
        var ed = ActiveEditor;
        if (ed is null) return;
        if (ed.SelectionLength > 0)
        {
            var start = ed.SelectionStart;
            ed.Document.Replace(start, ed.SelectionLength, text);
            ed.Select(start, text.Length);
        }
        else
        {
            ed.Text = text;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewDetailTabViewModel.SourceText))
        {
            PushSource();
        }
        else if (e.PropertyName == nameof(ViewDetailTabViewModel.EditableBody))
        {
            PushBody();
        }
        else if (e.PropertyName == nameof(ViewDetailTabViewModel.DdlText))
        {
            PushDdl();
        }
        else if (e.PropertyName == nameof(ViewDetailTabViewModel.EasyMode))
        {
            // Source⇄Easy flip: the sticky diagnostics document belongs to the mode we just left, so
            // drop it and fall back to the new mode's primary editor.
            _diagnostics.ResetActiveDocument();
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

    private void OnBodyEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressBodySync || _currentVm is null || _bodyEditor is null) return;
        _currentVm.EditableBody = _bodyEditor.Text;
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

    private void PushBody()
    {
        if (_bodyEditor is null || _currentVm is null) return;
        var text = _currentVm.EditableBody ?? string.Empty;
        if (_bodyEditor.Text == text) return;
        _suppressBodySync = true;
        try { _bodyEditor.Text = text; }
        finally { _suppressBodySync = false; }
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
                    // Tag = data column index → the filter-from-cell resolver reads it.
                    Tag = columnIndex,
                    CustomSortComparer = new RowIndexComparer(columnIndex),
                    CanUserSort = true,
                    CellTemplate = BuildTextCellTemplate(columnIndex),
                });
            }
        }

        _dataPreviewGrid.ItemsSource = result.Rows;
    }

    // ── Filter-from-cell (View Data) ──────────────────────────────────────────
    private GridCellFilterContext? _dataCellCtx;

    private void OnViewDataSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => _currentVm?.SetDataSelectedRow(_dataPreviewGrid?.SelectedIndex ?? -1);

    private void OnViewDataCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        if (_dataPreviewGrid is null || _currentVm is null) return;
        if (!e.PointerPressedEventArgs.GetCurrentPoint(_dataPreviewGrid).Properties.IsRightButtonPressed) return;
        if (e.Row?.DataContext is object?[] row) _dataPreviewGrid.SelectedItem = row;
        _dataCellCtx = GridCellFilter.Resolve(_dataPreviewGrid, e, _currentVm.DataFilterPanel.Columns);
        if (ViewDataFilterContainsItem is not null)
            ViewDataFilterContainsItem.IsEnabled = _dataCellCtx is { } ctx && GridCellFilter.SupportsContains(ctx);
    }

    private void OnViewDataFilterByValueClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null || _dataCellCtx is not { } ctx) return;
        var (col, op, val) = GridCellFilter.FilterByValue(ctx);
        _ = _currentVm.DataFilterPanel.ApplyFromCellAsync(col, op, val);
    }

    // Export the view data through the shared Export Framework (default = all rows).
    private async void OnViewDataExportClick(object? sender, RoutedEventArgs e)
        => await ExportDialog.LaunchAsync(this, _currentVm?.BuildDataExportSource(), ExportScope.AllRows);

    private void OnViewDataExcludeValueClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null || _dataCellCtx is not { } ctx) return;
        var (col, op, val) = GridCellFilter.ExcludeValue(ctx);
        _ = _currentVm.DataFilterPanel.ApplyFromCellAsync(col, op, val);
    }

    private void OnViewDataFilterContainsClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null || _dataCellCtx is not { } ctx) return;
        if (GridCellFilter.Contains(ctx) is not { } triple) return;
        _ = _currentVm.DataFilterPanel.ApplyFromCellAsync(triple.ColumnIndex, triple.Op, triple.Value);
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
        ApplyToEditor(_bodyEditor, syntax, selection);
        ApplyToEditor(_ddlEditor, syntax, selection);
    }

    private static void ApplyToEditor(TextEditor? editor, IHighlightingDefinition? syntax, IBrush? selection)
    {
        if (editor is null) return;
        editor.SyntaxHighlighting = syntax;
        if (selection is not null) editor.TextArea.SelectionBrush = selection;
    }
}
