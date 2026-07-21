using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.Behaviors;
using EmberTern.App.Completion;
using EmberTern.App.Controls;
using EmberTern.App.Sql;
using EmberTern.App.ViewModels;
using EmberTern.Core.Export;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using EmberTern.Core.Settings;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Templates;
using EmberTern.Core.Workspace;

namespace EmberTern.App.Views;

public partial class MainWindow : Window
{
    private TextEditor? _editor;
    private TextEditor? _ddlEditor;
    private DataGrid? _resultGrid;
    private MainWindowViewModel? _currentVm;
    private SqlCompletionController? _completion;
    // Guards the one-time, VM-arrival wiring of the main SQL editor's language capabilities (D3). The
    // window's VM is set after construction (App.axaml.cs: new MainWindow { DataContext = … }), so the
    // shared SqlEditorBehavior.Attach — which needs a stable, non-null VM — runs once the VM first arrives
    // in OnDataContextChanged, not in the ctor.
    private bool _completionAttached;
    // Feeds + navigates the Diagnostics bottom tab (S4/S5). The SQL Editor has a single SQL document, so
    // the LastFocusedSqlDocument rule collapses onto it — but it goes through the SAME host as the object
    // editors on purpose: one targeting mechanism, so the panel and F8 can never disagree anywhere.
    private readonly Completion.DiagnosticsPanelHost _diagnostics;

    private SvgIcon? _maxRestoreGlyph;

    // Drag-and-drop state. The DragDrop API is unreliable for virtualized items in
    // Avalonia 12 (containers recycle, drop events fire over children, etc.), so we drive
    // everything from pointer events on the sidebar ListBox.
    private object? _dragSource;          // ConnectionNodeViewModel or FolderNodeViewModel candidate
    private Point _dragStart;             // pointer position at PointerPressed, in tree coords
    private bool _isDragging;             // crossed the 8px threshold
    private object? _currentDropTarget;   // VM whose IsDropTarget is currently set
    private DropPosition _currentDropPosition;
    private const double DragThreshold = 8.0;

    // SQL-template Drag & Drop (metadata leaf → SQL editor). Distinct from the pointer-based
    // folder/connection reorder above: metadata leaves aren't reorder sources, so this uses
    // the built-in DragDrop API (cross-control, stable single-editor drop target). The flyout
    // is built from the object KIND on drop (no metadata read); the object's metadata loads
    // only after the user picks a template.
    private MetadataNodeViewModel? _snippetDragCandidate;
    private Point _snippetDragStart;
    private PointerPressedEventArgs? _snippetDragPressArgs;
    private bool _snippetDropAttached;

    // Type-to-filter: when the tree has focus and the user starts typing, focus jumps to
    // the sidebar filter box and the typed character goes there (subsequent typing is
    // handled natively by the now-focused TextBox). Ctrl+F focuses the filter; Escape in
    // the filter clears it and returns focus to the tree. Cached once the template applies.
    private TextBox? _sidebarFilterBox;

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
    private bool _scrollDiagHooked;

    // Resizable / collapsible layout (Part 2 + 3). Definitions are reached through
    // their parent grids (a ColumnDefinition isn't a Control). Width/height + the
    // collapsed flag persist in WorkspaceState, the same way WindowBounds does.
    private ColumnDefinition? _sidebarColumn;
    private RowDefinition? _editorRow;
    private RowDefinition? _resultsRow;
    private Border? _sidebarPanel;
    private GridSplitter? _sidebarSplitter;
    private Border? _sidebarRail;
    private GridSplitter? _resultsSplitter;
    private double _expandedSidebarWidth = DefaultSidebarWidth;
    private double _resultsHeight = DefaultResultsHeight;
    private bool _sidebarCollapsed;
    // True while the results panel is maximized (editor row collapsed, results
    // row takes all space). Toggled by the splitter double-click AND the tab-strip
    // button; restoring returns to the previous (possibly dragged) results height.
    private bool _resultsMaximized;
    private bool _layoutRestored;
    private const double DefaultSidebarWidth = 280;
    private const double MinSidebarWidth = 220;
    private const double MaxSidebarWidth = 600;
    private const double DefaultResultsHeight = 280;
    private const double MinResultsHeight = 120;
    // Column-structure tracking for the Results grid so paging / sort re-binds
    // don't rebuild columns (preserves persisted widths + sort-arrow headers).
    private readonly System.Collections.Generic.List<string> _resultColumnNames = new();

    public MainWindow()
    {
        InitializeComponent();
        Icon = new WindowIcon(
            AssetLoader.Open(new Uri("avares://EmberTern/Assets/Branding/EmberTern.ico")));
        _diagnostics = new Completion.DiagnosticsPanelHost(
            () => _currentVm?.DiagnosticsPanel,
            () => _editor,
            // The SQL editor sits above its panel and is normally visible, so there is no tab to switch
            // back to — except when the results panel is maximized, which collapses the editor's row to
            // zero height. Restore the split through the existing toggle rather than a second sizing path.
            reveal: _ => { if (_resultsMaximized) ToggleResultsMaximized(); });
        _editor = this.FindControl<TextEditor>("SqlEditor");
        _ddlEditor = this.FindControl<TextEditor>("DdlEditor");
        if (_ddlEditor is not null) SqlEditorBehavior.AttachReadOnlyHighlighting(_ddlEditor);
        _resultGrid = this.FindControl<DataGrid>("ResultGrid");
        _maxRestoreGlyph = this.FindControl<SvgIcon>("MaxRestoreGlyph");

        ApplyEditorThemeColors();
        if (_editor is not null)
        {
            // The main SQL editor's language capabilities — completion, semantic highlighting, hover +
            // navigation, squiggles, related-elements, language-completion, typing-ergonomics, search — are
            // wired in OnDataContextChanged, through the SAME shared SqlEditorBehavior.Attach the object
            // editors use: ONE attach path (D3; gotcha #219, previously two hand-maintained copies). They
            // cannot be wired here because the window's VM is set after construction (App.axaml.cs:
            // new MainWindow { DataContext = … }), and the shared path needs a stable, non-null
            // MainWindowViewModel — so it runs once, when the VM first arrives ("subscribe once the VM
            // arrives"). Only the TextChanged→QueryText sync, which needs no VM, stays here.
            // Double-click (INSERT/VALUES helper + name-based open) is owned by NavigationController.
            _editor.TextChanged += OnEditorTextChanged;
        }
        // S5: the panel's activation gestures (double-click / Enter / F8) target the active SQL document.
        var diagnosticsPanel = this.FindControl<DiagnosticsPanelView>("SqlDiagnosticsPanel");
        if (diagnosticsPanel is not null) diagnosticsPanel.Navigator = _diagnostics;
        if (_ddlEditor is not null)
        {
            // The read-only DDL preview has no language model → the model-less overload runs the text-based
            // producers only (selection occurrences + bracket matching); BEGIN/END and caret-symbol need a model.
            Completion.RelatedElementsRenderer.Attach(_ddlEditor, () => null);
            Completion.EditorSearch.Install(_ddlEditor);
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
            // 3-state header sort (asc → desc → none). Avalonia's DataGridColumnEventArgs
            // can't be cancelled, so instead of using the built-in (2-state) sort we keep
            // the grid non-sortable and detect header clicks via a tunneled PointerPressed,
            // driving the cycle through the VM (client-side sort over the materialized set
            // via the shared RowIndexComparer).
            _resultGrid.AddHandler(PointerPressedEvent, OnResultHeaderPointerPressed, RoutingStrategies.Tunnel);
            // Filter-from-cell: capture the right-clicked cell (gotcha #99) so the
            // context-menu's Filter-by/Exclude/Contains act on the exact cell.
            _resultGrid.CellPointerPressed += OnResultCellPointerPressed;
        }

        // Resizable-layout controls. ColumnDefinition / RowDefinition aren't Controls,
        // so we reach them through their (named) parent grids; the splitters + sidebar
        // panel are Controls and resolve via FindControl.
        var mainBody = this.FindControl<Grid>("MainBodyGrid");
        if (mainBody is not null && mainBody.ColumnDefinitions.Count > 0)
        {
            _sidebarColumn = mainBody.ColumnDefinitions[0];
        }
        var workspace = this.FindControl<Grid>("WorkspaceGrid");
        if (workspace is not null && workspace.RowDefinitions.Count >= 3)
        {
            _editorRow = workspace.RowDefinitions[0];
            _resultsRow = workspace.RowDefinitions[2];
        }
        _sidebarPanel = this.FindControl<Border>("SidebarPanel");
        _sidebarSplitter = this.FindControl<GridSplitter>("SidebarSplitter");
        _sidebarRail = this.FindControl<Border>("SidebarRail");
        _resultsSplitter = this.FindControl<GridSplitter>("ResultsSplitter");

        var sidebar = this.FindControl<ListBox>("SidebarList");
        if (sidebar is not null)
        {
            // Tunnel PointerPressed so we see it before the ListBox's own selection handling
            // (otherwise selection moves before we record the drag candidate). Moved/Released
            // bubble up — defaults are fine for those.
            sidebar.AddHandler(PointerPressedEvent, OnSidebarPointerPressed, RoutingStrategies.Tunnel);
            sidebar.PointerMoved += OnSidebarPointerMoved;
            sidebar.PointerReleased += OnSidebarPointerReleased;
            sidebar.PointerCaptureLost += OnSidebarPointerCaptureLost;
            // Type-to-filter: while the list (or a row) is focused, this tunnel handler sees
            // printable input — once the filter box is focused the event no longer routes
            // through the list, so the redirect only fires from the list. Redirects the char to
            // the filter box and hands off focus; subsequent keys go to the box natively.
            sidebar.AddHandler(TextInputEvent, OnSidebarTreeTextInput, RoutingStrategies.Tunnel);
        }

        _sidebarFilterBox = this.FindControl<TextBox>("SidebarFilterBox");
        if (_sidebarFilterBox is not null)
        {
            // Escape in the filter clears it and returns focus to the tree.
            _sidebarFilterBox.KeyDown += OnFilterBoxKeyDown;
        }

        // Ctrl+F focuses the sidebar filter from anywhere (tunnel so it wins before any
        // editor's own Ctrl+F). Per the user's explicit request the sidebar filter owns Ctrl+F.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

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
        // Scroll diagnostics (opt-in via EMBERTERN_SCROLL_DIAG). The sidebar ScrollViewer is a
        // template part of SidebarList, present after the template applies — post at
        // Background so layout has settled. Idempotent (guards on already-hooked).
        if (EmberTern.App.Diagnostics.ScrollTrace.IsEnabled)
        {
            Dispatcher.UIThread.Post(HookSidebarScrollDiagnostics, DispatcherPriority.Background);
        }

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

    // Subscribes the sidebar ScrollViewer's ScrollChanged to ScrollTrace so a live repro
    // logs offset/extent/viewport + deltas on every scroll change (EMBERTERN_SCROLL_DIAG).
    // A changing extentH while offsetY moves = Avalonia VSP extent re-estimation.
    private void HookSidebarScrollDiagnostics()
    {
        if (_scrollDiagHooked) return;
        var list = this.FindControl<ListBox>("SidebarList");
        // Prefer the template's PART_ScrollViewer by name; FirstOrDefault could grab a nested
        // (non-scrolling) ScrollViewer. Fall back to the first descendant.
        var descendants = list?.GetVisualDescendants().OfType<ScrollViewer>().ToList();
        var sv = descendants?.FirstOrDefault(s => s.Name == "PART_ScrollViewer")
                 ?? descendants?.FirstOrDefault();
        if (sv is null) return;
        _scrollDiagHooked = true;

        // Count of realized ListBoxItem containers at the moment of a scroll change. With the
        // flat single-VSP list this should stay a stable window and the extent should NOT
        // collapse (unlike the nested-VSP tree).
        int RealizedItems() => list!.GetVisualDescendants().OfType<ListBoxItem>().Count();

        sv.ScrollChanged += (s, e) =>
        {
            if (s is not ScrollViewer v) return;
            EmberTern.App.Diagnostics.ScrollTrace.Scroll(
                v.Offset.Y, v.Extent.Height, v.Viewport.Height, e.OffsetDelta.Y, e.ExtentDelta.Y, RealizedItems());
        };

        // The first diagnostic attempt subscribed ONLY to ScrollChanged and logged nothing
        // during a real drag: a thumb drag whose offset never COMMITS raises no net
        // ScrollChanged. Observe Offset/Extent directly so the churn is captured even when
        // ScrollChanged stays silent (a changing extentH while offsetY moves = VSP extent
        // re-estimation — the "thumb fights/snaps back" fingerprint).
        double lastOffset = sv.Offset.Y, lastExtent = sv.Extent.Height;
        sv.PropertyChanged += (s, e) =>
        {
            if (e.Property != ScrollViewer.OffsetProperty && e.Property != ScrollViewer.ExtentProperty) return;
            if (s is not ScrollViewer v) return;
            EmberTern.App.Diagnostics.ScrollTrace.Scroll(
                v.Offset.Y, v.Extent.Height, v.Viewport.Height,
                v.Offset.Y - lastOffset, v.Extent.Height - lastExtent, RealizedItems());
            lastOffset = v.Offset.Y;
            lastExtent = v.Extent.Height;
        };
        EmberTern.App.Diagnostics.ScrollTrace.Rebuild($"scroll diagnostics hooked (name={sv.Name ?? "?"})");
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

    // Set once the data-loss guard has cleared the close, so the re-entrant Close()
    // skips the guard and goes straight to the persist path.
    private bool _forceClose;

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_currentVm is null) return;

        // First pass: cancel the close, run the WorkGuard (prompts on active
        // transactions / unsaved work), and only re-close if the user allows it.
        // Setting e.Cancel before the first await keeps the window open.
        if (!_forceClose)
        {
            e.Cancel = true;
            bool canClose = await _currentVm.TryCloseApplicationAsync();
            if (!canClose) return;
            _forceClose = true;
            Close();
            return;
        }

        // Flush every still-attached grid's layout while ActualWidth is still valid
        // (before the visual tree is torn down on close).
        GridLayoutBehavior.FlushAll();
        var state = _currentVm.CaptureWorkspace();
        state.WindowBounds = new WindowBounds
        {
            X = _lastNormalBounds.X,
            Y = _lastNormalBounds.Y,
            Width = _lastNormalBounds.Width,
            Height = _lastNormalBounds.Height,
            WindowState = WindowState.ToString(),
        };

        // Persist layout (same pattern as WindowBounds — set on the captured state
        // here, read from the loaded state in ApplyLayoutFromPendingRestore).
        // Sidebar: the live column width when expanded, else the remembered width.
        if (!_sidebarCollapsed && _sidebarColumn is { Width.IsAbsolute: true, Width.Value: > 0 })
        {
            _expandedSidebarWidth = _sidebarColumn.Width.Value;
        }
        state.SidebarWidth = _expandedSidebarWidth;
        state.SidebarCollapsed = _sidebarCollapsed;
        // Results: the live row height when the Query tab is showing it, else the
        // remembered height (it's collapsed to 0 on other tabs).
        if (_currentVm.IsQueryTabActive && _resultsRow is { Height.IsAbsolute: true, Height.Value: > 0 })
        {
            _resultsHeight = _resultsRow.Height.Value;
        }
        state.ResultsPanelHeight = _resultsHeight;
        state.ResultsMaximized = _resultsMaximized;
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
            var key = WindowState == WindowState.Maximized
                ? "Icon.WindowRestore"
                : "Icon.WindowMaximize";
            if (this.TryFindResource(key, out var geometry) && geometry is Geometry g)
            {
                _maxRestoreGlyph.Data = g;
            }
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
            _currentVm.ChoiceRequested -= OnChoiceRequested;
            _currentVm.UserEditDialogRequested -= OnUserEditRequested;
            _currentVm.NewRoleDialogRequested -= OnNewRoleRequested;
            _currentVm.ClipboardWriteRequested -= OnClipboardWriteRequested;
            _currentVm.SaveFileRequested -= OnSaveFileRequested;
            _currentVm.ExportRequested -= OnExportRequested;
            _currentVm.AddConnectionRequested -= OnAddConnectionRequested;
            _currentVm.BatchResultsRequested -= OnBatchResultsRequested;
            _currentVm.GlobalSearchRequested -= OnGlobalSearchRequested;
            _currentVm.RecompileDependentsRequested -= OnRecompileDependentsRequested;
            _currentVm.SmartParametersRequested -= OnSmartParametersRequested;
            _currentVm.EditorFocusRequested -= OnEditorFocusRequested;
            _currentVm.SelectedQueryTextProvider = null;
            _currentVm.ReplaceSelectedOrAllText = null;
        }

        _currentVm = DataContext as MainWindowViewModel;

        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            _currentVm.EditRequested += OnEditConnectionRequested;
            _currentVm.ConfirmationRequested += OnConfirmationRequested;
            _currentVm.ChoiceRequested += OnChoiceRequested;
            _currentVm.UserEditDialogRequested += OnUserEditRequested;
            _currentVm.NewRoleDialogRequested += OnNewRoleRequested;
            _currentVm.ClipboardWriteRequested += OnClipboardWriteRequested;
            _currentVm.SaveFileRequested += OnSaveFileRequested;
            _currentVm.ExportRequested += OnExportRequested;
            _currentVm.AddConnectionRequested += OnAddConnectionRequested;
            _currentVm.BatchResultsRequested += OnBatchResultsRequested;
            _currentVm.GlobalSearchRequested += OnGlobalSearchRequested;
            _currentVm.RecompileDependentsRequested += OnRecompileDependentsRequested;
            _currentVm.SmartParametersRequested += OnSmartParametersRequested;
            _currentVm.EditorFocusRequested += OnEditorFocusRequested;
            _currentVm.SelectedQueryTextProvider = GetSqlEditorSelection;
            _currentVm.ReplaceSelectedOrAllText = ReplaceSqlEditorSelectionOrAll;

            // D3 — wire the main SQL editor's language capabilities ONCE, now that the stable VM has
            // arrived, through the SAME shared path the object editors use (SqlEditorBehavior.Attach). The
            // shared controller subscribes to metadata-changed / metadata-ready and warms referenced objects
            // itself (leak-free via the editor's visual-tree lifetime) — which is why the main window no
            // longer hand-wires those Metadata events: the shared path owns that responsibility now.
            if (!_completionAttached && _editor is not null)
            {
                _completion = SqlEditorBehavior.Attach(_editor, _currentVm);
                // Diagnostics panel + F8 navigation stay a HOST responsibility (this window's own
                // DiagnosticsPanelHost, with its own reveal), outside the shared intrinsic block — exactly
                // like the object editors' callers. (Gotcha #219 consolidation boundary: intrinsic block
                // only; DiagnosticsPanelHost / AmbientModelRefresh / SqlSnippetDropTarget are per-host.)
                _diagnostics.Track(_editor, _completion);
                _completionAttached = true;
            }

            // Metadata-object drop target on the main SQL editor (once — the VM is stable here).
            if (!_snippetDropAttached && _editor is not null)
            {
                SqlSnippetDropTarget.Attach(_editor, _currentVm, SnippetInsertionContext.PlainSql);
                _snippetDropAttached = true;
            }

            // Restore VM state once, the first time a VM is attached. Done here (not in
            // Opened) so QueryText is set before we push it into the editor below, and so
            // the workspace store is built from the VM's settings location (never the real
            // %AppData% in tests). Bounds restore happens separately in OnWindowOpened —
            // keep _pendingRestore alive until both consumers have used it.
            if (!_vmRestored)
            {
                var settingsDir = Path.GetDirectoryName(_currentVm.Store.FilePath)!;
                _workspaceStore = new WorkspaceStore(settingsDir, _currentVm.Store.Protector);
                // Grid layout (column order/width/auto-fit) persists through the same
                // settings.dat — wire the shared store from the VM's location so tests
                // never touch the real %AppData% (see gotcha #88).
                GridLayoutBehavior.Store = new GridProfileStore(settingsDir, _currentVm.Store.Protector);
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

            // Seed this VM's Diagnostics panel from the cached diagnostics. The model builds on a background
            // pass, so it may not be ready at attach time above; and an unchanged restored text raises no
            // TextChanged to republish on. (On a VM swap, the editor attach is guarded to run once, so this
            // is also what re-seeds the incoming VM's panel.)
            _diagnostics.Republish();

            // Apply persisted sidebar width/collapse + results height once the VM
            // (and thus _pendingRestore) is available, then size the results row for
            // the current tab.
            ApplyLayoutFromPendingRestore();
            ApplyResultsRowForActiveTab();
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

    // VM → View Save-file picker for DDL export. Returns the chosen absolute path, or null on
    // cancel; the VM builds the script and writes the file (UTF-8 no BOM). Avalonia's
    // StorageProvider stays here in the view.
    private async Task<string?> OnSaveFileRequested(SaveFileRequest request)
    {
        var picker = StorageProvider;
        if (picker is null) return null;

        var file = await picker.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = request.Title,
            SuggestedFileName = request.SuggestedFileName,
            DefaultExtension = request.Extension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType(request.FilterName) { Patterns = new[] { "*" + request.Extension } },
                FilePickerFileTypes.All,
            },
        });

        return file?.Path.LocalPath;
    }

    // VM → View: open the shared Export dialog for a grid's data source. The dialog owns its own
    // StorageProvider / Clipboard; returns the completed outcome (or null on cancel), which the VM
    // reports to the Messages log.
    private Task<ExportOutcome?> OnExportRequested(ExportDialogRequest request)
        => ExportDialog.ShowAsync(this, new ExportDialogViewModel(request.Source, request.DefaultScope));

    // Post-compile "Recompile dependents?" checklist. Returns the user's selection (null on
    // Skip/Cancel); the VM runs the recompile through the batch pipeline. StorageProvider /
    // dialogs stay in the view.
    private Task<RecompileDependentsResult?> OnRecompileDependentsRequested(RecompileDependentsRequest request)
        => RecompileDependentsDialog.ShowAsync(this, new RecompileDependentsDialogViewModel(request));

    // Smart SQL parameters: F5 on a statement with :name / @name placeholders → reuse the Execute
    // dialog (typed from the catalog for EXECUTE PROCEDURE, else "Unknown") + the value history,
    // keyed per-statement. Returns the ordered bound values (null on Cancel).
    private Task<IReadOnlyList<object?>?> OnSmartParametersRequested(SmartParametersRequest request)
    {
        var vm = new ExecuteProcedureDialogViewModel(
            request.Params,
            request.HistoryKey,
            _currentVm?.Service.ActiveProfile?.Id,
            "AdHocSql",
            _currentVm?.ParameterHistory);
        return ExecuteProcedureDialog.ShowAsync(this, vm);
    }

    // Selection sets the working connection when a connection row is picked — so the titlebar
    // Edit/Copy/Delete/Connect commands act on it. Picking a category/leaf leaves the last
    // connection selected (what those commands want).
    private void OnSidebarListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_currentVm is null || sender is not ListBox list) return;
        // Titlebar Edit/Copy/Delete/Connect still act on the (last) selected connection.
        if (list.SelectedItem is SidebarRow { Node: ConnectionNodeViewModel cn })
        {
            _currentVm.Metadata.SelectedConnection = cn;
        }
        // Feed the multi-selection to the VM so the "Selected" trigger bulk ops know their target.
        _currentVm.Metadata.SetSelectedTriggers(
            list.SelectedItems?.OfType<SidebarRow>() ?? Enumerable.Empty<SidebarRow>());
    }

    // Chevron click → flip the row's underlying node expansion (the controller splices).
    private void OnSidebarChevronClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null) return;
        if (sender is Button { DataContext: SidebarRow row })
        {
            _currentVm.Metadata.ToggleSidebarRow(row);
        }
    }

    // ---- Sidebar type-to-filter --------------------------------------------------

    // Tree (or a tree item) is focused and the user typed a printable char → redirect it
    // into the filter box and move focus there. Subsequent keystrokes land in the now-focused
    // TextBox natively (this tunnel handler only sees input while the tree itself is focused).
    private void OnSidebarTreeTextInput(object? sender, TextInputEventArgs e)
    {
        var box = _sidebarFilterBox;
        if (box is null) return;
        var text = e.Text;
        if (string.IsNullOrEmpty(text) || char.IsControl(text[0])) return;

        box.Focus();
        var current = box.Text ?? string.Empty;
        var caret = Math.Clamp(box.CaretIndex, 0, current.Length);
        box.Text = current.Insert(caret, text);
        box.CaretIndex = caret + text.Length;
        e.Handled = true;
    }

    // Escape in the filter box clears the filter and returns focus to the tree.
    private void OnFilterBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _currentVm is null) return;
        _currentVm.Metadata.FilterText = string.Empty;
        e.Handled = true;
        FocusSidebarTree();
    }

    // Ctrl+F is context-aware: inside a code editor it belongs to that editor's own
    // Find bar (AvaloniaEdit SearchPanel), so we leave the event unhandled and let it
    // tunnel down to the editor. Anywhere else (Explorer, grids, …) it focuses the
    // sidebar filter — the historical behaviour. (Ctrl+H is handled per-editor.)
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Shift+F → Global Search dialog (metadata names + source).
        if (e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            if (_currentVm?.OpenGlobalSearchCommand.CanExecute(null) == true)
            {
                _currentVm.OpenGlobalSearchCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            var focused = (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()) as Visual;
            if (Completion.EditorSearch.IsInsideEditor(focused))
                return; // editor's SearchPanel opens Find

            if (_sidebarFilterBox is not null)
            {
                _sidebarFilterBox.Focus();
                _sidebarFilterBox.SelectAll();
                e.Handled = true;
            }
        }
    }

    private void FocusSidebarTree()
    {
        var list = this.FindControl<ListBox>("SidebarList");
        if (list is null) return;
        // Focus a realized ListBoxItem (the ListBox delegates keyboard focus to its items).
        var target = list.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault();
        if (target is not null) target.Focus();
        else list.Focus();
    }

    // ---- Sidebar drag & drop --------------------------------------------------

    private void OnSidebarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list) return;
        var point = e.GetCurrentPoint(list);

        // Right-click must not collapse a selection that a context-menu action reads. Every sidebar
        // context menu binds to the clicked row's OWN DataContext, so the only selection-dependent
        // action is the trigger-group "Selected" bulk op, which acts on the leaf multi-selection.
        // Without this, right-clicking the Triggers group (a different row than the selected leaves)
        // makes the ListBox select the group → SetSelectedTriggers([]) → the op sees 0 (or 1, when a
        // selected leaf is right-clicked) triggers. Preserve the selection when right-clicking a row
        // already in it, or the trigger group while triggers are selected. The ContextMenu still
        // opens (it fires on release/RightTapped, after this PointerPressed). See gotcha #16/#99.
        if (point.Properties.IsRightButtonPressed)
        {
            var clickedRow = (list.InputHitTest(point.Position) as Visual)
                ?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as SidebarRow;
            var rowIsSelected = clickedRow is not null && list.SelectedItems?.Contains(clickedRow) == true;
            var triggerGroupWithSelection =
                clickedRow?.Node is MetadataNodeViewModel { IsTriggerGroup: true }
                && _currentVm?.Metadata.HasSelectedTriggers == true;
            if (rowIsSelected || triggerGroupWithSelection)
                e.Handled = true;
            // Refresh the clicked node's selection-dependent menu items ("Activate/Deactivate
            // selected (N)" + single-op hiding) to the CURRENT multi-selection before the menu opens.
            (clickedRow?.Node as MetadataNodeViewModel)?.NotifySelectionDependentMenuItems();
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;

        var vm = FindRowVmAtPointer(list, point.Position);

        // Actionable metadata leaf with at least one applicable template → candidate for a
        // snippet drag onto an editor (built-in DragDrop, started once we cross the threshold).
        if (vm is MetadataNodeViewModel node && node.IsActionable && node.Object is not null
            && _currentVm is not null && _currentVm.HasSnippetTemplates(node.Kind))
        {
            _snippetDragCandidate = node;
            _snippetDragStart = point.Position;
            _snippetDragPressArgs = e; // DoDragDropAsync needs the originating press args
            return; // leave selection to the ListBox; don't start a reorder drag
        }

        // Only Folder / Connection rows initiate a reorder drag; category rows don't.
        if (vm is not (ConnectionNodeViewModel or FolderNodeViewModel)) return;

        // Don't grab connections that are mid-connect/disconnect — moving them
        // would race with the event firing on the async-continuation thread.
        if (vm is ConnectionNodeViewModel cn && IsBusyConnection(cn)) return;

        _dragSource = vm;
        _dragStart = point.Position;
        _isDragging = false;
        // Leave routing un-handled so the ListBox's own selection still runs on a plain click.
    }

    private void OnSidebarPointerMoved(object? sender, PointerEventArgs e)
    {
        // Snippet drag (metadata leaf → editor). Once past the threshold, hand off to the
        // built-in DragDrop loop, which manages its own capture/cursor.
        if (_snippetDragCandidate is not null)
        {
            if (sender is not Visual v || _snippetDragPressArgs is null)
            {
                _snippetDragCandidate = null;
                _snippetDragPressArgs = null;
                return;
            }
            var p = e.GetCurrentPoint(v);
            if (!p.Properties.IsLeftButtonPressed)
            {
                _snippetDragCandidate = null;
                _snippetDragPressArgs = null;
                return;
            }
            var sdx = p.Position.X - _snippetDragStart.X;
            var sdy = p.Position.Y - _snippetDragStart.Y;
            if (sdx * sdx + sdy * sdy < DragThreshold * DragThreshold) return;

            var obj = _snippetDragCandidate.Object!;
            var pressArgs = _snippetDragPressArgs;
            _snippetDragCandidate = null;
            _snippetDragPressArgs = null;

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(SqlSnippetDropTarget.DragFormat, obj));
            _ = DragDrop.DoDragDropAsync(pressArgs, data, DragDropEffects.Copy);
            return;
        }

        if (_dragSource is null) return;
        if (sender is not ListBox list) return;
        var point = e.GetCurrentPoint(list);
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
            e.Pointer.Capture(list);
            list.Cursor = new Avalonia.Input.Cursor(StandardCursorType.DragMove);
        }

        UpdateDropTarget(list, pos);
    }

    private void OnSidebarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not ListBox list) return;
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
            list.Cursor = Avalonia.Input.Cursor.Default;
            e.Pointer.Capture(null);
            ClearDragState();
        }
    }

    private void OnSidebarPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is ListBox list) list.Cursor = Avalonia.Input.Cursor.Default;
        ClearDragState();
    }

    private void UpdateDropTarget(ListBox list, Point pos)
    {
        var hover = FindRowVmAtPointer(list, pos);
        var (target, position) = ResolveDropTarget(_dragSource!, hover, list, pos);

        if (!ReferenceEquals(target, _currentDropTarget))
        {
            SetIsDropTarget(_currentDropTarget, false);
            SetIsDropTarget(target, true);
            _currentDropTarget = target;
        }
        _currentDropPosition = position;
    }

    private static (object? target, DropPosition position) ResolveDropTarget(
        object source, object? hover, ListBox list, Point pos)
    {
        // Dropped onto empty area / a non-droppable row → no target (release no-ops).
        if (hover is null) return (null, DropPosition.After);
        if (ReferenceEquals(source, hover)) return (null, DropPosition.After);

        if (source is ConnectionNodeViewModel)
        {
            if (hover is FolderNodeViewModel) return (hover, DropPosition.Into);
            if (hover is ConnectionNodeViewModel)
            {
                // Top half = Before, bottom half = After (relative to the row container).
                var pos2 = PositionFromVerticalSplit(list, hover, pos);
                return (hover, pos2);
            }
            return (null, DropPosition.After);
        }

        if (source is FolderNodeViewModel)
        {
            // Folders only live at root. Reorder relative to another folder or a root-level
            // connection. (ExecuteDrop rejects folder-into-folder-member contexts itself.)
            if (hover is FolderNodeViewModel or ConnectionNodeViewModel)
            {
                var pos2 = PositionFromVerticalSplit(list, hover, pos);
                return (hover, pos2);
            }
            return (null, DropPosition.After);
        }

        return (null, DropPosition.After);
    }

    private static DropPosition PositionFromVerticalSplit(ListBox list, object hoverVm, Point pointerPos)
    {
        // Find the ListBoxItem whose SidebarRow.Node == hoverVm; top half = Before, bottom = After.
        var item = FindListBoxItemFor(list, hoverVm);
        if (item is null) return DropPosition.After;
        var topLeft = item.TranslatePoint(new Point(0, 0), list);
        if (topLeft is null) return DropPosition.After;
        var midY = topLeft.Value.Y + item.Bounds.Height / 2.0;
        return pointerPos.Y < midY ? DropPosition.Before : DropPosition.After;
    }

    private static ListBoxItem? FindListBoxItemFor(Visual root, object nodeVm)
    {
        foreach (var d in root.GetVisualDescendants())
        {
            if (d is ListBoxItem lbi && lbi.DataContext is SidebarRow row && ReferenceEquals(row.Node, nodeVm))
            {
                return lbi;
            }
        }
        return null;
    }

    private static object? FindRowVmAtPointer(ListBox list, Point pos)
    {
        var hit = list.InputHitTest(pos);
        if (hit is not Visual v) return null;
        // Walk up to the ListBoxItem; its DataContext is the SidebarRow → the underlying node.
        var item = v.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        return (item?.DataContext as SidebarRow)?.Node;
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
        _snippetDragCandidate = null;
        _snippetDragPressArgs = null;
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
        if (sender is not Control c || c.DataContext is not MetadataNodeViewModel node) return;
        if (node.IsActionable && node.OpenDdlCommand.CanExecute(null))
        {
            node.OpenDdlCommand.Execute(null);
            e.Handled = true;
        }
        else if (node.IsGroup)
        {
            // Double-click a category toggles expansion (parity with the TreeView); the
            // flat controller reacts to the IsExpanded change and splices the rows.
            node.IsExpanded = !node.IsExpanded;
            e.Handled = true;
        }
    }

    private async System.Threading.Tasks.Task<bool> OnConfirmationRequested(ConfirmRequest request)
    {
        var dialog = new ConfirmDialog { DataContext = new ConfirmDialogViewModel(request) };
        return await dialog.ShowDialog<bool>(this);
    }

    private async System.Threading.Tasks.Task<string?> OnChoiceRequested(ChoiceRequest request)
    {
        var dialog = new ChoiceDialog { DataContext = new ChoiceDialogViewModel(request) };
        return await dialog.ShowDialog<string?>(this);
    }

    private async System.Threading.Tasks.Task OnBatchResultsRequested(BatchResultsViewModel vm)
    {
        var dialog = new BatchResultsDialog { DataContext = vm };
        // VM raises CopyRequested with TSV text; the dialog window owns the clipboard.
        async void OnCopy(string text)
        {
            var cb = TopLevel.GetTopLevel(dialog)?.Clipboard;
            if (cb is not null) await cb.SetTextAsync(text);
        }
        vm.CopyRequested += OnCopy;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            vm.CopyRequested -= OnCopy;
        }
    }

    private async System.Threading.Tasks.Task<UserEditResult?> OnUserEditRequested(EmberTern.Core.Security.UserInfo? existing)
    {
        var dialog = new UserEditDialog { DataContext = new UserEditDialogViewModel(existing) };
        return await dialog.ShowDialog<UserEditResult?>(this);
    }

    private async System.Threading.Tasks.Task<string?> OnNewRoleRequested()
    {
        var dialog = new NewRoleDialog { DataContext = new NewRoleDialogViewModel() };
        return await dialog.ShowDialog<string?>(this);
    }

    // Global Search dialog → the search query (or null on cancel). Prefills with the
    // SQL editor's current selection when there is one (convenience).
    private async System.Threading.Tasks.Task<EmberTern.Core.Search.MetadataSearchQuery?> OnGlobalSearchRequested()
    {
        var seed = GetSqlEditorSelection();
        if (seed is not null && seed.Contains('\n')) seed = null; // don't seed a multi-line selection
        var dialog = new GlobalSearchDialog { DataContext = new GlobalSearchDialogViewModel(seed) };
        return await dialog.ShowDialog<EmberTern.Core.Search.MetadataSearchQuery?>(this);
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
            // Persists + (when this is the active connection) refreshes the live profile
            // so the status bar + next transaction pick up the edited settings, then
            // rebuilds the tree.
            _currentVm.ApplyEditedProfile(updated);
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentResultVersionTag)
            || e.PropertyName == nameof(MainWindowViewModel.ResultPageVersionTag))
        {
            // CurrentResultVersionTag → structure may have changed (rebuild columns);
            // ResultPageVersionTag → paging/sort changed (re-slice rows + sort arrows).
            // PopulateResultGrid handles both: it keeps columns when the structure
            // matches and only re-binds ItemsSource + repaints the arrow header.
            PopulateResultGrid(_currentVm?.CurrentResult);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.SelectedWorkspaceTab)
              || e.PropertyName == nameof(MainWindowViewModel.ActiveDdlText)
              || e.PropertyName == nameof(MainWindowViewModel.QueryText))
        {
            if (_currentVm is null) return;

            // Results row collapses to 0 on non-Query tabs and restores its saved
            // height when the Query tab is active.
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedWorkspaceTab))
            {
                ApplyResultsRowForActiveTab();
            }

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

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_currentVm is not null && _editor is not null)
        {
            _currentVm.QueryText = _editor.Text;
        }
    }

    // Move keyboard focus into the SQL editor (New query → type immediately). Posted so it runs after
    // the new query's (empty) text has been pushed into the editor and layout has settled. Focus the
    // TextArea — the TextEditor delegates keyboard input there.
    private void OnEditorFocusRequested()
    {
        if (_editor is null) return;
        Dispatcher.UIThread.Post(() => _editor.TextArea.Focus(), DispatcherPriority.Background);
    }

    // (D3) EnsureColumnsAsync / EnsureRoutineParametersAsync / CreateMetadataSnapshot / metadata-generation
    // + warm callbacks used to be hand-passed here into a MainWindow-owned SqlCompletionController. The main
    // SQL editor now goes through the shared SqlEditorBehavior.Attach, which wires those from the VM's own
    // methods (vm.EnsureColumnsAsync / vm.EnsureRoutineParametersAsync / vm.CreateMetadataSnapshot /
    // vm.WarmReferencedAsync) — so these MainWindow forwarders are gone. See OnDataContextChanged.

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

        if (result is null || !result.HasResultSet)
        {
            _resultGrid.Columns.Clear();
            _resultGrid.ItemsSource = null;
            _resultColumnNames.Clear();
            return;
        }

        // Rebuild columns only when the structure (count + names) changes — so a
        // paging / sort re-bind keeps the existing columns (and their persisted
        // widths) and just re-slices the ItemsSource + repaints the sort arrow.
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
                _resultColumnNames.Add(result.Columns[i].Name);
                _resultGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = result.Columns[i].Name,
                    // Tag = data column index → the filter-from-cell resolver reads it
                    // (robust to any column reorder).
                    Tag = i,
                    Binding = new Binding($"[{i}]")
                    {
                        StringFormat = "{0}",
                        FallbackValue = string.Empty,
                        TargetNullValue = string.Empty,
                    },
                });
            }
        }

        UpdateResultHeaderArrows();
        _resultGrid.ItemsSource = _currentVm?.PagedResultRows;
    }

    // Paints a ▲/▼ glyph onto the sorted column's header (and strips it from the
    // others). We drive sort state ourselves (3-state), so Avalonia's built-in
    // arrow indicator isn't used — this is the visible sort cue.
    private void UpdateResultHeaderArrows()
    {
        if (_resultGrid is null || _currentVm is null) return;
        int sortCol = _currentVm.ResultSortColumnIndex;
        bool desc = _currentVm.ResultSortDescending;
        for (int i = 0; i < _resultGrid.Columns.Count && i < _resultColumnNames.Count; i++)
        {
            var name = _resultColumnNames[i];
            _resultGrid.Columns[i].Header = i == sortCol ? name + (desc ? "  ▼" : "  ▲") : name;
        }
    }

    private void OnResultHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_currentVm is null || _resultGrid is null) return;
        if (!e.GetCurrentPoint(_resultGrid).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Visual visual) return;
        var header = visual.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true);
        if (header is null) return;

        // DataGridColumnHeader.OwningColumn is internal (gotcha #43), so map the
        // header back to a column index by its (arrow-stripped) text against the
        // tracked column names. CanUserReorderColumns is false, so first match is
        // unambiguous for distinct names; duplicate names fall back to first match.
        var baseName = StripSortArrow(header.Content?.ToString());
        if (baseName is null) return;
        int index = -1;
        for (int i = 0; i < _resultColumnNames.Count; i++)
        {
            if (string.Equals(_resultColumnNames[i], baseName, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }
        if (index < 0) return;
        _currentVm.CycleResultSort(index);
    }

    private const string SortArrowAscending = "  ▲";
    private const string SortArrowDescending = "  ▼";

    private static string? StripSortArrow(string? header)
    {
        if (header is null) return null;
        if (header.EndsWith(SortArrowAscending, StringComparison.Ordinal))
            return header[..^SortArrowAscending.Length];
        if (header.EndsWith(SortArrowDescending, StringComparison.Ordinal))
            return header[..^SortArrowDescending.Length];
        return header;
    }

    // ── Resizable / collapsible layout (Part 2 + 3) ──────────────────────────

    private void ApplyLayoutFromPendingRestore()
    {
        if (_layoutRestored) return;
        _layoutRestored = true;

        var s = _pendingRestore;
        var width = s?.SidebarWidth ?? DefaultSidebarWidth;
        if (width < MinSidebarWidth) width = MinSidebarWidth;
        if (width > MaxSidebarWidth) width = MaxSidebarWidth;
        _expandedSidebarWidth = width;

        var height = s?.ResultsPanelHeight ?? DefaultResultsHeight;
        if (height < MinResultsHeight) height = MinResultsHeight;
        _resultsHeight = height;

        if (s?.SidebarCollapsed == true)
        {
            CollapseSidebar();
        }
        else
        {
            SetSidebarWidth(_expandedSidebarWidth);
        }

        // Restore the results-maximized flag. The actual row sizing is applied by
        // ApplyResultsRowForActiveTab when the Query tab becomes active; here we just
        // set the flag + sync the VM's display flag (drives the maximize/restore icon).
        _resultsMaximized = s?.ResultsMaximized == true;
        _currentVm?.SetResultsMaximized(_resultsMaximized);
    }

    private void SetSidebarWidth(double width)
    {
        if (_sidebarColumn is null) return;
        if (width < MinSidebarWidth) width = MinSidebarWidth;
        if (width > MaxSidebarWidth) width = MaxSidebarWidth;
        _sidebarColumn.Width = new GridLength(width);
    }

    private void CollapseSidebar()
    {
        if (_sidebarColumn is null) return;
        if (!_sidebarCollapsed && _sidebarColumn.Width.IsAbsolute && _sidebarColumn.Width.Value > 0)
        {
            _expandedSidebarWidth = _sidebarColumn.Width.Value;
        }
        _sidebarCollapsed = true;
        // Hard-clamp the column to exactly 0px. Width=0 alone isn't enough: the
        // column's MinWidth (220) / MaxWidth (600) still permit a non-zero width,
        // and the adjacent GridSplitter's layout pass re-reserves the prior pixel
        // width — leaving an empty ~280px gap. Forcing Min AND Max to 0 makes 0 the
        // only legal width, so the workspace column (*) takes all remaining space.
        _sidebarColumn.MinWidth = 0;
        _sidebarColumn.MaxWidth = 0;
        _sidebarColumn.Width = new GridLength(0);
        if (_sidebarPanel is not null) _sidebarPanel.IsVisible = false;
        if (_sidebarSplitter is not null) _sidebarSplitter.IsVisible = false;
        // Show the left-edge grab rail so the user can re-expand with one click.
        if (_sidebarRail is not null) _sidebarRail.IsVisible = true;
    }

    private void ExpandSidebar()
    {
        if (_sidebarColumn is null) return;
        _sidebarCollapsed = false;
        // Restore the resize constraints lifted in CollapseSidebar.
        _sidebarColumn.MaxWidth = MaxSidebarWidth;
        _sidebarColumn.MinWidth = MinSidebarWidth;
        if (_sidebarPanel is not null) _sidebarPanel.IsVisible = true;
        if (_sidebarSplitter is not null) _sidebarSplitter.IsVisible = true;
        if (_sidebarRail is not null) _sidebarRail.IsVisible = false;
        // Restore the last width used before collapsing (not the default).
        SetSidebarWidth(_expandedSidebarWidth);
    }

    private void OnToggleSidebarClick(object? sender, RoutedEventArgs e)
    {
        if (_sidebarCollapsed) ExpandSidebar();
        else CollapseSidebar();
    }

    // The collapsed-state grab rail. Fires on press (reliable on the 12px target,
    // unlike Click) and only ever expands — the rail is shown only while collapsed.
    private void OnSidebarRailPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Visual v && !e.GetCurrentPoint(v).Properties.IsLeftButtonPressed) return;
        if (_sidebarCollapsed) ExpandSidebar();
        e.Handled = true;
    }

    // Double-click the separator toggles full collapse (VS Code / DataGrip style):
    // visible → hide entirely; hidden → restore the last width. (When collapsed the
    // splitter isn't shown — the left-edge rail's click drives the restore — but the
    // toggle is kept symmetric here so either gesture works.)
    private void OnSidebarSplitterDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_sidebarCollapsed) ExpandSidebar();
        else CollapseSidebar();
        e.Handled = true;
    }

    // Sizes the results row for the active tab: saved height on the Query tab,
    // collapsed to 0 elsewhere. Captures the live (possibly dragged) height before
    // collapsing so it round-trips when the user returns to the Query tab.
    private void ApplyResultsRowForActiveTab()
    {
        if (_resultsRow is null || _currentVm is null) return;
        if (_currentVm.IsQueryTabActive)
        {
            if (_resultsMaximized)
            {
                ApplyResultsMaximized();
            }
            else
            {
                _resultsRow.MinHeight = MinResultsHeight;
                _resultsRow.Height = new GridLength(_resultsHeight);
                if (_editorRow is not null) _editorRow.Height = new GridLength(1, GridUnitType.Star);
            }
        }
        else
        {
            if (!_resultsMaximized && _resultsRow.Height.IsAbsolute && _resultsRow.Height.Value > 0)
            {
                _resultsHeight = _resultsRow.Height.Value;
            }
            _resultsRow.MinHeight = 0;
            _resultsRow.Height = new GridLength(0);
            // Keep the editor row a star so it fills when results are hidden (non-Query tab).
            if (_editorRow is not null) _editorRow.Height = new GridLength(1, GridUnitType.Star);
        }
    }

    // Maximized layout: editor row collapses, results row takes all the space.
    private void ApplyResultsMaximized()
    {
        if (_resultsRow is null) return;
        if (_editorRow is not null) _editorRow.Height = new GridLength(0);
        _resultsRow.MinHeight = MinResultsHeight;
        _resultsRow.Height = new GridLength(1, GridUnitType.Star);
    }

    // Tri-state toggle shared by the splitter double-click and the tab-strip button:
    //   Normal (editor + results) ⇄ Results maximized.
    // Only meaningful on the Query tab (the only place the results panel shows).
    // Restoring returns to the previous (possibly dragged) results height.
    private void ToggleResultsMaximized()
    {
        if (_currentVm?.IsQueryTabActive != true) return;

        if (!_resultsMaximized)
        {
            // Capture the live (dragged) height so Restore lands back on it.
            if (_resultsRow is { } row && row.Height.IsAbsolute && row.Height.Value > 0)
            {
                _resultsHeight = row.Height.Value;
            }
            _resultsMaximized = true;
        }
        else
        {
            _resultsMaximized = false;
        }
        ApplyResultsRowForActiveTab();
        _currentVm?.SetResultsMaximized(_resultsMaximized);
    }

    private void OnResultsSplitterDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleResultsMaximized();
        e.Handled = true;
    }

    private void OnToggleResultsMaximizeClick(object? sender, RoutedEventArgs e)
        => ToggleResultsMaximized();

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

    // Feeds the "Record N of M" indicator: the grid's SelectedIndex is the row's
    // position within the current page; the VM adds the page offset.
    private void OnResultGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => _currentVm?.SetResultSelectedRow(_resultGrid?.SelectedIndex ?? -1);

    // ── Filter-from-cell + copy-from-cell (SQL Results) ───────────────────────
    private GridCellFilterContext? _resultCellCtx;
    // The exact object?[] row the user right-clicked. Both the filter menu and the
    // copy menu resolve their target from the clicked cell, never from the grid's
    // view coordinates (SelectedIndex / CurrentColumn) — those index the sorted/
    // filtered/paged view, not CurrentResult.Rows, and CurrentColumn is null on a
    // fresh right-click. See ResolveResultRowIndex.
    private object?[]? _resultCellRow;

    private void OnResultCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        if (_resultGrid is null || _currentVm is null) return;
        if (!e.PointerPressedEventArgs.GetCurrentPoint(_resultGrid).Properties.IsRightButtonPressed) return;
        // Row selection is handled by OnResultGridPointerPressed; here we resolve the
        // clicked cell for the filter menu (Contains gating) AND for the copy menu.
        _resultCellRow = e.Row?.DataContext as object?[];
        _resultCellCtx = GridCellFilter.Resolve(_resultGrid, e, _currentVm.ResultFilterPanel.Columns);
        if (ResultFilterContainsItem is not null)
            ResultFilterContainsItem.IsEnabled = _resultCellCtx is { } ctx && GridCellFilter.SupportsContains(ctx);
    }

    // Translate the right-clicked row object into its index in CurrentResult.Rows.
    // PagedResultRows holds the SAME object?[] references (filter/sort/page only
    // reorder/slice), so a reference lookup is the correct view→data mapping.
    private int ResolveResultRowIndex(object?[]? rowObject)
    {
        var rows = _currentVm?.CurrentResult?.Rows;
        if (rowObject is null || rows is null) return -1;
        for (int i = 0; i < rows.Count; i++)
            if (ReferenceEquals(rows[i], rowObject)) return i;
        return -1;
    }

    private void OnResultFilterByValueClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null || _resultCellCtx is not { } ctx) return;
        var (col, op, val) = GridCellFilter.FilterByValue(ctx);
        _ = _currentVm.ResultFilterPanel.ApplyFromCellAsync(col, op, val);
    }

    private void OnResultExcludeValueClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null || _resultCellCtx is not { } ctx) return;
        var (col, op, val) = GridCellFilter.ExcludeValue(ctx);
        _ = _currentVm.ResultFilterPanel.ApplyFromCellAsync(col, op, val);
    }

    private void OnResultFilterContainsClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null || _resultCellCtx is not { } ctx) return;
        if (GridCellFilter.Contains(ctx) is not { } triple) return;
        _ = _currentVm.ResultFilterPanel.ApplyFromCellAsync(triple.ColumnIndex, triple.Op, triple.Value);
    }

    private void OnCopyCellClick(object? sender, RoutedEventArgs e)
        => InvokeCopy(CopyGridMode.Cell);

    private void OnCopyRowClick(object? sender, RoutedEventArgs e)
        => InvokeCopy(CopyGridMode.Row);

    private void OnCopyRowWithHeadersClick(object? sender, RoutedEventArgs e)
        => InvokeCopy(CopyGridMode.RowWithHeaders);

    private void OnCopyAllWithHeadersClick(object? sender, RoutedEventArgs e)
        => InvokeCopy(CopyGridMode.AllWithHeaders);

    // The context menu opening is what makes the provenance capture "lazy, on demand": a ~7 ms
    // SchemaOnly prepare here is imperceptible, whereas paying it on every F5 — to serve a menu the user
    // usually never opens — would be a silent, across-the-board regression of the editor and its
    // execution timer. Cached per result set, so only the first open of a given result pays.
    //
    // The menu shows immediately and its items settle via binding a few milliseconds later; the click
    // handler re-checks anyway, so the enabled state is a hint and the click is the authority.
    private async void OnResultContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_currentVm is null) return;
        await _currentVm.RefreshSqlCopyAvailabilityAsync();
    }

    private void OnCopyAsInsertClick(object? sender, RoutedEventArgs e)
        => InvokeCopyAsSql(ExportFormat.InsertScript);

    private void OnCopyAsUpdateClick(object? sender, RoutedEventArgs e)
        => InvokeCopyAsSql(ExportFormat.UpdateScript);

    private async void InvokeCopyAsSql(ExportFormat format)
    {
        if (_currentVm is null) return;
        // Pass the RIGHT-CLICKED row OBJECT, exactly like the Table Data grid does — not an index
        // re-derived from it (that reference lookup was an extra failure mode that silently dropped the
        // copy). Two independent handlers capture the clicked row on a right-click: OnResultCellPointerPressed
        // (the cell) and OnResultGridPointerPressed (which sets the grid's SelectedItem via an ancestor
        // walk). Prefer the cell capture, fall back to the selection, so a miss in one still yields the row.
        var row = _resultCellRow ?? _resultGrid?.SelectedItem as object?[];
        await _currentVm.CopyRowAsSqlAsync(format, row);
    }

    private async void InvokeCopy(CopyGridMode mode)
    {
        if (_currentVm is null) return;
        // Resolve the TARGET from the right-clicked cell (captured in
        // OnResultCellPointerPressed), not from the grid's view coordinates:
        //   row → data index in CurrentResult.Rows (robust to sort/filter/paging);
        //   column → the cell's boxed data index (robust to column reorder).
        var rowIndex = ResolveResultRowIndex(_resultCellRow);
        var colIndex = _resultCellCtx?.ColumnIndex ?? 0;
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
        var node = (this.FindControl<ListBox>("SidebarList")?.SelectedItem as SidebarRow)?.Node;
        if (node is FolderNodeViewModel f) return f.Id;
        if (node is ConnectionNodeViewModel c
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
