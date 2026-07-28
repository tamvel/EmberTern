using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.Completion;
using EmberTern.App.Sql;
using EmberTern.App.ViewModels;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Templates;

namespace EmberTern.App.Views;

public partial class PackageDetailTabView : UserControl
{
    private TextEditor? _headerEditor;
    private TextEditor? _bodyEditor;
    private TextEditor? _ddlEditor;
    private PackageDetailTabViewModel? _currentVm;
    // Guards the editor↔VM feedback loop while pushing source INTO an editor.
    private bool _suppressHeaderSync;
    private bool _suppressBodySync;
    private bool _completionAttached;
    // Feeds this package's own Diagnostics sub-tab from the ACTIVE SQL document (S4). A package has no
    // Easy/Source mode: until an editor takes focus the fallback is ActiveEditor's tab-based rule.
    private readonly DiagnosticsPanelHost _diagnostics;

    public PackageDetailTabView()
    {
        InitializeComponent();
        _diagnostics = new DiagnosticsPanelHost(
            () => _currentVm?.DiagnosticsPanel,
            () => ActiveEditor,
            RevealEditor);
        _headerEditor = this.FindControl<TextEditor>("PackageHeaderEditor");
        _bodyEditor = this.FindControl<TextEditor>("PackageBodyEditor");
        _ddlEditor = this.FindControl<TextEditor>("PackageDdlEditor");
        if (_ddlEditor is not null) SqlEditorBehavior.AttachReadOnlyHighlighting(_ddlEditor);
        // S5: the panel's activation gestures navigate the active SQL document.
        var diagnosticsPanel = this.FindControl<DiagnosticsPanelView>("PackageDiagnosticsPanel");
        if (diagnosticsPanel is not null) diagnosticsPanel.Navigator = _diagnostics;
        // Format is not wired here any more: it is CommandId.FormatSql (Ctrl+K), declared once in
        // Commands.CommandCatalog for this tab kind and routed to this VM's own FormatSqlCommand.
        if (_headerEditor is not null) _headerEditor.TextChanged += OnHeaderEditorTextChanged;
        if (_bodyEditor is not null) _bodyEditor.TextChanged += OnBodyEditorTextChanged;
        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Autocomplete + Ctrl/double-click navigation on both editors, reusing the SQL
    // Editor's services via SqlEditorBehavior — same wiring as ViewDetailTabView.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_completionAttached) return;
        if (this.FindAncestorOfType<Window>()?.DataContext is MainWindowViewModel mainVm)
        {
            // Each editor is tracked by the Diagnostics host too, so this package's Diagnostics sub-tab
            // reflects whichever of them is the active SQL document (S4).
            if (_headerEditor is not null)
            {
                _diagnostics.Track(_headerEditor, SqlEditorBehavior.Attach(_headerEditor, mainVm));
            }
            if (_bodyEditor is not null)
            {
                _diagnostics.Track(_bodyEditor, SqlEditorBehavior.Attach(_bodyEditor, mainVm));
            }

            // Metadata-object drop → snippet flyout, into the editable package editors.
            if (_headerEditor is not null) SqlSnippetDropTarget.Attach(_headerEditor, mainVm, SnippetInsertionContext.PsqlBody);
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
            _currentVm.NavigateToMemberRequested -= OnNavigateToMember;
        }
        _currentVm = DataContext as PackageDetailTabViewModel;
        // A different package is now in these editors: the sticky diagnostics document belongs to the
        // previous one, so drop it and seed the incoming VM's panel from the cached diagnostics.
        _diagnostics.ResetActiveDocument();
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            _currentVm.CommentRequested += OnCommentRequested;
            _currentVm.UncommentRequested += OnUncommentRequested;
            _currentVm.NavigateToMemberRequested += OnNavigateToMember;
            _currentVm.SelectedTextProvider = GetActiveEditorSelection;
            _currentVm.ReplaceSelectedOrAllText = ReplaceActiveEditorSelectionOrAll;
            PushHeader();
            PushBody();
            PushDdl();
        }
    }

    // The active editor for Format / Comment / selection: the body editor on the Body
    // tab, the header editor otherwise.
    private TextEditor? ActiveEditor
        => (_currentVm?.ActiveSubTabIndex == PackageDetailTabViewModel.BodySubTabIndex)
            ? _bodyEditor
            : _headerEditor;

    // S5 — the Diagnostics panel is a PEER tab, so reading the list hides both editors: a jump has to
    // switch back to the one that owns the finding, not just move its caret. Here the editor IS the tab
    // (header / body), so selecting the tab is the whole reveal — and it re-aligns the tab-based
    // ActiveEditor fallback with the document just navigated to.
    private void RevealEditor(TextEditor editor)
    {
        if (_currentVm is null) return;
        _currentVm.ActiveSubTabIndex = ReferenceEquals(editor, _bodyEditor)
            ? PackageDetailTabViewModel.BodySubTabIndex
            : PackageDetailTabViewModel.PackageSubTabIndex;
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
            case nameof(PackageDetailTabViewModel.HeaderSource): PushHeader(); break;
            case nameof(PackageDetailTabViewModel.BodySource): PushBody(); break;
            case nameof(PackageDetailTabViewModel.DdlText): PushDdl(); break;
        }
    }

    private void OnHeaderEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressHeaderSync || _currentVm is null || _headerEditor is null) return;
        _currentVm.HeaderSource = _headerEditor.Text;
    }

    private void OnBodyEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressBodySync || _currentVm is null || _bodyEditor is null) return;
        _currentVm.BodySource = _bodyEditor.Text;
    }

    private void PushHeader()
    {
        if (_headerEditor is null || _currentVm is null) return;
        var text = _currentVm.HeaderSource ?? string.Empty;
        if (_headerEditor.Text == text) return;
        _suppressHeaderSync = true;
        try { _headerEditor.Text = text; }
        finally { _suppressHeaderSync = false; }
    }

    private void PushBody()
    {
        if (_bodyEditor is null || _currentVm is null) return;
        var text = _currentVm.BodySource ?? string.Empty;
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

    // Comment / Uncomment wrap the active editor's outermost BEGIN…END body in a
    // /* */ block — reuses the procedure body-comment scanner (existing mechanism).
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

    private void OnMemberNodeDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PackageMemberItemNode node } && _currentVm is not null)
        {
            _currentVm.NavigateToMember(node.Member);
            e.Handled = true;
        }
    }

    // "Debug procedure…" on a package member (D11 seam C) — the MenuItem inherits the member node as its
    // DataContext from the ContextMenu's placement target. Mirrors the double-click handler; the VM raises the
    // intent and the owner launches via the one debug-launch path.
    private void OnMemberDebugClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PackageMemberItemNode node } && _currentVm is not null)
        {
            _currentVm.RequestDebugMember(node.Member);
            e.Handled = true;
        }
    }

    // The VM already switched ActiveSubTabIndex to the right tab; post the select so the
    // target editor is realized after the tab switch before we move the caret.
    private void OnNavigateToMember(PackageMemberLocation loc)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var ed = loc.InBody ? _bodyEditor : _headerEditor;
            if (ed is null || loc.Offset < 0 || loc.Offset > ed.Document.TextLength) return;
            var length = Math.Min(loc.Length, ed.Document.TextLength - loc.Offset);
            ed.Select(loc.Offset, length);
            ed.CaretOffset = loc.Offset;
            var line = ed.Document.GetLineByOffset(loc.Offset).LineNumber;
            ed.ScrollToLine(line);
            ed.Focus();
        }, DispatcherPriority.Background);
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
        ApplyToEditor(_headerEditor, syntax, selection);
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
