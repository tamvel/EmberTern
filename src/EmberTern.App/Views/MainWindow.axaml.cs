using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.Completion;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using EmberTern.Core.Sql;
using EmberTern.Core.Workspace;

namespace EmberTern.App.Views;

public partial class MainWindow : Window
{
    private TextEditor? _editor;
    private TextEditor? _ddlEditor;
    private DataGrid? _resultGrid;
    private MainWindowViewModel? _currentVm;
    private SqlCompletionController? _completion;

    private TextBlock? _maxRestoreGlyph;

    private readonly WorkspaceStore _workspaceStore = new();
    private WorkspaceState? _pendingRestore;
    // Tracks the bounds last seen while WindowState was Normal so a closing-while-maximized
    // session doesn't blow away the user's preferred Restore-size and position.
    private WindowBounds _lastNormalBounds;
    private bool _vmRestored;
    private bool _boundsRestored;

    public MainWindow()
    {
        InitializeComponent();
        Icon = new WindowIcon(
            AssetLoader.Open(new Uri("avares://EmberTern/Assets/Branding/EmberTern.ico")));
        _editor = this.FindControl<TextEditor>("SqlEditor");
        _ddlEditor = this.FindControl<TextEditor>("DdlEditor");
        _resultGrid = this.FindControl<DataGrid>("ResultGrid");
        _maxRestoreGlyph = this.FindControl<TextBlock>("MaxRestoreGlyph");

        ApplyEditorThemeColors();
        if (_editor is not null)
        {
            _editor.TextChanged += OnEditorTextChanged;
            _editor.DoubleTapped += OnSqlEditorDoubleTapped;
            _completion = new SqlCompletionController(
                _editor,
                GetCompletionObjects,
                dotTableResolver: ResolveDotTable,
                cachedColumnsProvider: GetCachedColumns,
                ensureColumnsAsync: EnsureColumnsAsync);
        }
        // Re-apply on theme toggle. ActualThemeVariantChanged fires after the
        // resolved variant flips, so the read in ApplySyntaxHighlighting is
        // already on the new theme by the time we get the callback.
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        if (_resultGrid is not null)
        {
            // Avalonia DataGrid doesn't auto-select on right-click — the context menu
            // would then act on the previously-selected row (or nothing). Select the
            // row under the cursor first; leave Handled=false so ContextMenu still opens.
            _resultGrid.PointerPressed += OnResultGridPointerPressed;
        }

        _pendingRestore = _workspaceStore.Load();
        _lastNormalBounds = new WindowBounds
        {
            X = Position.X, Y = Position.Y,
            Width = Width, Height = Height,
            WindowState = "Normal",
        };

        DataContextChanged += OnDataContextChanged;
        PropertyChanged += OnWindowPropertyChanged;
        PositionChanged += OnWindowPositionChanged;
        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            _lastNormalBounds = new WindowBounds
            {
                X = Position.X, Y = Position.Y,
                Width = Width, Height = Height,
                WindowState = "Normal",
            };
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_boundsRestored) return;
        _boundsRestored = true;

        if (_pendingRestore?.WindowBounds is { } b && AreBoundsSane(b))
        {
            Width = b.Width;
            Height = b.Height;
            Position = new PixelPoint((int)b.X, (int)b.Y);
            _lastNormalBounds = new WindowBounds
            {
                X = b.X, Y = b.Y, Width = b.Width, Height = b.Height, WindowState = "Normal",
            };
            if (Enum.TryParse<WindowState>(b.WindowState, ignoreCase: true, out var ws)
                && ws is WindowState.Maximized or WindowState.Normal)
            {
                WindowState = ws;
            }
        }
    }

    private bool AreBoundsSane(WindowBounds b)
    {
        if (b.Width < MinWidth || b.Height < MinHeight) return false;
        if (double.IsNaN(b.X) || double.IsNaN(b.Y)) return false;
        // Require some part of the proposed rectangle to overlap a screen's working area,
        // so monitor changes between sessions don't strand the window off-screen.
        try
        {
            var screens = Screens;
            if (screens is null || screens.All.Count == 0) return true;
            var rect = new PixelRect((int)b.X, (int)b.Y, (int)b.Width, (int)b.Height);
            foreach (var s in screens.All)
            {
                if (s.WorkingArea.Intersects(rect)) return true;
            }
            return false;
        }
        catch
        {
            // Screens not yet available — trust the saved values rather than discard them.
            return true;
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_currentVm is null) return;
        var state = _currentVm.CaptureWorkspace();
        state.WindowBounds = new WindowBounds
        {
            X = _lastNormalBounds.X,
            Y = _lastNormalBounds.Y,
            Width = _lastNormalBounds.Width,
            Height = _lastNormalBounds.Height,
            WindowState = WindowState.ToString(),
        };
        try
        {
            _workspaceStore.Save(state);
        }
        catch (IOException)
        {
            // Closing path — don't block shutdown on a transient I/O hiccup.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Buttons consume their own clicks; clicking the bar background drags the window.
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        // The toolbar buttons live inside the titlebar Border, so DoubleTapped bubbles
        // up from them too. Bail when the original source is inside any Button so
        // double-clicking the +/✎/▶/etc icons doesn't also maximize the window.
        if (e.Source is Visual src && src.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaxRestoreClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty && _maxRestoreGlyph is not null)
        {
            _maxRestoreGlyph.Text = WindowState == WindowState.Maximized ? "❐" : "◻";
        }

        // Snapshot the size while in Normal state. Used at Closing so a maximized
        // session still persists the user's preferred Restore-bounds. Position is
        // tracked separately via PositionChanged (Position isn't an AvaloniaProperty).
        if (WindowState == WindowState.Normal
            && (e.Property == ClientSizeProperty || e.Property == WidthProperty || e.Property == HeightProperty))
        {
            _lastNormalBounds = new WindowBounds
            {
                X = Position.X, Y = Position.Y,
                Width = Width, Height = Height,
                WindowState = "Normal",
            };
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged -= OnVmPropertyChanged;
            _currentVm.EditRequested -= OnEditConnectionRequested;
            _currentVm.ConfirmationRequested -= OnConfirmationRequested;
            _currentVm.ClipboardWriteRequested -= OnClipboardWriteRequested;
            _currentVm.SelectedQueryTextProvider = null;
            _currentVm.ReplaceSelectedOrAllText = null;
        }

        _currentVm = DataContext as MainWindowViewModel;

        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            _currentVm.EditRequested += OnEditConnectionRequested;
            _currentVm.ConfirmationRequested += OnConfirmationRequested;
            _currentVm.ClipboardWriteRequested += OnClipboardWriteRequested;
            _currentVm.SelectedQueryTextProvider = GetSqlEditorSelection;
            _currentVm.ReplaceSelectedOrAllText = ReplaceSqlEditorSelectionOrAll;

            // Restore VM state once, the first time a VM is attached. Done here (not in
            // Opened) so QueryText is set before we push it into the editor below.
            // Bounds restore happens separately in OnWindowOpened — keep _pendingRestore
            // alive until both consumers have used it.
            if (!_vmRestored && _pendingRestore is not null)
            {
                _currentVm.RestoreWorkspace(_pendingRestore);
                _vmRestored = true;
            }

            if (_editor is not null && _editor.Text != _currentVm.QueryText)
            {
                _editor.Text = _currentVm.QueryText;
            }
        }
    }

    private async Task OnClipboardWriteRequested(string text)
    {
        var clipboard = Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void OnSidebarTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_currentVm is null)
        {
            return;
        }

        // Only update SelectedConnection when a connection root is picked; selecting a
        // category/leaf leaves SelectedConnection at the last connection the user picked,
        // which is what the toolbar's Edit/Copy/Delete commands want to act on.
        if (sender is TreeView tree && tree.SelectedItem is ConnectionNodeViewModel cn)
        {
            _currentVm.Metadata.SelectedConnection = cn;
        }
    }

    private void OnConnectionNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control c
            && c.DataContext is ConnectionNodeViewModel cn
            && !cn.IsConnected
            && cn.ConnectCommand.CanExecute(null))
        {
            cn.ConnectCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnMetadataNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control c && c.DataContext is MetadataNodeViewModel node
            && node.IsActionable && node.OpenDdlCommand.CanExecute(null))
        {
            node.OpenDdlCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async System.Threading.Tasks.Task<bool> OnConfirmationRequested(ConfirmRequest request)
    {
        var dialog = new ConfirmDialog { DataContext = new ConfirmDialogViewModel(request) };
        return await dialog.ShowDialog<bool>(this);
    }

    private async void OnEditConnectionRequested(EmberTern.Core.Connections.ConnectionProfile profile)
    {
        if (_currentVm is null)
        {
            return;
        }

        var dialogVm = new NewConnectionDialogViewModel(_currentVm.Service);
        dialogVm.LoadFromProfile(profile);
        var dialog = new NewConnectionDialog { DataContext = dialogVm };
        var updated = await dialog.ShowDialog<EmberTern.Core.Connections.ConnectionProfile?>(this);
        if (updated is not null)
        {
            _currentVm.Store.Upsert(updated);
            _currentVm.ReloadConnections();
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentResultVersionTag))
        {
            PopulateResultGrid(_currentVm?.CurrentResult);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.SelectedWorkspaceTab)
              || e.PropertyName == nameof(MainWindowViewModel.ActiveDdlText)
              || e.PropertyName == nameof(MainWindowViewModel.QueryText))
        {
            if (_currentVm is null) return;

            // Push DDL text into the read-only editor; two-way binding TextEditor.Text is flaky.
            if (_ddlEditor is not null)
            {
                var text = _currentVm.ActiveDdlText;
                if (_ddlEditor.Text != text)
                {
                    _ddlEditor.Text = text;
                }
            }

            // Per-connection workspace: QueryText swaps when the active connection
            // changes, so the SQL editor must follow. The != guard breaks the
            // TextChanged → VM.QueryText → SqlEditor.Text loop.
            if (_editor is not null && _editor.Text != _currentVm.QueryText)
            {
                _editor.Text = _currentVm.QueryText;
            }
        }
    }

    // Double-click on a word in the SQL editor: if the word matches a loaded metadata
    // object, open its DDL tab (same path as the metadata-tree double-click). If not,
    // leave the editor's default word-select behaviour in place.
    private void OnSqlEditorDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_currentVm is null || _editor is null) return;
        var text = _editor.Text;
        if (string.IsNullOrEmpty(text)) return;

        var word = SqlCompletionContext.GetWordAt(text, _editor.CaretOffset);
        if (word.IsEmpty) return;

        if (_currentVm.TryOpenDdlForWord(word.Text))
        {
            // Mark handled so AvaloniaEdit doesn't keep the word-selection live in the
            // SQL editor — focus is moving to the DDL tab and any lingering selection
            // there is just noise.
            e.Handled = true;
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_currentVm is not null && _editor is not null)
        {
            _currentVm.QueryText = _editor.Text;
        }
    }

    // Source for the SQL editor's autocomplete: keywords always + objects from
    // the active connection's loaded metadata categories. Lives on the VM so the
    // controller (UI-side) can stay free of cross-VM traversal.
    private System.Collections.Generic.IReadOnlyList<MetadataObject> GetCompletionObjects()
        => _currentVm?.EnumerateLoadedObjects() ?? System.Array.Empty<MetadataObject>();

    // Dot autocomplete plumbing — pure resolve on the VM, fetched columns
    // cached there too. Controller just funnels them into the popup.
    private string? ResolveDotTable(string text, int caret)
        => _currentVm?.ResolveDotTable(text, caret);

    private System.Collections.Generic.IReadOnlyList<EmberTern.Core.Metadata.ColumnSpec>? GetCachedColumns(string tableName)
        => _currentVm?.TryGetCachedColumns(tableName);

    private Task<System.Collections.Generic.IReadOnlyList<EmberTern.Core.Metadata.ColumnSpec>> EnsureColumnsAsync(string tableName)
        => _currentVm?.EnsureColumnsAsync(tableName)
           ?? Task.FromResult<System.Collections.Generic.IReadOnlyList<EmberTern.Core.Metadata.ColumnSpec>>(System.Array.Empty<EmberTern.Core.Metadata.ColumnSpec>());

    // Returns the currently-selected text in the SQL editor, or null when nothing
    // is selected. Used by the VM to scope Execute / Format SQL to the selection.
    private string? GetSqlEditorSelection()
    {
        if (_editor is null) return null;
        var sel = _editor.SelectedText;
        return string.IsNullOrEmpty(sel) ? null : sel;
    }

    // Replaces the editor's selection with the given text (re-selecting the
    // replacement); when nothing is selected, overwrites the whole document.
    private void ReplaceSqlEditorSelectionOrAll(string text)
    {
        if (_editor is null) return;
        if (_editor.SelectionLength > 0)
        {
            var start = _editor.SelectionStart;
            _editor.Document.Replace(start, _editor.SelectionLength, text);
            _editor.Select(start, text.Length);
        }
        else
        {
            _editor.Text = text;
        }
    }

    private void PopulateResultGrid(QueryResult? result)
    {
        if (_resultGrid is null)
        {
            return;
        }

        _resultGrid.Columns.Clear();
        _resultGrid.ItemsSource = null;

        if (result is null || !result.HasResultSet)
        {
            return;
        }

        for (int i = 0; i < result.Columns.Count; i++)
        {
            var column = result.Columns[i];
            _resultGrid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Name,
                Binding = new Binding($"[{i}]")
                {
                    StringFormat = "{0}",
                    FallbackValue = string.Empty,
                    TargetNullValue = string.Empty,
                },
            });
        }

        _resultGrid.ItemsSource = result.Rows;
    }

    private void OnThemeToggleClick(object? sender, RoutedEventArgs e)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var current = app.ActualThemeVariant;
        app.RequestedThemeVariant = current == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
        => ApplyEditorThemeColors();

    // AvaloniaEdit's XSHD definitions and TextArea selection brush are both
    // static — neither binds through DynamicResource. Same pattern as
    // IconBrushConverter for the metadata tree: pick the right palette on
    // theme change and re-assign. Covers syntax highlighting + selection
    // background; selection foreground is left at the editor default (text
    // on the dark #094771 / light #CCE4F7 selection reads fine without an
    // override).
    private void ApplyEditorThemeColors()
    {
        var theme = ActualThemeVariant;
        var name = theme == ThemeVariant.Light
            ? App.FirebirdSyntaxLightName
            : App.FirebirdSyntaxName;
        var syntax = HighlightingManager.Instance.GetDefinition(name);

        IBrush? selection = null;
        if (Application.Current?.Resources.TryGetResource("SelectionBrush", theme, out var res) == true
            && res is IBrush b)
        {
            selection = b;
        }

        ApplyToEditor(_editor, syntax, selection);
        ApplyToEditor(_ddlEditor, syntax, selection);
    }

    private static void ApplyToEditor(TextEditor? editor, IHighlightingDefinition? syntax, IBrush? selection)
    {
        if (editor is null) return;
        editor.SyntaxHighlighting = syntax;
        if (selection is not null)
        {
            editor.TextArea.SelectionBrush = selection;
        }
    }

    private void OnResultGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_resultGrid is null) return;
        var props = e.GetCurrentPoint(_resultGrid).Properties;
        if (!props.IsRightButtonPressed) return;
        if (e.Source is not Visual v) return;
        var row = v.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row is null) return;
        // SelectedItem flows to DataGrid.SelectedIndex automatically. DataContext is the
        // bound row object (object?[] for our result rows).
        _resultGrid.SelectedItem = row.DataContext;
        // Leave e.Handled = false so the right-click also bubbles up and triggers the
        // ContextMenu.
    }

    private void OnCopyCellClick(object? sender, RoutedEventArgs e)
        => InvokeCopy(CopyGridMode.Cell);

    private void OnCopyRowClick(object? sender, RoutedEventArgs e)
        => InvokeCopy(CopyGridMode.Row);

    private void OnCopyRowWithHeadersClick(object? sender, RoutedEventArgs e)
        => InvokeCopy(CopyGridMode.RowWithHeaders);

    private void OnCopyAllWithHeadersClick(object? sender, RoutedEventArgs e)
        => InvokeCopy(CopyGridMode.AllWithHeaders);

    private async void InvokeCopy(CopyGridMode mode)
    {
        if (_currentVm is null || _resultGrid is null) return;
        var rowIndex = _resultGrid.SelectedIndex;
        var colIndex = _resultGrid.CurrentColumn?.DisplayIndex ?? 0;
        await _currentVm.CopyGridAsync(mode, rowIndex, colIndex);
    }

    private async void OnNewConnectionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var dialogVm = new NewConnectionDialogViewModel(vm.Service);
        var dialog = new NewConnectionDialog { DataContext = dialogVm };
        var profile = await dialog.ShowDialog<EmberTern.Core.Connections.ConnectionProfile?>(this);
        if (profile is not null)
        {
            vm.Store.Upsert(profile);
            vm.ReloadConnections();
        }
    }
}
