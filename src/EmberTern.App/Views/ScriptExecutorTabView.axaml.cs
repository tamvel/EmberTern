using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.Completion;
using EmberTern.App.Sql;
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
        // F5 is not handled here: it is CommandId.Go, declared in Commands.CommandCatalog for this tab kind
        // and dispatched by the router. The local handler this replaced fired only while the SCRIPT EDITOR
        // held focus, so F5 with focus on the mode picker or the results grid fell through to the window and
        // executed the SQL Editor's query instead of the script.
        if (_scriptEditor is not null)
        {
            _scriptEditor.TextChanged += OnScriptEditorTextChanged;
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
            _currentVm.OpenRequested -= OnOpenRequested;
            _currentVm.SaveRequested -= OnSaveRequested;
        }
        _currentVm = DataContext as ScriptExecutorTabViewModel;
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            _currentVm.NavigateToStatementRequested += OnNavigateToStatement;
            _currentVm.OpenRequested += OnOpenRequested;
            _currentVm.SaveRequested += OnSaveRequested;
            PushScript();
        }
    }

    private static readonly FilePickerFileType SqlFileType =
        new(UiStrings.FilePickerSqlScripts) { Patterns = new[] { "*.sql" } };

    // Open a .sql into the editor. .NET's default reader handles BOM'd or no-BOM UTF-8.
    private async Task OnOpenRequested()
    {
        if (_currentVm is null) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = UiStrings.ScriptOpenTooltip,
                AllowMultiple = false,
                FileTypeFilter = new[] { SqlFileType, FilePickerFileTypes.All },
            });
            if (files.Count == 0) return;
            var path = files[0].Path.LocalPath;
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            _currentVm.LoadScript(text, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _currentVm.ReportFileError(ex.Message);
        }
    }

    // Save the script as UTF-8 without a BOM (gotcha #178 — isql/IBExpert choke on a BOM).
    private async Task OnSaveRequested()
    {
        if (_currentVm is null) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        try
        {
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = UiStrings.ScriptSaveTooltip,
                SuggestedFileName = "script.sql",
                DefaultExtension = "sql",
                FileTypeChoices = new[] { SqlFileType, FilePickerFileTypes.All },
            });
            if (file is null) return;
            var path = file.Path.LocalPath;
            await SqlFileWriter.WriteAsync(path, _currentVm.ScriptText).ConfigureAwait(true);
            _currentVm.ReportFileSaved(Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _currentVm.ReportFileError(ex.Message);
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
