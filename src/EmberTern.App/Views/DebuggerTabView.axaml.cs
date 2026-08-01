using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.Completion;
using EmberTern.App.ViewModels;
#if DEBUG
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Avalonia.Layout;
#endif

namespace EmberTern.App.Views;

/// <summary>
/// The Firebird debugger tab view (Stage X / D4). A thin shell over <see cref="DebuggerTabViewModel"/>:
/// a launch panel + an editable source editor with the current-line marker (<see cref="CurrentLineRenderer"/>)
/// and breakpoint gutter (<see cref="BreakpointMargin"/>), plus a basic variables panel. The editor reuses
/// the D3 single wiring seam (<see cref="SqlEditorBehavior.Attach"/>) for intrinsic highlighting/hover — the
/// debugger renderers attach alongside it (spec §11.1). Keyboard is VS-standard and <b>tab-scoped</b>: F5 =
/// Continue here (it is Execute in the SQL editor — the one deliberate contradiction, spec §9.7).
/// </summary>
public partial class DebuggerTabView : UserControl
{
    private TextEditor? _editor;
    private BreakpointMargin? _margin;
    private DataGrid? _suspendGrid;
    private DebuggerTabViewModel? _vm;
    private bool _attached;
    private Popup? _peekPopup; // Peek Frame flyout, created lazily on first double-click of a call-stack row

    // Bottom tabbed panel (Immediate / Executed SQL / Watches) collapse — same mechanism as the SQL results
    // panel (MainWindow.ApplyResultsRowForActiveTab): ApplyBottomPanel is the ONE re-normalization point, and
    // it sets BOTH rows every time — top row to star (so the editor always reclaims space) and bottom row to
    // Auto (collapsed → only the tab strip shows) or the remembered pixel height (expanded). Presentation only.
    private RowDefinition? _topRow;
    private RowDefinition? _bottomRow;
    private double _bottomHeight = 220;
    private const double MinBottomHeight = 80;

    // True while SyncEditorText is writing the VM's text into the editor, so that write is not mistaken for
    // the user typing (Seam 5a — the editor's text now flows both ways).
    private bool _suppressEditorTextSync;

    public DebuggerTabView()
    {
        InitializeComponent();
        _editor = this.FindControl<TextEditor>("SourceEditor");
        _suspendGrid = this.FindControl<DataGrid>("SuspendGrid");
        var layout = this.FindControl<Grid>("DebugLayout");
        if (layout is not null && layout.RowDefinitions.Count > 2)
        {
            _topRow = layout.RowDefinitions[0];
            _bottomRow = layout.RowDefinitions[2];
        }
        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
        if (_editor is not null)
        {
            _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _editor.AddHandler(PointerPressedEvent, OnEditorPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            // Seam 5a — the editor is a normal editor now, so its text flows BOTH ways. This is the view→VM
            // half; SyncEditorText is the VM→view half and suppresses this handler while it writes, so the
            // two can never chase each other.
            _editor.TextChanged += OnSourceEditorTextChanged;
        }
        // D15.3 Seam C — Enter-to-launch on the launch panel (tunnelled so the last field's Enter launches
        // instead of inserting a newline). Scoped to the panel because that is where the only launchable focus
        // targets live. F5 is deliberately NOT handled here — it is the application-level "Go" shortcut routed by
        // the window (MainWindowViewModel.GoCommand), so the debugger participates in F5 routing rather than
        // grabbing the key with a focus-dependent local handler that only won while focus sat inside the tab.
        this.FindControl<ScrollViewer>("LaunchPanel")?
            .AddHandler(KeyDownEvent, OnLaunchKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
#if DEBUG
        // The Harness Log tab is a DEBUG-only diagnostic surface (Sprint D10.5) — it exists in development
        // builds only, so it is added here rather than in the XAML. In RELEASE this call is compiled out and
        // the tab does not exist at all.
        InsertHarnessLogTab();
#endif
    }

    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Intrinsic editor block via the one D3 seam (highlighting/hover/related elements over the routine
        // source), once, when the host VM is available. The debugger adds a data-tip source (spec §9.4) so a
        // plain hover over a variable shows its live frame value — read from the VM's roster at hover time,
        // never the server. Then the renderers.
        if (!_attached && _editor is not null
            && this.FindAncestorOfType<Window>()?.DataContext is MainWindowViewModel mainVm)
        {
            SqlEditorBehavior.Attach(_editor, mainVm, debugValueLookup: DebugValueFor);
            CurrentLineRenderer.Attach(_editor, () => (_vm?.CurrentStart, _vm?.CurrentLength));
            // D15.5 — inline values, painted on top of the current-line wash (appended after it). Reads the
            // VM's ready-made annotation set; repaints on the same DebugMarkersChanged → TextView.Redraw() path.
            InlineValuesRenderer.Attach(_editor,
                () => _vm?.InlineValues ?? (System.Collections.Generic.IReadOnlyList<InlineValueAnnotation>)Array.Empty<InlineValueAnnotation>());
            _margin = new BreakpointMargin(
                () => _vm?.BreakpointOffsets ?? Array.Empty<int>(),
                offset => _vm?.ToggleBreakpointAt(offset));
            _editor.TextArea.LeftMargins.Insert(0, _margin);

            _attached = true;
            SyncEditorText();
            SyncEditability();
        }

        // Land keyboard focus in the launch panel now the view is in the tree — independent of the Phase event
        // (which this view may have missed if preparation finished before it subscribed) and of where focus was
        // (opening from the sidebar leaves it on the tree, outside the tab). See FocusLaunchStart.
        FocusLaunchStart();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.DebugMarkersChanged -= OnDebugMarkersChanged;
            _vm.SuspendColumnsChanged -= OnSuspendColumnsChanged;
        }
        _vm = DataContext as DebuggerTabViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.DebugMarkersChanged += OnDebugMarkersChanged;
            _vm.SuspendColumnsChanged += OnSuspendColumnsChanged;
        }
        SyncEditorText();
        SyncEditability();
        RepaintMarkers();
        RebuildSuspendColumns();
        ApplyBottomPanel();
        // If the VM arrived already ReadyToLaunch (the view realized after preparation finished), the Phase
        // transition is in the past — establish launch focus here too. No-op unless attached + panel visible.
        FocusLaunchStart();
    }

    // Rebuilds the Results DataGrid's columns from the VM's SuspendColumns (D12 Seam E2) — dynamic columns, the
    // same pattern as the SQL editor's result grid (MainWindow.PopulateResultGrid): each column binds to the
    // row array by index ([i]). The rows themselves (SuspendRows) bind in XAML and update observably; only the
    // columns need code-behind. Fired on SuspendColumnsChanged (first SUSPEND row of a run, or a clear).
    private void OnSuspendColumnsChanged(object? sender, EventArgs e) => RebuildSuspendColumns();

    private void RebuildSuspendColumns()
    {
        if (_suspendGrid is null) return;
        _suspendGrid.Columns.Clear();
        if (_vm is null) return;
        for (int i = 0; i < _vm.SuspendColumns.Count; i++)
        {
            _suspendGrid.Columns.Add(new DataGridTextColumn
            {
                Header = _vm.SuspendColumns[i],
                Binding = new Binding($"[{i}]")
                {
                    StringFormat = "{0}",
                    FallbackValue = string.Empty,
                    TargetNullValue = UiStrings.DebuggerVariableNull,
                },
            });
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DebuggerTabViewModel.SourceText)) SyncEditorText();
        else if (e.PropertyName == nameof(DebuggerTabViewModel.IsSourceEditable)) SyncEditability();
        else if (e.PropertyName == nameof(DebuggerTabViewModel.IsBottomPanelCollapsed)) ApplyBottomPanel();
        else if (e.PropertyName == nameof(DebuggerTabViewModel.Phase)) OnPhaseChanged();
    }

    // Tracks the launch-panel⇄debug-view transition so focus lands where the keyboard should act next
    // (D15.3 Seam C): ready-to-launch → the first parameter field (or the Launch button); launch → the editor
    // TextArea, so F10/F11/… work immediately without a click. Starts true (Preparing is launch-visible).
    private bool _launchPanelWasVisible = true;

    private void OnPhaseChanged()
    {
        if (_vm is null) return;
        bool launchVisible = _vm.IsLaunchPanelVisible;

        // Launch → debug view: give the editor keyboard focus once (not on every step — both sides of a step
        // are debug-visible, so this only fires on the launch/relaunch transition, never stealing focus from
        // the Immediate box mid-session).
        if (!launchVisible && _launchPanelWasVisible) FocusEditor();

        // Ready to launch (panel shown): put the caret where the user starts typing / can press Enter.
        if (launchVisible && _vm.Phase == DebuggerPhase.ReadyToLaunch) FocusLaunchStart();

        _launchPanelWasVisible = launchVisible;
    }

    private void FocusEditor()
    {
        if (_editor is null) return;
        // TextArea holds keyboard focus (the TextEditor itself is not focusable — gotcha #225).
        Dispatcher.UIThread.Post(() => _editor.TextArea.Focus(), DispatcherPriority.Background);
    }

    // Puts keyboard focus into the launch panel on show, so the user can type parameters immediately and
    // Enter-to-launch from the last field works. Driven by EVERY event that can complete the "shown + ready"
    // state (view attached, DataContext set, Phase → ReadyToLaunch) — whichever happens LAST succeeds; relying
    // only on the Phase-change event was unreliable, because a freshly-realized view subscribes during
    // PrepareAsync's await and can miss a ReadyToLaunch reached first (a fast/cached source fetch). (F5 itself no
    // longer depends on this — it is the window-level Go router — so this is purely a typing/Enter convenience.)
    // No-op unless the launch panel is visible and the view is attached (an earlier trigger skips; a later lands).
    private void FocusLaunchStart() => Dispatcher.UIThread.Post(() =>
    {
        if (_vm?.IsLaunchPanelVisible != true) return;   // a fast-path auto-launch may have moved on already
        if (TopLevel.GetTopLevel(this) is null) return;  // not attached yet — a later trigger will retry
        var first = FirstFormInput();
        if (first is not null) first.Focus();
        else this.FindControl<Button>("LaunchButton")?.Focus();
    }, DispatcherPriority.Background);

    // Enter-to-launch (D15.3 Seam C). Enter launches ONLY from the Launch button or the last parameter field —
    // every other field keeps its natural Enter (a multiline text box gets a newline). F5 is NOT handled here:
    // it is the application-level "Go" shortcut, routed by the window (MainWindowViewModel.GoCommand →
    // DebuggerTabViewModel.RequestGoAsync), so the debugger no longer contests F5 with a local key handler.
    private void OnLaunchKeyDown(object? sender, KeyEventArgs e)
    {
        // The launch phase only; during the debug view Enter has no launch meaning.
        if (_vm is null || !_vm.IsLaunchPanelVisible) return;
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if ((TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()) is not Visual focused) return;
            var launchBtn = this.FindControl<Button>("LaunchButton");
            bool onButton = launchBtn is not null
                && (ReferenceEquals(focused, launchBtn) || focused.GetVisualAncestors().Contains(launchBtn));
            if (onButton || IsInLastFormInput(focused))
            {
                TryLaunch();
                e.Handled = true;
            }
        }
    }

    private void TryLaunch()
    {
        if (_vm?.LaunchCommand.CanExecute(null) == true) _vm.LaunchCommand.Execute(null);
    }

    // The focusable value controls of the visible parameter area, in visual order. A multiline TextBox is
    // excluded (its Enter is a newline). Trigger mode hides the proc-parameter list, so its controls are not
    // effectively visible and are skipped (the Launch button becomes the focus target instead).
    private System.Collections.Generic.List<Control> FormInputs()
    {
        var result = new System.Collections.Generic.List<Control>();
        if (this.FindControl<ItemsControl>("ParamsList") is not { } list) return result;
        foreach (var c in list.GetVisualDescendants().OfType<Control>())
        {
            if (c.IsEffectivelyVisible && IsFormInput(c)) result.Add(c);
        }
        return result;
    }

    private static bool IsFormInput(Control c) => c switch
    {
        TextBox tb => !tb.AcceptsReturn, // a multiline value box keeps Enter = newline
        NumericUpDown => true,
        CalendarDatePicker => true,
        CheckBox => true,
        _ => false,
    };

    private Control? FirstFormInput() => FormInputs().FirstOrDefault();

    private bool IsInLastFormInput(Visual focused)
    {
        var inputs = FormInputs();
        if (inputs.Count == 0) return false;
        var last = inputs[^1];
        return ReferenceEquals(focused, last) || focused.GetVisualAncestors().Contains(last);
    }

    // The ONE re-normalization point (mirrors MainWindow.ApplyResultsRowForActiveTab): it always sets BOTH
    // rows, so a GridSplitter drag that converted the top row to an absolute pixel height (Avalonia's Split
    // behaviour on a star+absolute pair) never survives a toggle — the top row is re-established as star, so
    // the editor always reclaims the space and the panel can never "glue" to it. Collapsed → the bottom row
    // goes Auto (the tab contents bind IsVisible to !IsBottomPanelCollapsed, so an Auto row measures to the
    // strip only); expanded → the remembered (possibly dragged) pixel height.
    private void ApplyBottomPanel()
    {
        if (_bottomRow is null) return;
        bool collapsed = _vm?.IsBottomPanelCollapsed ?? false;
        if (_topRow is not null) _topRow.Height = new GridLength(1, GridUnitType.Star);
        if (collapsed)
        {
            if (_bottomRow.Height.IsAbsolute && _bottomRow.Height.Value > 0)
            {
                _bottomHeight = _bottomRow.Height.Value; // remember the (possibly dragged) height for restore
            }
            _bottomRow.MinHeight = 0;
            _bottomRow.Height = GridLength.Auto;
        }
        else
        {
            _bottomRow.MinHeight = MinBottomHeight;
            _bottomRow.Height = new GridLength(_bottomHeight);
        }
    }

    // Double-click the bottom panel's header bar (the tab strip) to toggle collapse — a second, more natural
    // affordance beside the chevron button. Reuses the SAME logic (ToggleBottomPanelCommand); no separate
    // mechanism. When expanded, only a double-tap on a tab header toggles, so double-clicking the panel's
    // content (rows, inputs) is left alone; when collapsed only the strip is visible, so any double-tap on the
    // bar expands it. The chevron button owns its own click, so a double-tap that lands on it is ignored here.
    private void OnBottomPanelDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null) return;
        var source = e.Source as Visual;
        if (source?.FindAncestorOfType<Button>() is not null) return;
        bool onStrip = _vm.IsBottomPanelCollapsed || source?.FindAncestorOfType<TabItem>() is not null;
        if (!onStrip) return;
        Invoke(_vm.ToggleBottomPanelCommand);
        e.Handled = true;
    }

    // Double-click the resize bar toggles collapse — structurally identical to MainWindow's
    // OnResultsSplitterDoubleTapped (synchronous flag toggle → ApplyBottomPanel sets both rows). The splitter
    // is always visible (its visibility no longer depends on the state it toggles), so it never hides itself
    // mid-gesture; the double-click therefore toggles reliably in both directions and needs no deferral.
    private void OnBottomSplitterDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null) return;
        Invoke(_vm.ToggleBottomPanelCommand);
        e.Handled = true;
    }

    // Click a Variables group header to expand/collapse it. A view concern (presentation), so it flips the
    // group VM's IsExpanded directly rather than routing a command — theme-safe (no ToggleButton fighting
    // FluentTheme's accent state colours).
    private void OnVariableGroupHeaderTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is DebugVariableGroupViewModel group)
        {
            group.IsExpanded = !group.IsExpanded;
            e.Handled = true;
        }
    }

    // Data-tip source for the unified hover (spec §9.4): the paused frame's value for a variable/parameter,
    // read from the VM's roster (the same rows the Variables panel shows — one truth). Null when not paused,
    // so a hover outside a live session shows no data tip. Pure lookup; never touches the server.
    private EmberTern.Core.Sql.Language.Hover.DebugHoverValue? DebugValueFor(string name)
    {
        var vm = _vm;
        if (vm is null || !vm.IsPaused) return null;
        foreach (var row in vm.Variables)
        {
            if (string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
                return new EmberTern.Core.Sql.Language.Hover.DebugHoverValue(row.Name, row.ValueText, row.IsNull);
        }
        return null;
    }

    // Double-click a variable's value → inline edit (spec §9.4). Begins the edit on the row, then focuses +
    // selects the swapped-in text box so the user can type immediately. Guarded to the paused state by the
    // command; a view concern, so it reaches the row via its DataContext.
    private void OnVariableValueDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || (sender as Control)?.DataContext is not DebugVariableRowViewModel row) return;
        if (!_vm.BeginEditCommand.CanExecute(row)) return;
        _vm.BeginEditCommand.Execute(row);
        if (sender is Panel panel)
        {
            var box = panel.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (box is not null)
                Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Background);
        }
        e.Handled = true;
    }

    // "Break when changes" (D12, §9.8.4) from a variable row's right-click menu. Routed here because an
    // ElementName binding cannot reach the VM across a ContextMenu's popup namescope; the MenuItem inherits the
    // row as its DataContext. Pure routing to the VM command — the data-breakpoint change detection is in Core.
    private void OnBreakWhenChangesClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || (sender as Control)?.DataContext is not DebugVariableRowViewModel row) return;
        if (_vm.AddDataBreakpointCommand.CanExecute(row)) _vm.AddDataBreakpointCommand.Execute(row);
        e.Handled = true;
    }

    private void OnDebugMarkersChanged(object? sender, EventArgs e) => RepaintMarkers();

    // VM → view. Guarded so the write does not come back through OnSourceEditorTextChanged as if the user had
    // typed it (which would, among other things, mark a clean tab dirty on every step).
    private void SyncEditorText()
    {
        if (_editor is null || _vm is null) return;
        var text = _vm.SourceText ?? string.Empty;
        if (_editor.Text == text) return;

        _suppressEditorTextSync = true;
        try
        {
            _editor.Text = text;
        }
        finally
        {
            _suppressEditorTextSync = false;
        }
    }

    // View → VM. Only real typing reaches the VM's edit buffer; the VM itself rejects an edit while a
    // callee frame is displayed, so a stale event can never write another routine's text into the buffer.
    private void OnSourceEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressEditorTextSync || _editor is null || _vm is null) return;
        _vm.ApplySourceEdit(_editor.Text ?? string.Empty);
    }

    // Read-only means "this frame's source is not ours to save" (a callee/caller frame), never "a session is
    // running" — Seam 5a's ratified rule.
    private void SyncEditability()
    {
        if (_editor is null || _vm is null) return;
        _editor.IsReadOnly = !_vm.IsSourceEditable;
    }

    // Repaint the current-line renderer + breakpoint gutter. TextView.Redraw() (never InvalidateVisual) —
    // gotcha #223: InvalidateVisual can run before the visual lines exist and a diff-guard makes the miss
    // permanent. Scrolling the paused statement into view is a convenience, not a correctness requirement.
    private void RepaintMarkers()
    {
        if (_editor is null) return;
        _editor.TextArea.TextView.Redraw();
        _margin?.Refresh();

        if (_vm?.CurrentStart is { } start && start >= 0 && start <= _editor.Document.TextLength)
        {
            var loc = _editor.Document.GetLocation(start);
            _editor.ScrollTo(loc.Line, loc.Column);
        }
    }

    // Tab-scoped VS-standard debugger keys (spec §9.7). Tunnelled so the read-only editor never swallows
    // them first. Every gesture here is declared in Commands.CommandCatalog as CommandDispatch.Reserved:
    // the catalog knows about them (so no global gesture can quietly steal one, and menus/tooltips can show
    // them) while dispatch stays here, because several are VIEW actions needing the source editor's caret —
    // Run To Cursor and Toggle Breakpoint have no view-model command to route to.
    //
    // ⚠ F5 is deliberately ABSENT: it is CommandId.Go, resolved by the router to DebuggerTabViewModel's
    // GoCommand. It used to be handled here AND by the window's F5 binding (which routed to the debugger
    // too) — two owners for one key, where this one won only while focus sat inside the debugger tab. F5
    // still means Continue in the debugger and Execute in the SQL editor: the one ratified contradiction.
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        switch (e.Key)
        {
            case Key.F5 when ctrl && shift: Invoke(_vm.RestartCommand); break;
            case Key.F5 when shift: Invoke(_vm.StopCommand); break;
            case Key.F10 when ctrl: RunToCursor(); break;
            case Key.F10: Invoke(_vm.StepOverCommand); break;
            case Key.F11 when shift: Invoke(_vm.StepOutCommand); break;
            case Key.F11: Invoke(_vm.StepIntoCommand); break;
            case Key.F9 when shift: EvaluateSelection(); break;
            case Key.F9: ToggleBreakpointAtCaret(); break;
            // Seam 5b — Ctrl+S saves + compiles, the same key it is in every object editor. Handled even
            // when there is nothing to save, so it never falls through to some other surface's Save.
            case Key.S when ctrl: Invoke(_vm.SaveSourceCommand); break;
            // Ctrl+Alt+Up/Down move the frame selection up/down the call stack (VS/Rider-standard, spec §5.2).
            case Key.Up when ctrl && alt: _vm.MoveFrameSelection(-1); break;
            case Key.Down when ctrl && alt: _vm.MoveFrameSelection(+1); break;
            default: return;
        }
        e.Handled = true;
    }

    // Peek Frame (spec §5): double-click a call-stack row to preview that frame's source inline — without
    // changing which frame the editor is pinned to (single-click already navigates). A transient, light-
    // dismissed card (the same visual pattern as NavigationController's Peek Definition, kept debugger-local
    // because that peek is private to the editor navigation controller).
    private void OnCallStackDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null) return;
        var row = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>()?.DataContext as DebugFrameRowViewModel;
        if (row is null) return;
        var peek = _vm.GetFramePeek(row.FrameId);
        if (peek is null) return;
        ShowFramePeek(peek);
        e.Handled = true;
    }

    private void ShowFramePeek(DebugFramePeek peek)
    {
        EnsurePeekPopup();
        string header = peek.CurrentLine > 0
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture,
                UiStrings.DebuggerCallStackPeekHeaderFormat, peek.RoutineName, peek.CurrentLine)
            : peek.RoutineName;
        _peekPopup!.Child = BuildPeekCard(header, peek.Source);
        _peekPopup.IsOpen = false;
        _peekPopup.IsOpen = true;
    }

    private void EnsurePeekPopup()
    {
        if (_peekPopup is not null) return;
        _peekPopup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Center,
            IsLightDismissEnabled = true,
        };
        ((ISetLogicalParent)_peekPopup).SetParent(this);
    }

    private Control BuildPeekCard(string header, string source)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = header,
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
            Foreground = Brush("ForegroundBrush"),
        });
        var body = new TextBox
        {
            Text = source,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Code,Consolas,Menlo,monospace"),
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Brush("ForegroundBrush"),
        };
        var scroll = new ScrollViewer
        {
            Content = body,
            MaxHeight = 320,
            MaxWidth = 640,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        stack.Children.Add(scroll);
        var border = new Border
        {
            Child = stack,
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            Background = Brush("SurfaceRaisedBrush"),
            BorderBrush = Brush("BorderBrush"),
        };
        border.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { ClosePeek(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        return border;
    }

    // Resolve a theme brush against the current variant (mirrors ApplyEditorTheme) — the peek card is
    // transient, so a snapshot brush is fine (no live theme-toggle re-bind needed).
    private IBrush? Brush(string key)
    {
        var theme = ActualThemeVariant;
        return Application.Current?.Resources.TryGetResource(key, theme, out var res) == true && res is IBrush b ? b : null;
    }

    private void ClosePeek()
    {
        if (_peekPopup is { IsOpen: true } p) p.IsOpen = false;
    }

    // Evaluate (Shift+F9, spec §9.7): evaluate the current selection — or, if there is none, the identifier
    // under the caret — as an expression against the current frame. The result lands in the Executed SQL log.
    // This is a presentation convenience; it extracts a text fragment only, never any SQL semantics (the
    // engine is DebugSession.Evaluate).
    private void EvaluateSelection()
    {
        if (_editor is null || _vm is null) return;
        var fragment = _editor.SelectedText;
        if (string.IsNullOrWhiteSpace(fragment)) fragment = IdentifierAtCaret();
        if (!string.IsNullOrWhiteSpace(fragment)) _ = _vm.EvaluateSelectionAsync(fragment);
    }

    // The identifier/qualified name straddling the caret (letters/digits/_/$/.), or empty. A best-effort
    // fallback so Shift+F9 without a selection still evaluates the symbol the caret is on.
    private string IdentifierAtCaret()
    {
        if (_editor?.Document is not { } doc) return string.Empty;
        var text = doc.Text;
        int caret = _editor.CaretOffset;
        static bool IsPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' or '.';
        int start = caret, end = caret;
        while (start > 0 && IsPart(text[start - 1])) start--;
        while (end < text.Length && IsPart(text[end])) end++;
        return end > start ? text.Substring(start, end - start) : string.Empty;
    }

    private static void Invoke(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    private void ToggleBreakpointAtCaret()
    {
        if (_editor is null || _vm is null) return;
        _vm.ToggleBreakpointAt(_editor.CaretOffset);
    }

    private void RunToCursor()
    {
        if (_editor is null || _vm is null) return;
        _ = _vm.RunToCursorAsync(_editor.CaretOffset);
    }

    // Toolbar button + editor context-menu entry for Run To Cursor — both route to the SAME RunToCursor()
    // (which reads the editor caret), so no debugger logic is duplicated; only new discoverable UI.
    private void OnRunToCursorClick(object? sender, RoutedEventArgs e) => RunToCursor();

    // Right-click moves the caret to the clicked position (like VS) so the context-menu "Run to Cursor"
    // targets the line under the pointer, not a stale caret. Reuses the point→offset pattern from
    // NavigationController; caret is settable on a read-only editor.
    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editor?.Document is null) return;
        if (!e.GetCurrentPoint(_editor).Properties.IsRightButtonPressed) return;
        if (_editor.GetPositionFromPoint(e.GetPosition(_editor)) is { } tvp)
            _editor.CaretOffset = _editor.Document.GetOffset(tvp.Location);
    }

#if DEBUG
    // ── Harness Log tab (DEBUG-only diagnostic surface, Sprint D10.5) ───────────────────────────────────
    //
    // Shows the EXECUTE BLOCK harnesses the debugger generates internally to evaluate expressions/statements
    // on the server (§10.3/§F) — a developer-diagnostic view of how the debugger works, NOT a production
    // feature and NOT the user's SQL history. It is deliberately built in code-behind under #if DEBUG (never
    // in DebuggerTabView.axaml), so in RELEASE builds none of this UI is compiled and the tab does not exist.
    // The audit log it renders (DebuggerTabViewModel.ExecutedSql) is still collected in every build — it also
    // feeds the Immediate tab's inline result — so only the *exposing* UI is DEBUG-scoped, not the mechanism.

    private void InsertHarnessLogTab()
    {
        if (this.FindControl<TabControl>("BottomTabs") is not { } tabs) return;
        // Keep the historical position (right after Immediate); clamp defensively.
        tabs.Items.Insert(Math.Min(1, tabs.Items.Count), BuildHarnessLogTab());
    }

    private TabItem BuildHarnessLogTab()
    {
        var header = new TextBlock { Text = UiStrings.DebuggerBottomTabHarnessLog };
        ToolTip.SetTip(header, UiStrings.DebuggerHarnessLogDescription);

        var tab = new TabItem { Header = header };
        tab.Classes.Add("bottom-tab");

        var content = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        content.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        content.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        // Collapse behaviour: mirror the other tabs — the content hides when the bottom panel is collapsed so
        // the Auto row measures to the tab strip only (see ApplyBottomPanel).
        content.Bind(Visual.IsVisibleProperty, new Binding("IsBottomPanelCollapsed") { Converter = BoolConverters.Not });

        // Always-visible purpose line, so the tab explains itself the moment it is opened (Task 3).
        var description = Subtle(UiStrings.DebuggerHarnessLogDescription, new Thickness(10, 6, 10, 4));
        Grid.SetRow(description, 0);
        content.Children.Add(description);

        // Empty-state hint (no harnesses generated yet in this session).
        var empty = Subtle(UiStrings.DebuggerHarnessLogEmpty, new Thickness(10, 2, 10, 6));
        empty.Bind(Visual.IsVisibleProperty, new Binding("HasExecutedSql") { Converter = BoolConverters.Not });
        Grid.SetRow(empty, 1);
        content.Children.Add(empty);

        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<DebugExecutedSqlRowViewModel>((_, _) => BuildHarnessRow(), supportsRecycling: true),
        };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding("ExecutedSql"));
        list.Bind(Visual.IsVisibleProperty, new Binding("HasExecutedSql"));
        Grid.SetRow(list, 1);
        content.Children.Add(list);

        tab.Content = content;
        return tab;
    }

    // One Harness Log row (mirrors the former XAML template): [time | fragment | ± side-effect] then the
    // result / error text, with the generated harness SQL on the row tooltip (the §10.3/§F audit anchor).
    private Control BuildHarnessRow()
    {
        var row = new StackPanel { Spacing = 1, Margin = new Thickness(0, 2, 0, 2) };
        row.Bind(ToolTip.TipProperty, new Binding("Sql"));

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var time = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        time.Bind(TextBlock.TextProperty, new Binding("TimestampText"));
        BindBrush(time, TextBlock.ForegroundProperty, "SubtleForegroundBrush");
        Grid.SetColumn(time, 0);
        head.Children.Add(time);

        var fragment = Mono("Fragment");
        fragment.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(fragment, 1);
        head.Children.Add(fragment);

        var glyph = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        glyph.Bind(TextBlock.TextProperty, new Binding("SideEffectGlyph"));
        BindBrush(glyph, TextBlock.ForegroundProperty, "WarningBrush");
        Grid.SetColumn(glyph, 2);
        head.Children.Add(glyph);

        row.Children.Add(head);

        // Error vs. normal result — two TextBlocks toggled by IsError (matches the former XAML), each themed.
        var errorText = Mono("ResultText");
        errorText.TextWrapping = TextWrapping.Wrap;
        errorText.Bind(Visual.IsVisibleProperty, new Binding("IsError"));
        BindBrush(errorText, TextBlock.ForegroundProperty, "ErrorBrush");
        row.Children.Add(errorText);

        var okText = Mono("ResultText");
        okText.TextWrapping = TextWrapping.Wrap;
        okText.Bind(Visual.IsVisibleProperty, new Binding("IsError") { Converter = BoolConverters.Not });
        BindBrush(okText, TextBlock.ForegroundProperty, "ForegroundBrush");
        row.Children.Add(okText);

        return row;
    }

    // A subtle, wrapping caption (matches the "subtle" style used for the other panels' hints/descriptions).
    private TextBlock Subtle(string text, Thickness margin)
    {
        var tb = new TextBlock { Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = margin };
        BindBrush(tb, TextBlock.ForegroundProperty, "SubtleForegroundBrush");
        return tb;
    }

    private static TextBlock Mono(string path)
    {
        var tb = new TextBlock
        {
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code,Consolas,Menlo,monospace"),
        };
        tb.Bind(TextBlock.TextProperty, new Binding(path));
        return tb;
    }

    // Consume a theme brush as a live DynamicResource (project rule: brushes via DynamicResource, never a
    // snapshot) so the harness rows recolour on a theme toggle.
    private void BindBrush(Control control, AvaloniaProperty property, string key)
        => control.Bind(property, this.GetResourceObservable(key));
#endif

    private void ApplyEditorTheme()
    {
        if (_editor is null) return;
        var theme = ActualThemeVariant;
        var name = theme == ThemeVariant.Light ? App.FirebirdSyntaxLightName : App.FirebirdSyntaxName;
        _editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(name);
        if (Application.Current?.Resources.TryGetResource("SelectionBrush", theme, out var res) == true
            && res is IBrush brush)
        {
            _editor.TextArea.SelectionBrush = brush;
        }
    }
}
