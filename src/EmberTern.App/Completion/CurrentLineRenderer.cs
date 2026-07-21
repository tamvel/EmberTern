using System;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace EmberTern.App.Completion;

/// <summary>
/// Stage X / D4 — paints the debugger's <b>current-statement</b> marker: the line(s) the session is paused
/// on (spec §9.6). A pure <b>client</b> of the debug tab's read-only state — it reads a span (start + length)
/// from a provider and does no analysis. Mirrors <see cref="SquiggleRenderer"/> /
/// <see cref="RelatedElementsRenderer"/>: an <see cref="IBackgroundRenderer"/> whose colours are theme tokens,
/// so it follows light/dark with no hardcoded colours. Repaint is driven by the view via
/// <c>TextView.Redraw()</c> (never <c>InvalidateVisual()</c> — gotcha #223: it can run before visual lines
/// exist and a diff-guard makes the miss permanent).
/// <para>
/// D15.1 Seam B rebuilt the visuals to a calm, IDE-grade marker (never a new effect, just the right one):
/// a <b>full-line-width</b> wash (<c>DebugCurrentLineBrush</c>, a quiet ~10–16% blue) instead of the old
/// amber statement-span band, plus a crisp <b>accent bar</b> at the line's left edge
/// (<c>DebugCurrentLineBarBrush</c>). It draws as the BACKDROP (inserted first, below the squiggle /
/// related-element renderers) so those and the text selection read on top of it; the low alpha never masks
/// glyphs or syntax colour. Per-visual-line geometry from <see cref="BackgroundGeometryBuilder"/> keeps it
/// correct under word wrap, folding, and variable line heights (a folded/hidden line has no geometry ⇒ it
/// is simply not painted).
/// </para>
/// </summary>
internal sealed class CurrentLineRenderer : IBackgroundRenderer
{
    // The left accent bar width, in DIPs. A thin marker, not a second band.
    private const double BarWidth = 2.5;

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
        // Insert FIRST so the current-line wash is the backdrop: the squiggle / related-element background
        // renderers (added by SqlEditorBehavior.Attach before this) then draw ON TOP, staying legible.
        editor.TextArea.TextView.BackgroundRenderers.Insert(0, renderer);
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

        var wash = ResolveBrush("DebugCurrentLineBrush");
        var bar = ResolveBrush("DebugCurrentLineBarBrush");
        if (wash is null && bar is null) return;

        // One vertical band per visual line the statement touches (GetRectsForSegment already accounts for
        // word wrap, folding and variable heights). We reuse only each rect's Y/Height and span the FULL
        // viewport width, so the marker reads as a calm full-line highlight rather than a span band.
        double width = textView.Bounds.Width;
        var segment = new TextSegment { StartOffset = s, Length = e - s };
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
        {
            var line = new Rect(0, rect.Top, width, rect.Height);
            if (wash is not null) drawingContext.FillRectangle(wash, line);
            if (bar is not null) drawingContext.FillRectangle(bar, new Rect(0, rect.Top, BarWidth, rect.Height));
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
