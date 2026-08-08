using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.Behaviors;
using EmberTern.App.Completion;
using EmberTern.App.Sql;
using EmberTern.App.ViewModels;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Templates;

namespace EmberTern.App.Views;

public partial class TriggerDetailTabView : UserControl
{
    private TextEditor? _sqlEditor;
    private TextEditor? _bodyEditor;
    private TextEditor? _ddlEditor;
    private DataGrid? _variablesGrid;
    private TriggerDetailTabViewModel? _currentVm;
    private bool _suppressSourceSync;
    private bool _suppressBodySync;
    // The editable editor that last had focus — drives which editor Format / Comment
    // act on (body in Easy mode, source in Source mode).
    private TextEditor? _focusedEditor;
    private bool _completionAttached;
    // Rebuilds the ambient-seeded body model when the Easy-mode Variables grid changes (S3 follow-up).
    private readonly AmbientModelRefresh _ambientRefresh = new();
    // Feeds this trigger's own Diagnostics sub-tab from the ACTIVE SQL document (S4).
    private readonly DiagnosticsPanelHost _diagnostics;

    public TriggerDetailTabView()
    {
        InitializeComponent();
        _diagnostics = new DiagnosticsPanelHost(
            () => _currentVm?.DiagnosticsPanel,
            () => ModePrimaryEditor,
            RevealEditor);
        _sqlEditor = this.FindControl<TextEditor>("TriggerSqlEditor");
        _bodyEditor = this.FindControl<TextEditor>("TriggerBodyEditor");
        _ddlEditor = this.FindControl<TextEditor>("TriggerDdlEditor");
        if (_ddlEditor is not null) SqlEditorBehavior.AttachReadOnlyHighlighting(_ddlEditor);
        // S5: the panel's activation gestures navigate the active SQL document.
        var diagnosticsPanel = this.FindControl<DiagnosticsPanelView>("TriggerDiagnosticsPanel");
        if (diagnosticsPanel is not null) diagnosticsPanel.Navigator = _diagnostics;
        _variablesGrid = this.FindControl<DataGrid>("VariablesGrid");
        if (_variablesGrid is not null)
        {
            FieldGridColumns.Build(_variablesGrid, includeDefault: true);
            EditableGridBehavior.Attach(_variablesGrid);
        }

        WireEditor(_sqlEditor, OnSqlEditorTextChanged);
        WireEditor(_bodyEditor, OnBodyEditorTextChanged);

        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
    }

    // Format is not wired here any more: it is CommandId.FormatSql (Ctrl+K), declared once in
    // Commands.CommandCatalog for this tab kind and routed to this VM's own FormatSqlCommand.
    private void WireEditor(TextEditor? editor, EventHandler handler)
    {
        if (editor is null) return;
        editor.TextChanged += handler;
        editor.GotFocus += (_, _) => _focusedEditor = editor;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Attach autocomplete + double-click/Ctrl+Click navigation to the editable editors
    // once the owning MainWindowViewModel is reachable. Reuses the SQL Editor's services
    // via SqlEditorBehavior — no second implementation.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_completionAttached) return;
        if (this.FindAncestorOfType<Window>()?.DataContext is MainWindowViewModel mainVm)
        {
            // NEW. / OLD. in the trigger body complete the trigger's table columns.
            Func<string?> triggerTable = () => _currentVm?.TableName;
            // Easy mode holds only the body; the trigger's DECLAREd variables live in the grid, so
            // seed them into the model or Ctrl+Space offers no locals. Source mode needs nothing.
            Func<IReadOnlyList<Symbol>> ambient = () =>
                _currentVm?.BuildAmbientSymbols() ?? Array.Empty<Symbol>();

            // Each editor is tracked by the Diagnostics host too, so this trigger's Diagnostics sub-tab
            // reflects whichever of them is the active SQL document (S4).
            if (_sqlEditor is not null)
            {
                _diagnostics.Track(_sqlEditor, SqlEditorBehavior.Attach(_sqlEditor, mainVm, triggerTable));
            }
            if (_bodyEditor is not null)
            {
                var c = SqlEditorBehavior.Attach(_bodyEditor, mainVm, triggerTable, ambientSymbols: ambient);
                _ambientRefresh.Track(c);
                _diagnostics.Track(_bodyEditor, c);
            }
            // Variables-grid edits (add/remove/rename) → rebuild the ambient-seeded body model.
            _ambientRefresh.Bind(_currentVm);

            // Metadata-object drop → snippet flyout, into the editable trigger editors.
            if (_sqlEditor is not null) SqlSnippetDropTarget.Attach(_sqlEditor, mainVm, SnippetInsertionContext.PsqlBody);
            if (_bodyEditor is not null) SqlSnippetDropTarget.Attach(_bodyEditor, mainVm, SnippetInsertionContext.PsqlBody);
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
        }
        _currentVm = DataContext as TriggerDetailTabViewModel;
        // Follow the (possibly reused) view onto this VM for ambient-grid → model rebuilds.
        _ambientRefresh.Bind(_currentVm);
        // A different trigger is now in these editors: the sticky diagnostics document belongs to the
        // previous one, so drop it and seed the incoming VM's panel from the cached diagnostics.
        _diagnostics.ResetActiveDocument();
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            _currentVm.CommentRequested += OnCommentRequested;
            _currentVm.UncommentRequested += OnUncommentRequested;
            _currentVm.SelectedTextProvider = GetActiveEditorSelection;
            _currentVm.ReplaceSelectedOrAllText = ReplaceActiveEditorSelectionOrAll;
            PushSource();
            PushBody();
            PushDdl();
        }
    }

    // The editor this mode's work happens in by default: the body-only editor in Easy mode, the full
    // CREATE TRIGGER text in Source mode. Also the Diagnostics panel's fallback document.
    private TextEditor? ModePrimaryEditor => (_currentVm?.EasyMode ?? false) ? _bodyEditor : _sqlEditor;

    // S5 — the Diagnostics panel is a PEER tab, so reading the list hides the editor: a jump has to switch
    // back to it, not just move the caret. Both trigger editors live directly on the Editor tab (visibility
    // follows the mode), so there is no sub-tab to select.
    private void RevealEditor(TextEditor editor)
    {
        if (_currentVm is not null) _currentVm.ActiveSubTabIndex = TriggerDetailTabViewModel.EditorSubTabIndex;
    }

    private TextEditor? ActiveEditor
    {
        get
        {
            if (_focusedEditor is not null && _focusedEditor.IsEffectivelyVisible) return _focusedEditor;
            return ModePrimaryEditor;
        }
    }

    private string? GetActiveEditorSelection()
    {
        var sel = ActiveEditor?.SelectedText;
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
            case nameof(TriggerDetailTabViewModel.SourceText): PushSource(); break;
            case nameof(TriggerDetailTabViewModel.ExecutableBody): PushBody(); break;
            case nameof(TriggerDetailTabViewModel.DdlText): PushDdl(); break;
            // Source⇄Easy flip: the sticky diagnostics document belongs to the mode we just left, so drop
            // it and fall back to the new mode's primary editor.
            case nameof(TriggerDetailTabViewModel.EasyMode): _diagnostics.ResetActiveDocument(); break;
        }
    }

    private void OnSqlEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressSourceSync || _currentVm is null || _sqlEditor is null) return;
        _currentVm.SourceText = _sqlEditor.Text;
    }

    // Select the row under a right-click on the Variables grid so the context-menu
    // Remove / Move act on the clicked row (Avalonia DataGrid doesn't auto-select on
    // right-click, gotcha #16). Handled stays false so the ContextMenu still opens.
    private void OnEasyGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (!e.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;
        if (e.Source is not Visual v) return;
        var row = v.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.DataContext is { } item) grid.SelectedItem = item;
    }

    private void OnBodyEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressBodySync || _currentVm is null || _bodyEditor is null) return;
        _currentVm.ExecutableBody = _bodyEditor.Text;
    }

    private void PushSource() => PushInto(_sqlEditor, _currentVm?.SourceText, ref _suppressSourceSync);
    private void PushBody() => PushInto(_bodyEditor, _currentVm?.ExecutableBody, ref _suppressBodySync);

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
    }

    private static void ApplyToEditor(TextEditor? editor, IHighlightingDefinition? syntax, IBrush? selection)
    {
        if (editor is null) return;
        editor.SyntaxHighlighting = syntax;
        if (selection is not null) editor.TextArea.SelectionBrush = selection;
    }
}
