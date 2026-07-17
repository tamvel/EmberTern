using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace EmberTern.App.Completion;

/// <summary>
/// Stage X / D4 — the debugger's breakpoint gutter (spec §9.6). A left <see cref="AbstractMargin"/> that
/// draws a red dot on every document line carrying a breakpoint and toggles a breakpoint when clicked.
/// Breakpoints snap to a real step unit (an <c>IExecutableStatement</c>) — the margin only reports the
/// clicked line's start offset to <see cref="_toggle"/>; the VM maps it to the nearest step point. A pure
/// view component: it reads the breakpoint offsets from a provider and owns no debug state. Colour is the
/// theme token <c>DebugBreakpointBrush</c> (both dictionaries), so it follows light/dark with no hardcoded
/// colours; repaint follows the text view's <c>VisualLinesChanged</c> (never a diff-guarded
/// <c>InvalidateVisual</c> on stale lines — gotcha #223).
/// </summary>
internal sealed class BreakpointMargin : AbstractMargin
{
    private const double MarginWidth = 18;
    private const double DotSize = 11;

    private readonly Func<IReadOnlyCollection<int>> _breakpoints;
    private readonly Action<int> _toggle;
    private EventHandler? _visualLinesChanged;

    public BreakpointMargin(Func<IReadOnlyCollection<int>> breakpoints, Action<int> toggle)
    {
        _breakpoints = breakpoints;
        _toggle = toggle;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null && _visualLinesChanged is not null)
        {
            oldTextView.VisualLinesChanged -= _visualLinesChanged;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
        {
            _visualLinesChanged ??= (_, _) => InvalidateVisual();
            newTextView.VisualLinesChanged += _visualLinesChanged;
        }
        InvalidateVisual();
    }

    /// <summary>Re-paints the gutter (called by the view when the breakpoint set changes).</summary>
    public void Refresh() => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var textView = TextView;
        if (textView is null || !textView.VisualLinesValid) return;

        var bps = _breakpoints();
        if (bps.Count == 0) return;

        var brush = ResolveBrush("DebugBreakpointBrush");
        if (brush is null) return;

        double scrollY = textView.VerticalOffset;
        foreach (var line in textView.VisualLines)
        {
            int lineStart = line.FirstDocumentLine.Offset;
            int lineEnd = line.LastDocumentLine.EndOffset;
            if (!bps.Any(o => o >= lineStart && o <= lineEnd)) continue;

            double top = line.VisualTop - scrollY;
            var center = new Point(MarginWidth / 2, top + line.Height / 2);
            context.DrawEllipse(brush, null, center, DotSize / 2, DotSize / 2);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var textView = TextView;
        if (textView is null || !textView.VisualLinesValid) return;

        double y = e.GetPosition(this).Y + textView.VerticalOffset;
        foreach (var line in textView.VisualLines)
        {
            if (y >= line.VisualTop && y < line.VisualTop + line.Height)
            {
                _toggle(line.FirstDocumentLine.Offset);
                e.Handled = true;
                InvalidateVisual();
                return;
            }
        }
    }

    private IBrush? ResolveBrush(string key)
    {
        var theme = ActualThemeVariant;
        if (Application.Current?.Resources.TryGetResource(key, theme, out var v) == true && v is IBrush b)
            return b;
        return null;
    }
}
