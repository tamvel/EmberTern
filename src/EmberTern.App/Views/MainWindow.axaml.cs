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

    // Drag-and-drop state. The DragDrop API on TreeView in Avalonia 12 is unreliable
    // (item containers are virtualized, drop events drop while the cursor is over
    // children, etc.), so we drive everything from pointer events on the TreeView.
    private object? _dragSource;          // ConnectionNodeViewModel or FolderNodeViewModel candidate
    private Point _dragStart;             // pointer position at PointerPressed, in tree coords
    private bool _isDragging;             // crossed the 8px threshold
    private object? _currentDropTarget;   // VM whose IsDropTarget is currently set
    private DropPosition _currentDropPosition;
    private const double DragThreshold = 8.0;

    // Built lazily when the VM attaches (OnDataContextChanged), from the VM's store
    // directory + protector — so the View's workspace section writes into the SAME
    // shared settings.dat the VM uses. Deliberately NOT a field initializer with the
    // default dir: that would (a) hit the real %AppData% during headless tests and
    // (b) trigger legacy-file migration before any VM is attached.
    private WorkspaceStore? _workspaceStore;
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

        var sidebar = this.FindControl<TreeView>("SidebarTree");
        if (sidebar is not null)
        {
            // Tunnel PointerPressed so we see it before TreeView's own selection handling
            // (otherwise selection moves before we record the drag candidate). Moved/Released
            // bubble up — defaults are fine for those.
            sidebar.AddHandler(PointerPressedEvent, OnSidebarPointerPressed, RoutingStrategies.Tunnel);
            sidebar.PointerMoved += OnSidebarPointerMoved;
            sidebar.PointerReleased += OnSidebarPointerReleased;
            sidebar.PointerCaptureLost += OnSidebarPointerCaptureLost;
        }

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
            _workspaceStore?.Save(state);
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
            _currentVm.AddConnectionRequested -= OnAddConnectionRequested;
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
            _currentVm.AddConnectionRequested += OnAddConnectionRequested;
            _currentVm.SelectedQueryTextProvider = GetSqlEditorSelection;
            _currentVm.ReplaceSelectedOrAllText = ReplaceSqlEditorSelectionOrAll;

            // Restore VM state once, the first time a VM is attached. Done here (not in
            // Opened) so QueryText is set before we push it into the editor below, and so
            // the workspace store is built from the VM's settings location (never the real
            // %AppData% in tests). Bounds restore happens separately in OnWindowOpened —
            // keep _pendingRestore alive until both consumers have used it.
            if (!_vmRestored)
            {
                var settingsDir = Path.GetDirectoryName(_currentVm.Store.FilePath)!;
                _workspaceStore = new WorkspaceStore(settingsDir, _currentVm.Store.Protector);
                _pendingRestore = _workspaceStore.Load();
                if (_pendingRestore is not null)
                {
                    _currentVm.RestoreWorkspace(_pendingRestore);
                }
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

    // ---- Sidebar drag & drop --------------------------------------------------

    private void OnSidebarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TreeView tree) return;
        var point = e.GetCurrentPoint(tree);
        if (!point.Properties.IsLeftButtonPressed) return;

        // Find the closest row VM under the pointer. We only initiate drags for
        // Folder / Connection rows — clicking a category or a metadata leaf does
        // not start a drag.
        var vm = FindRowVmAtPointer(tree, point.Position);
        if (vm is not (ConnectionNodeViewModel or FolderNodeViewModel)) return;

        // Don't grab connections that are mid-connect/disconnect — moving them
        // would race with the event firing on the async-continuation thread.
        if (vm is ConnectionNodeViewModel cn && IsBusyConnection(cn)) return;

        _dragSource = vm;
        _dragStart = point.Position;
        _isDragging = false;
        // Leave PointerPressed routing un-handled so the TreeView's own selection
        // handling still runs (clicking a connection still selects it normally if
        // the user doesn't actually drag).
    }

    private void OnSidebarPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource is null) return;
        if (sender is not TreeView tree) return;
        var point = e.GetCurrentPoint(tree);
        if (!point.Properties.IsLeftButtonPressed)
        {
            ClearDragState();
            return;
        }

        var pos = point.Position;
        if (!_isDragging)
        {
            var dx = pos.X - _dragStart.X;
            var dy = pos.Y - _dragStart.Y;
            if (dx * dx + dy * dy < DragThreshold * DragThreshold) return;
            _isDragging = true;
            MarkSourceDragging(true);
            e.Pointer.Capture(tree);
            tree.Cursor = new Avalonia.Input.Cursor(StandardCursorType.DragMove);
        }

        UpdateDropTarget(tree, pos);
    }

    private void OnSidebarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not TreeView tree) return;
        try
        {
            if (!_isDragging || _dragSource is null) return;
            var source = _dragSource;
            var target = _currentDropTarget;
            var position = _currentDropPosition;
            if (target is null || ReferenceEquals(source, target)) return;
            if (_currentVm is null) return;

            _currentVm.ExecuteDrop(source, target, position);
        }
        finally
        {
            tree.Cursor = Avalonia.Input.Cursor.Default;
            e.Pointer.Capture(null);
            ClearDragState();
        }
    }

    private void OnSidebarPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is TreeView tree) tree.Cursor = Avalonia.Input.Cursor.Default;
        ClearDragState();
    }

    private void UpdateDropTarget(TreeView tree, Point pos)
    {
        var hover = FindRowVmAtPointer(tree, pos);
        var (target, position) = ResolveDropTarget(_dragSource!, hover, tree, pos);

        if (!ReferenceEquals(target, _currentDropTarget))
        {
            SetIsDropTarget(_currentDropTarget, false);
            SetIsDropTarget(target, true);
            _currentDropTarget = target;
        }
        _currentDropPosition = position;
    }

    private static (object? target, DropPosition position) ResolveDropTarget(
        object source, object? hover, TreeView tree, Point pos)
    {
        // Dropped onto empty area or outside any row: if source is a connection in
        // a folder, treat as "move to root" by targeting a root sibling or — if no
        // siblings exist — surface a null target and let release no-op. Spec says
        // "drop outside any valid target → cancel" so we keep it simple: no target.
        if (hover is null) return (null, DropPosition.After);

        if (ReferenceEquals(source, hover)) return (null, DropPosition.After);

        if (source is ConnectionNodeViewModel)
        {
            if (hover is FolderNodeViewModel) return (hover, DropPosition.Into);
            if (hover is ConnectionNodeViewModel)
            {
                // Top half = Before, bottom half = After (relative to the row container).
                var pos2 = PositionFromVerticalSplit(tree, hover, pos);
                return (hover, pos2);
            }
            return (null, DropPosition.After);
        }

        if (source is FolderNodeViewModel)
        {
            // Folders only live at root. Reorder relative to another folder or a
            // root-level connection. (ExecuteDrop rejects folder-into-folder-member
            // contexts itself, so we don't have to filter here.)
            if (hover is FolderNodeViewModel or ConnectionNodeViewModel)
            {
                var pos2 = PositionFromVerticalSplit(tree, hover, pos);
                return (hover, pos2);
            }
            return (null, DropPosition.After);
        }

        return (null, DropPosition.After);
    }

    private static DropPosition PositionFromVerticalSplit(TreeView tree, object hoverVm, Point pointerPos)
    {
        // Walk the tree's visual tree for the TreeViewItem whose DataContext == hoverVm.
        // Use its bounds (translated to tree coords) to decide top vs bottom half.
        var item = FindTreeViewItemFor(tree, hoverVm);
        if (item is null) return DropPosition.After;
        var topLeft = item.TranslatePoint(new Point(0, 0), tree);
        if (topLeft is null) return DropPosition.After;
        var midY = topLeft.Value.Y + item.Bounds.Height / 2.0;
        return pointerPos.Y < midY ? DropPosition.Before : DropPosition.After;
    }

    private static TreeViewItem? FindTreeViewItemFor(Visual root, object dataContext)
    {
        foreach (var d in root.GetVisualDescendants())
        {
            if (d is TreeViewItem tvi && ReferenceEquals(tvi.DataContext, dataContext))
            {
                return tvi;
            }
        }
        return null;
    }

    private static object? FindRowVmAtPointer(TreeView tree, Point pos)
    {
        var hit = tree.InputHitTest(pos);
        if (hit is not Visual v) return null;
        // Walk up until we find a TreeViewItem; its DataContext is the row VM.
        var item = v.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        return item?.DataContext;
    }

    private static bool IsBusyConnection(ConnectionNodeViewModel cn)
        // CanConnect() / CanDisconnect() are private. The CommandManager-managed
        // CanExecute on the relay commands is the next-best signal — but it's
        // equivalent to !IsConnected/IsConnected. There's no exposed "connecting"
        // flag today, so the simplest correct check is: never grab nodes that
        // can neither connect nor disconnect (would mean a connection mid-flight).
        => !cn.ConnectCommand.CanExecute(null) && !cn.DisconnectCommand.CanExecute(null);

    private void MarkSourceDragging(bool dragging)
    {
        switch (_dragSource)
        {
            case ConnectionNodeViewModel cn: cn.IsDragging = dragging; break;
            case FolderNodeViewModel fn: fn.IsDragging = dragging; break;
        }
    }

    private static void SetIsDropTarget(object? vm, bool value)
    {
        switch (vm)
        {
            case ConnectionNodeViewModel cn: cn.IsDropTarget = value; break;
            case FolderNodeViewModel fn: fn.IsDropTarget = value; break;
        }
    }

    private void ClearDragState()
    {
        MarkSourceDragging(false);
        SetIsDropTarget(_currentDropTarget, false);
        _dragSource = null;
        _currentDropTarget = null;
        _isDragging = false;
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

    private void OnSavedQueryNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control c && c.DataContext is SavedQueryViewModel sq)
        {
            sq.BeginRenameCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnRenameTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is SavedQueryViewModel sq && sq.IsRenaming)
        {
            sq.CommitRenameCommand.Execute(null);
        }
    }

    private void OnRenameTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not SavedQueryViewModel sq) return;
        if (e.Key == Key.Enter)
        {
            sq.CommitRenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            sq.CancelRenameCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void OnNewConnectionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        // Detect folder context from the sidebar selection — a selected folder
        // (or a selected connection inside a folder) targets that folder; anything
        // else lands the new connection at the root (legacy behaviour).
        var folderId = DetectFolderContext(vm);
        await OnAddConnectionRequested(folderId);
    }

    private string? DetectFolderContext(MainWindowViewModel vm)
    {
        var tree = this.FindControl<TreeView>("SidebarTree");
        if (tree?.SelectedItem is FolderNodeViewModel f) return f.Id;
        if (tree?.SelectedItem is ConnectionNodeViewModel c
            && vm.FolderState.ConnectionFolderMap.TryGetValue(c.Profile.Id, out var fid)
            && !string.IsNullOrEmpty(fid))
        {
            return fid;
        }
        return null;
    }

    private async Task OnAddConnectionRequested(string? folderId)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var dialogVm = new NewConnectionDialogViewModel(vm.Service);
        var dialog = new NewConnectionDialog { DataContext = dialogVm };
        var profile = await dialog.ShowDialog<EmberTern.Core.Connections.ConnectionProfile?>(this);
        if (profile is null) return;

        vm.Store.Upsert(profile);
        // PlaceConnectionInFolder calls PersistFolderState + ReloadConnections itself,
        // so the second branch's bare Reload is the only path that needs the call.
        if (folderId is not null)
        {
            vm.PlaceConnectionInFolder(profile.Id, folderId);
        }
        else
        {
            vm.ReloadConnections();
        }
    }

    private async void OnNewFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var dialog = new NewFolderDialog();
        var name = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrEmpty(name))
        {
            vm.CreateFolder(name);
        }
    }

    private void OnFolderNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control c && c.DataContext is FolderNodeViewModel f)
        {
            f.BeginRenameCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnFolderRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is FolderNodeViewModel f && f.IsRenaming)
        {
            f.CommitRenameCommand.Execute(null);
        }
    }

    private void OnFolderRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not FolderNodeViewModel f) return;
        if (e.Key == Key.Enter)
        {
            f.CommitRenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            f.CancelRenameCommand.Execute(null);
            e.Handled = true;
        }
    }
}
