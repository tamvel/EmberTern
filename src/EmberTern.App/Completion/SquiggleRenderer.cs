using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using EmberTern.Core.Sql.Language;

namespace EmberTern.App.Completion;

/// <summary>
/// Stage 7 / S3 — draws a wavy underline (squiggle) beneath every diagnostic span in an AvaloniaEdit
/// editor. A pure <b>client</b> of the diagnostics pipeline: it renders the cached
/// <see cref="Diagnostic"/> list the per-editor language service produced (via
/// <see cref="SqlCompletionController.Diagnostics"/>) and does <em>no</em> analysis, structure walking,
/// or SQL interpretation of its own — it consumes only <see cref="Diagnostic.Start"/> /
/// <see cref="Diagnostic.Length"/> / <see cref="Diagnostic.Severity"/>.
/// <para>
/// Mirrors the existing background-renderer highlighters (<see cref="RelatedElementsRenderer"/> /
/// <see cref="SearchMatchHighlighter"/>): an <see cref="IBackgroundRenderer"/> attached in the single
/// wiring seam <see cref="SqlEditorBehavior.Attach"/>, repainting on the shared
/// <see cref="SqlCompletionController.ModelUpdated"/> cycle (same signal semantic highlighting uses) —
/// no second parse loop. Severity → theme brush (<c>ErrorBrush</c> / <c>WarningBrush</c> /
/// <c>SubtleForegroundBrush</c>), so it follows light/dark with no hardcoded colours. Read-only paint —
/// §0 holds by construction.
/// </para>
/// </summary>
internal sealed class SquiggleRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly Func<IReadOnlyList<Diagnostic>> _diagnostics;

    private SquiggleRenderer(TextEditor editor, Func<IReadOnlyList<Diagnostic>> diagnostics)
    {
        _editor = editor;
        _diagnostics = diagnostics;
    }

    /// <summary>Attaches diagnostic squiggles to <paramref name="editor"/>, driven by
    /// <paramref name="controller"/>'s cached diagnostics. Repaints whenever the model — and therefore
    /// its diagnostics — is rebuilt.</summary>
    public static void Attach(TextEditor editor, SqlCompletionController controller)
    {
        var renderer = Attach(editor, () => controller.Diagnostics);
        controller.ModelUpdated += (_, _) => editor.TextArea.TextView.InvalidateVisual();
        _ = renderer;
    }

    /// <summary>Attaches with an explicit diagnostics source (test seam — the production overload wires
    /// it to the controller's cached diagnostics + a repaint on model change).</summary>
    internal static SquiggleRenderer Attach(TextEditor editor, Func<IReadOnlyList<Diagnostic>> diagnostics)
    {
        var renderer = new SquiggleRenderer(editor, diagnostics);
        editor.TextArea.TextView.BackgroundRenderers.Add(renderer);
        return renderer;
    }

    // Draw under the text (below the selection), so a squiggle reads as an underline that never masks
    // the glyphs or the selection highlight.
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var diagnostics = _diagnostics();
        if (diagnostics.Count == 0 || textView.VisualLines.Count == 0) return;
        var doc = _editor.Document;
        if (doc is null) return;

        int viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
        int viewEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        int docLen = doc.TextLength;

        foreach (var d in diagnostics)
        {
            // Cheap viewport cull — bound the paint cost to on-screen diagnostics on a large script.
            if (d.End <= viewStart || d.Start >= viewEnd) continue;

            // Clamp to the current document. Diagnostics are version-matched to the model, but a paint
            // can land a hair ahead of the next rebuild after an edit — never draw past the text.
            int start = Math.Max(0, d.Start);
            int end = Math.Min(docLen, d.End);
            if (start >= end) continue;

            var pen = PenFor(d.Severity);
            if (pen is null) continue;

            var segment = new TextSegment { StartOffset = start, Length = end - start };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                DrawSquiggle(drawingContext, pen, rect.Left, rect.Right, rect.Bottom);
            }
        }
    }

    // A triangle-wave underline along the bottom edge of a span rectangle — the conventional squiggle.
    private static void DrawSquiggle(DrawingContext dc, IPen pen, double left, double right, double bottom)
    {
        const double step = 3.0;       // half-wavelength (px)
        const double amplitude = 2.5;  // peak height (px)
        double y = bottom - 1;         // sit just inside the line box

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(left, y), isFilled: false);
            bool up = true;
            for (double x = left; x < right; x += step)
            {
                double nextX = Math.Min(x + step, right);
                ctx.LineTo(new Point(nextX, up ? y - amplitude : y));
                up = !up;
            }
        }
        dc.DrawGeometry(null, pen, geometry);
    }

    private IPen? PenFor(DiagnosticSeverity severity)
    {
        var brush = ResolveBrush(severity switch
        {
            DiagnosticSeverity.Error => "ErrorBrush",
            DiagnosticSeverity.Warning => "WarningBrush",
            _ => "SubtleForegroundBrush", // Info — deliberately subtle (design §5)
        });
        return brush is null ? null : new Pen(brush, 1.1);
    }

    private IBrush? ResolveBrush(string key)
    {
        var theme = _editor.ActualThemeVariant;
        if (Application.Current?.Resources.TryGetResource(key, theme, out var v) == true && v is IBrush b)
            return b;
        return null;
    }
}
