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
using EmberTern.App.Completion;
using EmberTern.App.ViewModels;
using EmberTern.Core.Sql;

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
    // and Alt+F act on (body in Easy mode, source in Source mode).
    private TextEditor? _focusedEditor;
    private bool _completionAttached;

    public TriggerDetailTabView()
    {
        InitializeComponent();
        _sqlEditor = this.FindControl<TextEditor>("TriggerSqlEditor");
        _bodyEditor = this.FindControl<TextEditor>("TriggerBodyEditor");
        _ddlEditor = this.FindControl<TextEditor>("TriggerDdlEditor");
        _variablesGrid = this.FindControl<DataGrid>("VariablesGrid");
        if (_variablesGrid is not null) FieldGridColumns.Build(_variablesGrid, includeDefault: true);

        WireEditor(_sqlEditor, OnSqlEditorTextChanged);
        WireEditor(_bodyEditor, OnBodyEditorTextChanged);

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

    // Attach autocomplete + double-click/Ctrl+Click navigation to the editable editors
    // once the owning MainWindowViewModel is reachable. Reuses the SQL Editor's services
    // via SqlEditorBehavior — no second implementation.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_completionAttached) return;
        if (this.FindAncestorOfType<Window>()?.DataContext is MainWindowViewModel mainVm)
        {
            if (_sqlEditor is not null) SqlEditorBehavior.Attach(_sqlEditor, mainVm);
            if (_bodyEditor is not null) SqlEditorBehavior.Attach(_bodyEditor, mainVm);
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

    private TextEditor? ActiveEditor
    {
        get
        {
            if (_focusedEditor is not null && _focusedEditor.IsEffectivelyVisible) return _focusedEditor;
            return (_currentVm?.EasyMode ?? false) ? _bodyEditor : _sqlEditor;
        }
    }

    // Alt+F formats the focused editor via the shared (PSQL-aware) SqlFormatter, routed
    // through the VM command so the formatted text syncs back.
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || e.KeyModifiers != KeyModifiers.Alt || _currentVm is null) return;
        if (_currentVm.FormatSqlCommand.CanExecute(null))
        {
            _currentVm.FormatSqlCommand.Execute(null);
            e.Handled = true;
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
    }

    private static void ApplyToEditor(TextEditor? editor, IHighlightingDefinition? syntax, IBrush? selection)
    {
        if (editor is null) return;
        editor.SyntaxHighlighting = syntax;
        if (selection is not null) editor.TextArea.SelectionBrush = selection;
    }
}
