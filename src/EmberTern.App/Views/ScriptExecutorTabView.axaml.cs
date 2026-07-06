using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.Completion;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

public partial class ScriptExecutorTabView : UserControl
{
    private TextEditor? _scriptEditor;
    private ScriptExecutorTabViewModel? _currentVm;
    // Guards the editor↔VM feedback loop while we push ScriptText INTO the editor.
    private bool _suppressScriptSync;
    private bool _completionAttached;

    public ScriptExecutorTabView()
    {
        InitializeComponent();
        _scriptEditor = this.FindControl<TextEditor>("ScriptEditor");
        if (_scriptEditor is not null)
        {
            _scriptEditor.TextChanged += OnScriptEditorTextChanged;
            _scriptEditor.KeyDown += OnScriptEditorKeyDown;
        }
        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Autocomplete + Find/Replace (Ctrl+F/Ctrl+H) + double-click/Ctrl+Click navigation,
    // reusing the SQL Editor's services once the owning MainWindowViewModel is reachable.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_completionAttached) return;
        if (this.FindAncestorOfType<Window>()?.DataContext is MainWindowViewModel mainVm && _scriptEditor is not null)
        {
            SqlEditorBehavior.Attach(_scriptEditor, mainVm);
            _completionAttached = true;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged -= OnVmPropertyChanged;
            _currentVm.NavigateToStatementRequested -= OnNavigateToStatement;
        }
        _currentVm = DataContext as ScriptExecutorTabViewModel;
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            _currentVm.NavigateToStatementRequested += OnNavigateToStatement;
            PushScript();
        }
    }

    // F5 runs the script (same key as the SQL Editor's execute).
    private void OnScriptEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 && _currentVm is not null && _currentVm.RunCommand.CanExecute(null))
        {
            _currentVm.RunCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnScriptEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressScriptSync || _currentVm is null || _scriptEditor is null) return;
        _currentVm.ScriptText = _scriptEditor.Text;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptExecutorTabViewModel.ScriptText)) PushScript();
    }

    private void PushScript()
    {
        if (_scriptEditor is null || _currentVm is null) return;
        var text = _currentVm.ScriptText ?? string.Empty;
        if (_scriptEditor.Text == text) return;
        _suppressScriptSync = true;
        try { _scriptEditor.Text = text; }
        finally { _suppressScriptSync = false; }
    }

    // Double-click a result row → select + scroll the editor to that statement's source.
    private void OnResultsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_currentVm is null) return;
        if ((e.Source as Control)?.DataContext is ScriptResultRowViewModel row)
        {
            _currentVm.NavigateToStatement(row);
        }
    }

    private void OnNavigateToStatement(int offset, int length)
    {
        if (_scriptEditor is null) return;
        int docLength = _scriptEditor.Document?.TextLength ?? 0;
        if (offset < 0 || offset > docLength) return;
        int safeLength = Math.Min(length, docLength - offset);
        _scriptEditor.Select(offset, Math.Max(0, safeLength));
        var line = _scriptEditor.Document?.GetLineByOffset(offset);
        if (line is not null) _scriptEditor.ScrollToLine(line.LineNumber);
        _scriptEditor.Focus();
    }

    private void ApplyEditorTheme()
    {
        if (_scriptEditor is null) return;
        var theme = ActualThemeVariant;
        var name = theme == ThemeVariant.Light ? App.FirebirdSyntaxLightName : App.FirebirdSyntaxName;
        _scriptEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(name);
        if (Application.Current?.Resources.TryGetResource("SelectionBrush", theme, out var res) == true
            && res is IBrush brush)
        {
            _scriptEditor.TextArea.SelectionBrush = brush;
        }
    }
}
