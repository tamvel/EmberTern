using System;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace EmberTern.App.Completion;

/// <summary>
/// Stage X / D4 — paints the debugger's <b>current-statement</b> marker: a translucent band over the step
/// point the session is paused on (spec §9.6). A pure <b>client</b> of the debug tab's read-only state —
/// it reads a span (start + length) from a provider and does no analysis. Mirrors
/// <see cref="SquiggleRenderer"/> / <see cref="RelatedElementsRenderer"/>: an <see cref="IBackgroundRenderer"/>
/// whose colour is a theme token (<c>DebugCurrentLineBrush</c>), so it follows light/dark with no hardcoded
/// colours. Repaint is driven by the view via <c>TextView.Redraw()</c> (never <c>InvalidateVisual()</c> —
/// gotcha #223: it can run before visual lines exist and a diff-guard makes the miss permanent).
/// </summary>
internal sealed class CurrentLineRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly Func<(int? Start, int? Length)> _current;

    private CurrentLineRenderer(TextEditor editor, Func<(int? Start, int? Length)> current)
    {
        _editor = editor;
        _current = current;
    }

    public static CurrentLineRenderer Attach(TextEditor editor, Func<(int? Start, int? Length)> current)
    {
        var renderer = new CurrentLineRenderer(editor, current);
        editor.TextArea.TextView.BackgroundRenderers.Add(renderer);
        return renderer;
    }

    // Under the text (below the selection), like the squiggle/related renderers — the band never masks glyphs.
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var (start, length) = _current();
        if (start is null || length is null || textView.VisualLines.Count == 0) return;

        var doc = _editor.Document;
        if (doc is null) return;

        int s = Math.Max(0, start.Value);
        int e = Math.Min(doc.TextLength, start.Value + Math.Max(0, length.Value));
        if (s >= e) return;

        var brush = ResolveBrush("DebugCurrentLineBrush");
        if (brush is null) return;

        var segment = new TextSegment { StartOffset = s, Length = e - s };
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
        {
            drawingContext.FillRectangle(brush, rect);
        }
    }

    private IBrush? ResolveBrush(string key)
    {
        var theme = _editor.ActualThemeVariant;
        if (Application.Current?.Resources.TryGetResource(key, theme, out var v) == true && v is IBrush b)
            return b;
        return null;
    }
}
