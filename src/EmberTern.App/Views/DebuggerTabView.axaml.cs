using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
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

    // Bottom tabbed panel (Immediate / Executed SQL / Watches) collapse — mirrors the SQL results panel's
    // row-height toggle (MainWindow). Collapsed → the row goes Auto (only the tab strip shows), the editor +
    // Variables reclaim the height; expanded → the remembered (draggable) pixel height. Presentation only.
    private RowDefinition? _bottomRow;
    private double _bottomHeight = 220;
    private const double MinBottomHeight = 80;

    public DebuggerTabView()
    {
        InitializeComponent();
        _editor = this.FindControl<TextEditor>("SourceEditor");
        var layout = this.FindControl<Grid>("DebugLayout");
        if (layout is not null && layout.RowDefinitions.Count > 2) _bottomRow = layout.RowDefinitions[2];
        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
        if (_editor is not null)
        {
            _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }
    }

    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_attached || _editor is null) return;
        if (this.FindAncestorOfType<Window>()?.DataContext is not MainWindowViewModel mainVm) return;

        // Intrinsic editor block via the one D3 seam (highlighting/hover/related elements over the read-only
        // source). Then the debugger renderers, per the D3 architecture (spec §11.1).
        SqlEditorBehavior.Attach(_editor, mainVm);
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

    // Toggles the bottom-panel row between its remembered pixel height (expanded) and Auto (collapsed → just
    // the tab strip). The tab contents bind IsVisible to !IsBottomPanelCollapsed, so an Auto row measures to
    // the strip only. Mirrors MainWindow's ApplyResultsRowForActiveTab.
    private void ApplyBottomPanel()
    {
        if (_bottomRow is null) return;
        bool collapsed = _vm?.IsBottomPanelCollapsed ?? false;
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
