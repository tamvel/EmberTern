using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
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
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Sql.Templates;

namespace EmberTern.App.Views;

public partial class ProcedureDetailTabView : UserControl
{
    private TextEditor? _sqlEditor;
    private TextEditor? _bodyEditor;
    private TextEditor? _ddlEditor;
    private TextEditor? _cursorEditor;
    private TextEditor? _subprogramEditor;
    private DataGrid? _resultGrid;
    private DataGrid? _inputGrid;
    private DataGrid? _outputGrid;
    private DataGrid? _variablesGrid;
    private PerformancePanelView? _performancePanel;
    private ProcedureDetailTabViewModel? _currentVm;
    private readonly List<string> _resultColumnNames = new();
    private bool _suppressSourceSync;
    private bool _suppressBodySync;
    private bool _suppressCursorSync;
    private bool _suppressSubprogramSync;
    // The editable editor that last had focus — drives which editor the toolbar
    // Format / Comment commands and Alt+F act on (body, source, cursor, subprogram).
    private TextEditor? _focusedEditor;
    private bool _completionAttached;
    // Rebuilds the ambient-seeded editors' models when the Easy-mode grids change (S3 follow-up) so
    // diagnostics/completion/highlighting refresh live without a body-text edit.
    private readonly AmbientModelRefresh _ambientRefresh = new();

    public ProcedureDetailTabView()
    {
        InitializeComponent();
        _sqlEditor = this.FindControl<TextEditor>("ProcSqlEditor");
        _bodyEditor = this.FindControl<TextEditor>("ProcBodyEditor");
        _ddlEditor = this.FindControl<TextEditor>("ProcDdlEditor");
        _cursorEditor = this.FindControl<TextEditor>("CursorSourceEditor");
        _subprogramEditor = this.FindControl<TextEditor>("SubprogramSourceEditor");
        _resultGrid = this.FindControl<DataGrid>("ProcResultGrid");
        if (_resultGrid is not null) _resultGrid.CellPointerPressed += OnProcResultCellPointerPressed;
        _inputGrid = this.FindControl<DataGrid>("InputParamsGrid");
        _outputGrid = this.FindControl<DataGrid>("OutputParamsGrid");
        _variablesGrid = this.FindControl<DataGrid>("VariablesGrid");
        _performancePanel = this.FindControl<PerformancePanelView>("ProcPerformancePanel");
        if (_inputGrid is not null) FieldGridColumns.Build(_inputGrid, includeDefault: true);
        if (_outputGrid is not null) FieldGridColumns.Build(_outputGrid, includeDefault: false);
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

    // Attach autocomplete + double-click/Ctrl+Click navigation to every editable
    // editor once the owning MainWindowViewModel is reachable. Reuses the SQL Editor's
    // services via SqlEditorBehavior — no second implementation.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // A restored/re-shown tab may re-attach with the Performance sub-tab already active.
        NotifyPerformanceVisibility();
        if (_completionAttached) return;
        if (this.FindAncestorOfType<Window>()?.DataContext is MainWindowViewModel mainVm)
        {
            // Source mode: the text is the whole CREATE PROCEDURE, so the model already sees the
            // params + DECLAREs — no ambient symbols. The Easy-mode BODY / cursor / subprogram
            // editors hold only a fragment, with the params + variables in the grids, so they must
            // be seeded or Ctrl+Space offers no parameters/locals.
            Func<IReadOnlyList<Symbol>> ambient = () =>
                _currentVm?.BuildAmbientSymbols() ?? Array.Empty<Symbol>();

            if (_sqlEditor is not null) SqlEditorBehavior.Attach(_sqlEditor, mainVm);
            if (_bodyEditor is not null) _ambientRefresh.Track(SqlEditorBehavior.Attach(_bodyEditor, mainVm, ambientSymbols: ambient));
            if (_cursorEditor is not null) _ambientRefresh.Track(SqlEditorBehavior.Attach(_cursorEditor, mainVm, ambientSymbols: ambient));
            if (_subprogramEditor is not null) _ambientRefresh.Track(SqlEditorBehavior.Attach(_subprogramEditor, mainVm, ambientSymbols: ambient));
            // Grid edits (param/variable add/remove/rename) → rebuild the ambient-seeded models.
            _ambientRefresh.Bind(_currentVm);

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
            // The outgoing procedure's Performance surface is no longer on screen.
            _currentVm.Performance?.SetVisible(false);
        }
        _currentVm = DataContext as ProcedureDetailTabViewModel;
        // Follow the (possibly reused) view onto this VM for ambient-grid → model rebuilds.
        _ambientRefresh.Bind(_currentVm);
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            _currentVm.CommentRequested += OnCommentRequested;
            _currentVm.UncommentRequested += OnUncommentRequested;
            // Bind the hosted Performance panel to THIS procedure's own context (the one reused
            // view instance follows ActiveProcedureDetail), then arm visibility for lazy build.
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
            PopulateResultGrid();
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

    // Alt+F formats the focused editor via the shared (PSQL-aware) SqlFormatter.
    // The body/source editors go through the VM command (so the formatted text syncs
    // back); the cursor/subprogram editors format in place (TextChanged syncs the row).
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
            case nameof(ProcedureDetailTabViewModel.SourceText): PushSource(); break;
            case nameof(ProcedureDetailTabViewModel.ExecutableBody): PushBody(); break;
            case nameof(ProcedureDetailTabViewModel.DdlText): PushDdl(); break;
            case nameof(ProcedureDetailTabViewModel.ExecResultVersionTag): PopulateResultGrid(); break;
            case nameof(ProcedureDetailTabViewModel.SelectedCursor): PushCursor(); break;
            case nameof(ProcedureDetailTabViewModel.SelectedSubprogram): PushSubprogram(); break;
            case nameof(ProcedureDetailTabViewModel.ActiveSubTabIndex): NotifyPerformanceVisibility(); break;
        }
    }

    // Tell THIS procedure's own Performance context whether its sub-tab is currently shown, so a
    // stale analysis (marked after this procedure's last Execute) is built lazily on show.
    private void NotifyPerformanceVisibility()
        => _currentVm?.Performance?.SetVisible(
            _currentVm.ActiveSubTabIndex == ProcedureDetailTabViewModel.PerformanceSubTabIndex);

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

    // ─── Comment Body / Uncomment Body (disable/enable the whole body) ─────

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

    // ─── Execute Procedure dialog ──────────────────────────────────────────

    private async Task<IReadOnlyList<object?>?> CollectExecuteParamsAsync(IReadOnlyList<ProcedureParamRowViewModel> inputs)
    {
        var window = this.FindAncestorOfType<Window>();
        if (window is null) return null;
        var mainVm = window.DataContext as MainWindowViewModel;
        var vm = new ExecuteProcedureDialogViewModel(
            inputs, _currentVm?.ProcedureName,
            mainVm?.Service.ActiveProfile?.Id, "Procedure", mainVm?.ParameterHistory);
        return await ExecuteProcedureDialog.ShowAsync(window, vm).ConfigureAwait(true);
    }

    // ─── Result grid (read-only, dynamic columns over object?[] rows) ──────

    private void PopulateResultGrid()
    {
        if (_resultGrid is null) return;
        var result = _currentVm?.ExecResult;

        if (result is null || !result.HasResultSet)
        {
            _resultGrid.Columns.Clear();
            _resultGrid.ItemsSource = null;
            _resultColumnNames.Clear();
            return;
        }

        bool sameStructure = _resultColumnNames.Count == result.Columns.Count;
        if (sameStructure)
        {
            for (int i = 0; i < result.Columns.Count; i++)
            {
                if (!string.Equals(result.Columns[i].Name, _resultColumnNames[i], StringComparison.Ordinal))
                {
                    sameStructure = false;
                    break;
                }
            }
        }

        if (!sameStructure)
        {
            _resultGrid.Columns.Clear();
            _resultColumnNames.Clear();
            for (int i = 0; i < result.Columns.Count; i++)
            {
                int columnIndex = i;
                var columnName = result.Columns[i].Name;
                _resultColumnNames.Add(columnName);
                _resultGrid.Columns.Add(new DataGridTemplateColumn
                {
                    Header = columnName,
                    // Tag = data column index → the filter-from-cell resolver reads it.
                    Tag = columnIndex,
                    CanUserSort = false,
                    CellTemplate = BuildTextCellTemplate(columnIndex),
                });
            }
        }

        _resultGrid.ItemsSource = _currentVm?.PagedExecRows;
    }

    // Feeds the "Record N of M" indicator (SelectedIndex is within the page).
    private void OnProcResultSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => _currentVm?.SetExecSelectedRow(_resultGrid?.SelectedIndex ?? -1);

    // Select the row under a right-click on an Easy-Mode collection grid (params /
    // variables) so the context-menu Remove / Move act on the clicked row — Avalonia's
    // DataGrid doesn't auto-select on right-click (gotcha #16). Leaves Handled=false so
    // the ContextMenu still opens; the grid's SelectedItem two-way binding carries the
    // selection to the VM, which the shared collection commands read.
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

    private void OnProcResultCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        if (_resultGrid is null || _currentVm is null) return;
        if (!e.PointerPressedEventArgs.GetCurrentPoint(_resultGrid).Properties.IsRightButtonPressed) return;
        if (e.Row?.DataContext is object?[] row) _resultGrid.SelectedItem = row;
        _execCellCtx = GridCellFilter.Resolve(_resultGrid, e, _currentVm.ExecFilterPanel.Columns);
        if (ProcFilterContainsItem is not null)
            ProcFilterContainsItem.IsEnabled = _execCellCtx is { } ctx && GridCellFilter.SupportsContains(ctx);
    }

    private void OnProcFilterByValueClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null || _execCellCtx is not { } ctx) return;
        var (col, op, val) = GridCellFilter.FilterByValue(ctx);
        _ = _currentVm.ExecFilterPanel.ApplyFromCellAsync(col, op, val);
    }

    // Export the procedure-execution result through the shared Export Framework (default = all rows).
    private async void OnProcExportClick(object? sender, RoutedEventArgs e)
        => await ExportDialog.LaunchAsync(this, _currentVm?.BuildExecResultExportSource(), ExportScope.AllRows);

    private void OnProcExcludeValueClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null || _execCellCtx is not { } ctx) return;
        var (col, op, val) = GridCellFilter.ExcludeValue(ctx);
        _ = _currentVm.ExecFilterPanel.ApplyFromCellAsync(col, op, val);
    }

    private void OnProcFilterContainsClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null || _execCellCtx is not { } ctx) return;
        if (GridCellFilter.Contains(ctx) is not { } triple) return;
        _ = _currentVm.ExecFilterPanel.ApplyFromCellAsync(triple.ColumnIndex, triple.Op, triple.Value);
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
