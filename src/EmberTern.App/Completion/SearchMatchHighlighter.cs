using System;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace EmberTern.App.Completion;

/// <summary>
/// Boxes every occurrence of a fixed search term in a read-only AvaloniaEdit preview
/// (Global Search results). Cloned from <see cref="OccurrenceHighlighter"/> but driven
/// by an explicit term (case-insensitive substring, no identifier-boundary requirement)
/// rather than the editor selection. Theme brushes → follows light/dark.
/// </summary>
internal sealed class SearchMatchHighlighter : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private string? _term;

    private SearchMatchHighlighter(TextEditor editor) => _editor = editor;

    public static SearchMatchHighlighter Attach(TextEditor editor)
    {
        var hl = new SearchMatchHighlighter(editor);
        editor.TextArea.TextView.BackgroundRenderers.Add(hl);
        return hl;
    }

    public void SetTerm(string? term)
    {
        if (string.Equals(term, _term, StringComparison.Ordinal)) return;
        _term = term;
        _editor.TextArea.TextView.InvalidateVisual();
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (string.IsNullOrEmpty(_term) || textView.VisualLines.Count == 0) return;
        var doc = _editor.Document;
        if (doc is null) return;
        var text = doc.Text;

        int viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
        int viewEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;

        var fill = ResolveBrush("OccurrenceHighlightBrush");
        var outline = ResolveBrush("AccentBrush");
        var pen = outline is null ? null : new Pen(outline, 1);
        if (fill is null && pen is null) return;

        int i = Math.Max(0, viewStart);
        int limit = Math.Min(text.Length, viewEnd);
        while (i <= limit - _term!.Length)
        {
            int idx = text.IndexOf(_term, i, StringComparison.OrdinalIgnoreCase);
            if (idx < 0 || idx > limit - _term.Length) break;
            var builder = new BackgroundGeometryBuilder { CornerRadius = 2 };
            builder.AddSegment(textView, new TextSegment { StartOffset = idx, Length = _term.Length });
            var geo = builder.CreateGeometry();
            if (geo is not null) drawingContext.DrawGeometry(fill, pen, geo);
            i = idx + _term.Length;
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
