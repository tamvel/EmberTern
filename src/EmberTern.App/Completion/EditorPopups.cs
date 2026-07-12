using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace EmberTern.App.Completion;

/// <summary>
/// Shared placement helpers for the editor's self-managed popups (Quick Info on Ctrl+Space, the
/// Ctrl-hover tooltip's caret variants, inline rename, Peek Definition). Extracted so the completion
/// controller and the navigation controller anchor popups to the caret with ONE implementation
/// (reuse-before-create) rather than each carrying its own copy.
/// </summary>
internal static class EditorPopups
{
    /// <summary>Anchors <paramref name="popup"/> just below the caret in <paramref name="editor"/>;
    /// falls back to centring on the editor when the caret rect can't be computed (e.g. the caret
    /// line isn't currently rendered).</summary>
    public static void PlaceAtCaret(TextEditor editor, Popup popup)
    {
        if (TryGetCaretRect(editor, out var rect))
        {
            popup.PlacementRect = rect;
            popup.Placement = PlacementMode.BottomEdgeAlignedLeft;
        }
        else
        {
            popup.PlacementRect = null;
            popup.Placement = PlacementMode.Center;
        }
    }

    /// <summary>The caret's rectangle in <paramref name="editor"/> coordinates, or <c>false</c> when
    /// it can't be computed (no rendered lines / the caret line isn't laid out).</summary>
    public static bool TryGetCaretRect(TextEditor editor, out Rect rect)
    {
        rect = default;
        var tv = editor.TextArea.TextView;
        // TextView.VisualLines THROWS VisualLinesInvalidException when a re-measure is pending — e.g.
        // right after a double-click changed the selection, or when a just-activated tab's view hasn't
        // completed its first layout. (This was the double-click crash: the old code read tv.VisualLines
        // OUTSIDE the try below.) Build the lines first when we can; if we can't (the build is already
        // running mid-Measure, or the view isn't laid out yet), fall back to Center placement rather
        // than crashing — never access VisualLines while it's invalid.
        if (!tv.VisualLinesValid)
        {
            try { tv.EnsureVisualLines(); }
            catch (InvalidOperationException) { return false; } // building during Measure — can't compute now
        }
        if (!tv.VisualLinesValid || tv.VisualLines.Count == 0) return false;
        try
        {
            var pos = editor.TextArea.Caret.Position;
            var top = tv.GetVisualPosition(pos, VisualYPosition.LineTop) - tv.ScrollOffset;
            var bottom = tv.GetVisualPosition(pos, VisualYPosition.LineBottom) - tv.ScrollOffset;
            var p1 = tv.TranslatePoint(top, editor);
            var p2 = tv.TranslatePoint(bottom, editor);
            if (p1 is null || p2 is null) return false;
            rect = new Rect(p1.Value.X, p1.Value.Y, 1, Math.Max(1, p2.Value.Y - p1.Value.Y));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
