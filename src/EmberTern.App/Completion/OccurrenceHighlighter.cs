using System;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace EmberTern.App.Completion;

/// <summary>
/// Boxes every occurrence of the currently-selected identifier inside an AvaloniaEdit
/// editor (the common "select a word → all matches highlighted" QoL behaviour).
/// Implemented as a background renderer driven by the editor's selection — reused by
/// every SQL surface via <see cref="SqlEditorBehavior.Attach"/>. Colours come from the
/// theme (<c>OccurrenceHighlightBrush</c> fill + <c>AccentBrush</c> outline), so it
/// follows light/dark.
/// </summary>
internal sealed class OccurrenceHighlighter : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private string? _word;

    private OccurrenceHighlighter(TextEditor editor) => _editor = editor;

    public static void Attach(TextEditor editor)
    {
        var hl = new OccurrenceHighlighter(editor);
        editor.TextArea.TextView.BackgroundRenderers.Add(hl);
        editor.TextArea.SelectionChanged += (_, _) => hl.OnSelectionChanged();
    }

    public KnownLayer Layer => KnownLayer.Selection;

    private void OnSelectionChanged()
    {
        var sel = _editor.SelectedText;
        var w = IsIdentifier(sel) ? sel : null;
        if (string.Equals(w, _word, StringComparison.Ordinal)) return;
        _word = w;
        _editor.TextArea.TextView.InvalidateVisual();
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (string.IsNullOrEmpty(_word) || textView.VisualLines.Count == 0) return;
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
        while (i <= limit - _word!.Length)
        {
            int idx = text.IndexOf(_word, i, StringComparison.OrdinalIgnoreCase);
            if (idx < 0 || idx > limit - _word.Length) break;
            int end = idx + _word.Length;
            bool boundaryLeft = idx == 0 || !IsIdentChar(text[idx - 1]);
            bool boundaryRight = end >= text.Length || !IsIdentChar(text[end]);
            if (boundaryLeft && boundaryRight)
            {
                var builder = new BackgroundGeometryBuilder { CornerRadius = 2 };
                builder.AddSegment(textView, new TextSegment { StartOffset = idx, Length = _word.Length });
                var geo = builder.CreateGeometry();
                if (geo is not null) drawingContext.DrawGeometry(fill, pen, geo);
            }
            i = idx + 1;
        }
    }

    private IBrush? ResolveBrush(string key)
    {
        var theme = _editor.ActualThemeVariant;
        if (Application.Current?.Resources.TryGetResource(key, theme, out var v) == true && v is IBrush b)
            return b;
        return null;
    }

    private static bool IsIdentifier(string? s)
    {
        if (string.IsNullOrEmpty(s) || s!.Length < 2) return false;
        if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;
        foreach (var c in s)
            if (!IsIdentChar(c)) return false;
        return true;
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
}
