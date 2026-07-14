using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
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
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Export;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Templates;

namespace EmberTern.App.Views;

public partial class FunctionDetailTabView : UserControl
{
    private TextEditor? _sqlEditor;
    private TextEditor? _bodyEditor;
    private TextEditor? _ddlEditor;
    private TextEditor? _cursorEditor;
    private TextEditor? _subprogramEditor;
    private DataGrid? _execResultGrid;
    private DataGrid? _argumentsGrid;
    private DataGrid? _resultTypeGrid;
    private DataGrid? _variablesGrid;
    private PerformancePanelView? _performancePanel;
    private FunctionDetailTabViewModel? _currentVm;
    private readonly List<string> _execColumnNames = new();
    private bool _suppressSourceSync;
    private bool _suppressBodySync;
    private bool _suppressCursorSync;
    private bool _suppressSubprogramSync;
    private TextEditor? _focusedEditor;
    private bool _completionAttached;

    public FunctionDetailTabView()
    {
        InitializeComponent();
        _sqlEditor = this.FindControl<TextEditor>("FuncSqlEditor");
        _bodyEditor = this.FindControl<TextEditor>("FuncBodyEditor");
        _ddlEditor = this.FindControl<TextEditor>("FuncDdlEditor");
        _cursorEditor = this.FindControl<TextEditor>("FuncCursorEditor");
        _subprogramEditor = this.FindControl<TextEditor>("FuncSubprogramEditor");
        _execResultGrid = this.FindControl<DataGrid>("FuncExecResultGrid");
        if (_execResultGrid is not null) _execResultGrid.CellPointerPressed += OnFuncExecCellPointerPressed;
        _argumentsGrid = this.FindControl<DataGrid>("FuncArgumentsGrid");
        _resultTypeGrid = this.FindControl<DataGrid>("FuncResultTypeGrid");
        _variablesGrid = this.FindControl<DataGrid>("FuncVariablesGrid");
        _performancePanel = this.FindControl<PerformancePanelView>("FuncPerformancePanel");
        if (_argumentsGrid is not null) FieldGridColumns.Build(_argumentsGrid, includeDefault: true);
        // The return value is a single, unnamed row — omit the Name + Default columns.
        if (_resultTypeGrid is not null) FieldGridColumns.Build(_resultTypeGrid, includeDefault: false, includeName: false);
        if (_variablesGrid is not null) FieldGridColumns.Build(_variablesGrid, includeDefault: true);

        WireEditor(_sqlEditor, OnSqlEditorTextChanged);
        WireEditor(_bodyEditor, OnBodyEditorTextChanged);
        WireEditor(_cursorEditor, OnCursorEditorTextChanged);
        WireEditor(_subprogramEditor, OnSubprogramEditorTextChanged);

        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
    }

    private void WireEditor(TextEditor? editor, EventHandler handler)
    {
        if (editor is null) return;
        editor.TextChanged += handler;
        editor.KeyDown += OnEditorKeyDown;
        editor.GotFocus += (_, _) => _focusedEditor = editor;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // A restored/re-shown tab may re-attach with the Performance sub-tab already active.
        NotifyPerformanceVisibility();
        if (_completionAttached) return;
        if (this.FindAncestorOfType<Window>()?.DataContext is MainWindowViewModel mainVm)
        {
            // Easy-mode fragments (body / cursor / subprogram) don't declare the function's
            // arguments or variables — those live in the grids — so seed them into the model or
            // Ctrl+Space offers no args/locals. Source mode needs nothing (the text has it all).
            Func<IReadOnlyList<Symbol>> ambient = () =>
                _currentVm?.BuildAmbientSymbols() ?? Array.Empty<Symbol>();

            if (_sqlEditor is not null) SqlEditorBehavior.Attach(_sqlEditor, mainVm);
            if (_bodyEditor is not null) SqlEditorBehavior.Attach(_bodyEditor, mainVm, ambientSymbols: ambient);
            if (_cursorEditor is not null) SqlEditorBehavior.Attach(_cursorEditor, mainVm, ambientSymbols: ambient);
            if (_subprogramEditor is not null) SqlEditorBehavior.Attach(_subprogramEditor, mainVm, ambientSymbols: ambient);

            // Metadata-object drop → snippet flyout, into every editable PSQL editor.
            if (_sqlEditor is not null) SqlSnippetDropTarget.Attach(_sqlEditor, mainVm, SnippetInsertionContext.PsqlBody);
            if (_bodyEditor is not null) SqlSnippetDropTarget.Attach(_bodyEditor, mainVm, SnippetInsertionContext.PsqlBody);
            if (_cursorEditor is not null) SqlSnippetDropTarget.Attach(_cursorEditor, mainVm, SnippetInsertionContext.PsqlBody);
            if (_subprogramEditor is not null) SqlSnippetDropTarget.Attach(_subprogramEditor, mainVm, SnippetInsertionContext.PsqlBody);
            _completionAttached = true;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged -= OnVmPropertyChanged;
            _currentVm.CommentRequested -= OnCommentRequested;
            _currentVm.UncommentRequested -= OnUncommentRequested;
            // The outgoing function's Performance surface is no longer on screen.
            _currentVm.Performance?.SetVisible(false);
        }
        _currentVm = DataContext as FunctionDetailTabViewModel;
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            _currentVm.CommentRequested += OnCommentRequested;
            _currentVm.UncommentRequested += OnUncommentRequested;
            // Bind the hosted Performance panel to THIS function's own context (the one reused
            // view instance follows ActiveFunctionDetail), then arm visibility for lazy build.
            if (_performancePanel is not null) _performancePanel.DataContext = _currentVm.Performance;
            NotifyPerformanceVisibility();
            _currentVm.SelectedTextProvider = GetActiveEditorSelection;
            _currentVm.ReplaceSelectedOrAllText = ReplaceActiveEditorSelectionOrAll;
            _currentVm.ExecuteParamsRequested = CollectExecuteParamsAsync;
            _currentVm.SubprogramKindRequested = AskSubprogramKindAsync;
            PushSource();
            PushBody();
            PushDdl();
            PushCursor();
            PushSubprogram();
            PopulateExecResultGrid();
        }
    }

    private TextEditor? ActiveEditor
    {
        get
        {
            if (_focusedEditor is not null && _focusedEditor.IsEffectivelyVisible) return _focusedEditor;
            return (_currentVm?.EasyMode ?? false) ? _bodyEditor : _sqlEditor;
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || e.KeyModifiers != KeyModifiers.Alt || _currentVm is null) return;
        if (sender is TextEditor ed && (ReferenceEquals(ed, _cursorEditor) || ReferenceEquals(ed, _subprogramEditor)))
        {
            FormatEditorInPlace(ed);
            e.Handled = true;
            return;
        }
        if (_currentVm.FormatSqlCommand.CanExecute(null))
        {
            _currentVm.FormatSqlCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static void FormatEditorInPlace(TextEditor ed)
    {
        if (ed.SelectionLength > 0)
        {
            var start = ed.SelectionStart;
            var formatted = SqlFormatter.Format(ed.SelectedText);
            ed.Document.Replace(start, ed.SelectionLength, formatted);
            ed.Select(start, formatted.Length);
        }
        else
        {
            var formatted = SqlFormatter.Format(ed.Text);
            if (!string.Equals(formatted, ed.Text, StringComparison.Ordinal)) ed.Text = formatted;
        }
    }

    private string? GetActiveEditorSelection()
    {
        var ed = ActiveEditor;
        var sel = ed?.SelectedText;
        return string.IsNullOrEmpty(sel) ? null : sel;
    }

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
        switch (e.PropertyName)
        {
            case nameof(FunctionDetailTabViewModel.SourceText): PushSource(); break;
            case nameof(FunctionDetailTabViewModel.ExecutableBody): PushBody(); break;
            case nameof(FunctionDetailTabViewModel.DdlText): PushDdl(); break;
            case nameof(FunctionDetailTabViewModel.ExecResultVersionTag): PopulateExecResultGrid(); break;
            case nameof(FunctionDetailTabViewModel.SelectedCursor): PushCursor(); break;
            case nameof(FunctionDetailTabViewModel.SelectedSubprogram): PushSubprogram(); break;
            case nameof(FunctionDetailTabViewModel.ActiveSubTabIndex): NotifyPerformanceVisibility(); break;
        }
    }

    // Tell THIS function's own Performance context whether its sub-tab is currently shown, so a
    // stale analysis (marked after this function's last Execute) is built lazily on show.
    private void NotifyPerformanceVisibility()
        => _currentVm?.Performance?.SetVisible(
            _currentVm.ActiveSubTabIndex == FunctionDetailTabViewModel.PerformanceSubTabIndex);

    private void OnSqlEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressSourceSync || _currentVm is null || _sqlEditor is null) return;
        _currentVm.SourceText = _sqlEditor.Text;
    }

    private void OnBodyEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressBodySync || _currentVm is null || _bodyEditor is null) return;
        _currentVm.ExecutableBody = _bodyEditor.Text;
    }

    private void OnCursorEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressCursorSync || _currentVm?.SelectedCursor is null || _cursorEditor is null) return;
        _currentVm.SelectedCursor.Declaration = _cursorEditor.Text;
    }

    private void OnSubprogramEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressSubprogramSync || _currentVm?.SelectedSubprogram is null || _subprogramEditor is null) return;
        _currentVm.SelectedSubprogram.Declaration = _subprogramEditor.Text;
    }

    private void PushSource() => PushInto(_sqlEditor, _currentVm?.SourceText, ref _suppressSourceSync);
    private void PushBody() => PushInto(_bodyEditor, _currentVm?.ExecutableBody, ref _suppressBodySync);
    private void PushCursor() => PushInto(_cursorEditor, _currentVm?.SelectedCursor?.Declaration, ref _suppressCursorSync);
    private void PushSubprogram() => PushInto(_subprogramEditor, _currentVm?.SelectedSubprogram?.Declaration, ref _suppressSubprogramSync);

    private static void PushInto(TextEditor? editor, string? value, ref bool suppress)
    {
        if (editor is null) return;
        var text = value ?? string.Empty;
        if (editor.Text == text) return;
        suppress = true;
        try { editor.Text = text; }
        finally { suppress = false; }
    }

    private void PushDdl()
    {
        if (_ddlEditor is null || _currentVm is null) return;
        var text = _currentVm.DdlText ?? string.Empty;
        if (_ddlEditor.Text != text) _ddlEditor.Text = text;
    }

    // ─── Comment Body / Uncomment Body ─────────────────────────────────────

    private void OnCommentRequested() => ApplyBodyTransform(ProcedureBodyScanner.CommentBody);
    private void OnUncommentRequested() => ApplyBodyTransform(ProcedureBodyScanner.UncommentBody);

    private void ApplyBodyTransform(Func<string?, string?> transform)
    {
        var ed = ActiveEditor;
        if (ed is null) return;
        var result = transform(ed.Text);
        if (result is null || string.Equals(result, ed.Text, StringComparison.Ordinal)) return;
        var caret = ed.CaretOffset;
        ed.Text = result; // TextChanged syncs back to the VM
        ed.CaretOffset = Math.Min(caret, result.Length);
    }

    // ─── Subprogram kind prompt (Procedure / Function) ─────────────────────

    private async Task<string?> AskSubprogramKindAsync()
    {
        var window = this.FindAncestorOfType<Window>();
        if (window is null) return "PROCEDURE";
        return await SubprogramKindDialog.ShowAsync(window).ConfigureAwait(true);
    }

    // ─── Execute Function dialog ───────────────────────────────────────────

    private async Task<IReadOnlyList<object?>?> CollectExecuteParamsAsync(IReadOnlyList<ProcedureParamRowViewModel> inputs)
    {
        var window = this.FindAncestorOfType<Window>();
        if (window is null) return null;
        var mainVm = window.DataContext as MainWindowViewModel;
        var vm = new ExecuteProcedureDialogViewModel(
            inputs, _currentVm?.FunctionName,
            mainVm?.Service.ActiveProfile?.Id, "Function", mainVm?.ParameterHistory);
        return await ExecuteProcedureDialog.ShowAsync(window, vm).ConfigureAwait(true);
    }

    // ─── Execute Result grid (read-only, dynamic columns over object?[] rows) ──

    // Feeds the "Record N of M" indicator (SelectedIndex is within the page).
    private void OnFuncExecResultSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => _currentVm?.SetExecSelectedRow(_execResultGrid?.SelectedIndex ?? -1);

    // Select the row under a right-click on an Easy-Mode collection grid (arguments /
    // variables) so the context-menu Remove / Move act on the clicked row — Avalonia's
    // DataGrid doesn't auto-select on right-click (gotcha #16). Handled stays false so the
    // ContextMenu still opens; the SelectedItem two-way binding carries it to the VM.
    private void OnEasyGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (!e.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;
        if (e.Source is not Visual v) return;
        var row = v.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.DataContext is { } item) grid.SelectedItem = item;
    }

    // ── Filter-from-cell (Execute Result) ────────────────────────────────────
    private GridCellFilterContext? _execCellCtx;

    private void OnFuncExecCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        if (_execResultGrid is null || _currentVm is null) return;
        if (!e.PointerPressedEventArgs.GetCurrentPoint(_execResultGrid).Properties.IsRightButtonPressed) return;
        if (e.Row?.DataContext is object?[] row) _execResultGrid.SelectedItem = row;
        _execCellCtx = GridCellFilter.Resolve(_execResultGrid, e, _currentVm.ExecFilterPanel.Columns);
        if (FuncFilterContainsItem is not null)
            FuncFilterContainsItem.IsEnabled = _execCellCtx is { } ctx && GridCellFilter.SupportsContains(ctx);
    }

    private void OnFuncFilterByValueClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null || _execCellCtx is not { } ctx) return;
        var (col, op, val) = GridCellFilter.FilterByValue(ctx);
        _ = _currentVm.ExecFilterPanel.ApplyFromCellAsync(col, op, val);
    }

    // Export the function-execution result through the shared Export Framework (default = all rows).
    private async void OnFuncExportClick(object? sender, RoutedEventArgs e)
        => await ExportDialog.LaunchAsync(this, _currentVm?.BuildExecResultExportSource(), ExportScope.AllRows);

    private void OnFuncExcludeValueClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null || _execCellCtx is not { } ctx) return;
        var (col, op, val) = GridCellFilter.ExcludeValue(ctx);
        _ = _currentVm.ExecFilterPanel.ApplyFromCellAsync(col, op, val);
    }

    private void OnFuncFilterContainsClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null || _execCellCtx is not { } ctx) return;
        if (GridCellFilter.Contains(ctx) is not { } triple) return;
        _ = _currentVm.ExecFilterPanel.ApplyFromCellAsync(triple.ColumnIndex, triple.Op, triple.Value);
    }

    private void PopulateExecResultGrid()
    {
        if (_execResultGrid is null) return;
        var result = _currentVm?.ExecResult;

        if (result is null || !result.HasResultSet)
        {
            _execResultGrid.Columns.Clear();
            _execResultGrid.ItemsSource = null;
            _execColumnNames.Clear();
            return;
        }

        bool sameStructure = _execColumnNames.Count == result.Columns.Count;
        if (sameStructure)
        {
            for (int i = 0; i < result.Columns.Count; i++)
            {
                if (!string.Equals(result.Columns[i].Name, _execColumnNames[i], StringComparison.Ordinal))
                {
                    sameStructure = false;
                    break;
                }
            }
        }

        if (!sameStructure)
        {
            _execResultGrid.Columns.Clear();
            _execColumnNames.Clear();
            for (int i = 0; i < result.Columns.Count; i++)
            {
                int columnIndex = i;
                var columnName = result.Columns[i].Name;
                _execColumnNames.Add(columnName);
                _execResultGrid.Columns.Add(new DataGridTemplateColumn
                {
                    Header = columnName,
                    // Tag = data column index → the filter-from-cell resolver reads it.
                    Tag = columnIndex,
                    CanUserSort = false,
                    CellTemplate = BuildTextCellTemplate(columnIndex),
                });
            }
        }

        _execResultGrid.ItemsSource = _currentVm?.PagedExecRows;
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
        var name = theme == ThemeVariant.Light ? App.FirebirdSyntaxLightName : App.FirebirdSyntaxName;
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
        ApplyToEditor(_cursorEditor, syntax, selection);
        ApplyToEditor(_subprogramEditor, syntax, selection);
    }

    private static void ApplyToEditor(TextEditor? editor, IHighlightingDefinition? syntax, IBrush? selection)
    {
        if (editor is null) return;
        editor.SyntaxHighlighting = syntax;
        if (selection is not null) editor.TextArea.SelectionBrush = selection;
    }
}
