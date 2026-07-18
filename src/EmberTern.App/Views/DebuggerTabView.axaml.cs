using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.Completion;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>
/// The Firebird debugger tab view (Stage X / D4). A thin shell over <see cref="DebuggerTabViewModel"/>:
/// a launch panel + a read-only source editor with the current-line marker (<see cref="CurrentLineRenderer"/>)
/// and breakpoint gutter (<see cref="BreakpointMargin"/>), plus a basic variables panel. The editor reuses
/// the D3 single wiring seam (<see cref="SqlEditorBehavior.Attach"/>) for intrinsic highlighting/hover — the
/// debugger renderers attach alongside it (spec §11.1). Keyboard is VS-standard and <b>tab-scoped</b>: F5 =
/// Continue here (it is Execute in the SQL editor — the one deliberate contradiction, spec §9.7).
/// </summary>
public partial class DebuggerTabView : UserControl
{
    private TextEditor? _editor;
    private BreakpointMargin? _margin;
    private DebuggerTabViewModel? _vm;
    private bool _attached;

    // Bottom tabbed panel (Immediate / Executed SQL / Watches) collapse — same mechanism as the SQL results
    // panel (MainWindow.ApplyResultsRowForActiveTab): ApplyBottomPanel is the ONE re-normalization point, and
    // it sets BOTH rows every time — top row to star (so the editor always reclaims space) and bottom row to
    // Auto (collapsed → only the tab strip shows) or the remembered pixel height (expanded). Presentation only.
    private RowDefinition? _topRow;
    private RowDefinition? _bottomRow;
    private double _bottomHeight = 220;
    private const double MinBottomHeight = 80;

    public DebuggerTabView()
    {
        InitializeComponent();
        _editor = this.FindControl<TextEditor>("SourceEditor");
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
        }
    }

    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_attached || _editor is null) return;
        if (this.FindAncestorOfType<Window>()?.DataContext is not MainWindowViewModel mainVm) return;

        // Intrinsic editor block via the one D3 seam (highlighting/hover/related elements over the read-only
        // source). The debugger adds a data-tip source (spec §9.4) so a plain hover over a variable shows its
        // live frame value — read from the VM's roster at hover time, never the server. Then the renderers.
        SqlEditorBehavior.Attach(_editor, mainVm, debugValueLookup: DebugValueFor);
        CurrentLineRenderer.Attach(_editor, () => (_vm?.CurrentStart, _vm?.CurrentLength));
        _margin = new BreakpointMargin(
            () => _vm?.BreakpointOffsets ?? Array.Empty<int>(),
            offset => _vm?.ToggleBreakpointAt(offset));
        _editor.TextArea.LeftMargins.Insert(0, _margin);

        _attached = true;
        SyncEditorText();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.DebugMarkersChanged -= OnDebugMarkersChanged;
        }
        _vm = DataContext as DebuggerTabViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.DebugMarkersChanged += OnDebugMarkersChanged;
        }
        SyncEditorText();
        RepaintMarkers();
        ApplyBottomPanel();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DebuggerTabViewModel.SourceText)) SyncEditorText();
        else if (e.PropertyName == nameof(DebuggerTabViewModel.IsBottomPanelCollapsed)) ApplyBottomPanel();
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

    private void OnDebugMarkersChanged(object? sender, EventArgs e) => RepaintMarkers();

    private void SyncEditorText()
    {
        if (_editor is null || _vm is null) return;
        var text = _vm.SourceText ?? string.Empty;
        if (_editor.Text != text) _editor.Text = text;
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
    // them first. F5 = Continue here (Execute in the SQL editor — the one deliberate contradiction).
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.F5 when ctrl && shift: Invoke(_vm.RestartCommand); break;
            case Key.F5 when shift: Invoke(_vm.StopCommand); break;
            case Key.F5: Invoke(_vm.ContinueCommand); break;
            case Key.F10 when ctrl: RunToCursor(); break;
            case Key.F10: Invoke(_vm.StepOverCommand); break;
            case Key.F11 when shift: Invoke(_vm.StepOutCommand); break;
            case Key.F11: Invoke(_vm.StepIntoCommand); break;
            case Key.F9 when shift: EvaluateSelection(); break;
            case Key.F9: ToggleBreakpointAtCaret(); break;
            default: return;
        }
        e.Handled = true;
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
