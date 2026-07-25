using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Completion;

/// <summary>
/// Stage X / D15.5 — draws the debugger's <b>inline values</b>: greyed <c>name = value</c> annotations at the
/// end of a source line (spec §7). A pure <b>client</b> of the debug tab's read-only state — it reads a
/// ready-made annotation set from a provider (the VM computes WHICH values show and on WHICH line; this only
/// draws them) and does no analysis. Mirrors <see cref="CurrentLineRenderer"/> / <see cref="SquiggleRenderer"/>:
/// an <see cref="IBackgroundRenderer"/> whose colour is a theme token, repainted via <c>TextView.Redraw()</c>.
/// <para>
/// It <b>never shifts the document text</b>: the annotation is painted in the empty space PAST the line's text
/// end (position from <see cref="BackgroundGeometryBuilder.GetRectsForSegment"/>, the same geometry the
/// current-line marker uses — so it is correct under word wrap / folding), not inserted as a document element.
/// A line with no visible geometry (folded / off-screen) is simply not drawn. Appended AFTER the current-line
/// renderer so it paints on top of that calm wash.
/// </para>
/// </summary>
internal sealed class InlineValuesRenderer : IBackgroundRenderer
{
    // Horizontal gap (DIPs) between the line's last character and the annotation, so it reads as a separate hint.
    private const double GapBeforeAnnotation = 28;
    // Separator between multiple annotations on the same line.
    private const string Separator = "    ";

    private readonly TextEditor _editor;
    private readonly Func<IReadOnlyList<InlineValueAnnotation>> _provider;

    private InlineValuesRenderer(TextEditor editor, Func<IReadOnlyList<InlineValueAnnotation>> provider)
    {
        _editor = editor;
        _provider = provider;
    }

    public static InlineValuesRenderer Attach(TextEditor editor, Func<IReadOnlyList<InlineValueAnnotation>> provider)
    {
        var renderer = new InlineValuesRenderer(editor, provider);
        // Append (not insert-first): the current-line wash is the backdrop (inserted at 0); the inline
        // annotations paint on top of it so they stay legible over the wash.
        editor.TextArea.TextView.BackgroundRenderers.Add(renderer);
        return renderer;
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var annotations = _provider();
        if (annotations is null || annotations.Count == 0 || textView.VisualLines.Count == 0) return;

        var doc = _editor.Document;
        if (doc is null) return;

        var brush = ResolveBrush("SubtleForegroundBrush");
        if (brush is null) return;

        // Group by document line so several changed variables on the current line render as one trailing hint.
        var byLine = new Dictionary<int, List<string>>();
        foreach (var a in annotations)
        {
            if (a.AnchorOffset < 0 || a.AnchorOffset > doc.TextLength || string.IsNullOrEmpty(a.Text)) continue;
            int lineNumber = doc.GetLineByOffset(a.AnchorOffset).LineNumber;
            if (!byLine.TryGetValue(lineNumber, out var texts))
            {
                texts = new List<string>();
                byLine[lineNumber] = texts;
            }
            texts.Add(a.Text);
        }
        if (byLine.Count == 0) return;

        double emSize = Math.Max(1.0, _editor.FontSize - 1);
        var typeface = new Typeface(_editor.FontFamily);

        foreach (var (lineNumber, texts) in byLine)
        {
            var line = doc.GetLineByNumber(lineNumber);
            // The end of the line's text on its LAST visual line (accounts for word wrap / folding / heights);
            // no rects ⇒ the line is not currently visible, so nothing to draw.
            Rect? lastRect = null;
            var segment = new TextSegment { StartOffset = line.Offset, Length = line.Length };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) lastRect = rect;
            if (lastRect is not { } r) continue;

            var text = new FormattedText(
                string.Join(Separator, texts), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, emSize, brush);
            double x = r.Right + GapBeforeAnnotation;
            double y = r.Top + Math.Max(0, (r.Height - text.Height) / 2);
            drawingContext.DrawText(text, new Point(x, y));
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
