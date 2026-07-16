using System;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using EmberTern.Core.Sql.Language.Matching;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.App.Completion;

/// <summary>
/// Stage 8 / M1 — the ONE "Related Elements Highlighting" renderer. It replaces the editor's several
/// separate occurrence/reference highlighters (the former text-selection occurrence highlighter and
/// <c>NavigationController</c>'s semantic reference boxer) with a single background renderer fed by
/// interchangeable pure-Core producers (<see cref="RelatedElementMatcher"/>): selection-word occurrences,
/// the caret symbol's local references, caret-adjacent bracket pairs, and caret-adjacent
/// <c>BEGIN/END</c>. A future structural pair (CASE/END, LOOP, …) is one more producer — never another
/// renderer.
/// <para>Thin glue: it maps the editor state to a <see cref="MatchContext"/>, asks the matcher, and
/// paints. It recomputes on caret move / selection change / model rebuild, and does no analysis on the
/// paint path (viewport-culled + doc-length-clamped, so a momentarily stale span can never draw out of
/// range). Colours come from the theme (<c>RelatedElementHighlight*</c>), so it follows light/dark.</para>
/// </summary>
internal sealed class RelatedElementsRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly Func<SemanticModel?> _model;
    private readonly RelatedElementMatcher _matcher = RelatedElementMatcher.CreateDefault();
    private System.Collections.Generic.IReadOnlyList<TextSpan> _spans = Array.Empty<TextSpan>();

    private RelatedElementsRenderer(TextEditor editor, Func<SemanticModel?> model)
    {
        _editor = editor;
        _model = model;
    }

    /// <summary>Attaches to an editor that has a language model (via its completion controller) — the SQL
    /// editor and every object-editor body. Recomputes on caret / selection / model rebuild.</summary>
    public static RelatedElementsRenderer Attach(TextEditor editor, SqlCompletionController controller)
    {
        var r = Attach(editor, () => controller.Model);
        controller.ModelUpdated += (_, _) => r.Recompute();
        return r;
    }

    /// <summary>Attaches to an editor whose model is supplied by <paramref name="model"/> — pass
    /// <c>() =&gt; null</c> for a model-less surface (the read-only DDL-preview editor), where only the
    /// text-based producers (selection occurrences, bracket pairs) contribute.</summary>
    public static RelatedElementsRenderer Attach(TextEditor editor, Func<SemanticModel?> model)
    {
        var r = new RelatedElementsRenderer(editor, model);
        editor.TextArea.TextView.BackgroundRenderers.Add(r);
        editor.TextArea.Caret.PositionChanged += (_, _) => r.Recompute();
        editor.TextArea.SelectionChanged += (_, _) => r.Recompute();
        return r;
    }

    public KnownLayer Layer => KnownLayer.Selection;

    private void Recompute()
    {
        var doc = _editor.Document;
        if (doc is null) { SetSpans(Array.Empty<TextSpan>()); return; }
        // One document-text materialization per caret/selection change (a string copy, NOT a parse); the
        // producers self-gate the heavier work (occurrence scan only with a selection, re-lex only when the
        // caret is bracket-adjacent). Fine for the editor's document sizes; the paint path stays trivial.
        var ctx = new MatchContext(doc.Text, _editor.CaretOffset, _editor.SelectedText, _model());
        SetSpans(_matcher.Match(ctx));
    }

    /// <summary>Test seam: the spans the renderer currently intends to paint.</summary>
    internal System.Collections.Generic.IReadOnlyList<TextSpan> SpansForTest => _spans;

    private void SetSpans(System.Collections.Generic.IReadOnlyList<TextSpan> spans)
    {
        // Skip ONLY when nothing was and nothing is highlighted — plain-text caret movement, no paint
        // needed. Every other transition repaints, INCLUDING an unchanged non-empty set: that way a paint
        // that didn't land (e.g. the text view hadn't built its visual lines yet on the first caret-driven
        // repaint right after connect) self-heals on the next recompute at the same site, instead of
        // staying invisible until the caret moves to another pair and back (the reported symptom).
        if (_spans.Count == 0 && spans.Count == 0) return;
        _spans = spans;
        // Redraw() — NOT InvalidateVisual() — rebuilds the text view's visual lines and re-renders, so a
        // background renderer always draws against valid lines. A plain InvalidateVisual() could run before
        // the lines were built on that first post-connect repaint, so Draw hit VisualLines.Count == 0 and
        // painted nothing. Same repaint mechanism SemanticHighlighter uses for its non-content updates.
        _editor.TextArea.TextView.Redraw();
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_spans.Count == 0 || textView.VisualLines.Count == 0) return;
        var doc = _editor.Document;
        if (doc is null) return;

        int viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
        int viewEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;

        var fill = ResolveBrush("RelatedElementHighlightBrush");
        var border = ResolveBrush("RelatedElementHighlightBorderBrush");
        var pen = border is null ? null : new Pen(border, 1.5);
        if (fill is null && pen is null) return;

        int docLen = doc.TextLength;
        foreach (var span in _spans)
        {
            if (span.Length == 0) continue;
            if (span.End <= viewStart || span.Start >= viewEnd) continue; // viewport cull
            if (span.Start < 0 || span.End > docLen) continue;            // clamp against a stale span
            var builder = new BackgroundGeometryBuilder { CornerRadius = 2 };
            builder.AddSegment(textView, new TextSegment { StartOffset = span.Start, Length = span.Length });
            var geo = builder.CreateGeometry();
            if (geo is not null) drawingContext.DrawGeometry(fill, pen, geo);
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
