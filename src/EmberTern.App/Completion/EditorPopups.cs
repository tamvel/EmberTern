using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace EmberTern.App.Completion;

/// <summary>
/// Shared placement helpers for the editor's self-managed popups (Quick Info on Ctrl+Space, inline
/// rename, Peek Definition) and for the <see cref="OverlayLayer"/>-hosted cards (the Parameter Helper,
/// the unified hover). Extracted so the completion and navigation controllers anchor popups with ONE
/// implementation (reuse-before-create) rather than each carrying its own copy.
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

    /// <summary>
    /// Keeps an <see cref="OverlayLayer"/>-hosted card on screen: nudge it left if it overflows the right
    /// edge, and flip it ABOVE its anchor if it would overflow the bottom. Call after layout, when the
    /// card's <c>Bounds</c> are known.
    /// <para>
    /// Shared by the Parameter Helper (a 40-column INSERT is taller than the editor) and the unified
    /// hover (a hover near the last line) — the same geometry problem, so one implementation.
    /// </para>
    /// </summary>
    /// <param name="flipOffset">Vertical distance from the card's current top back to its anchor, so a
    /// flipped card clears the thing it describes: the caret's line height for a caret-anchored card,
    /// the pointer gap for a pointer-anchored one.</param>
    public static void ClampIntoOverlay(OverlayLayer overlay, Control card, double flipOffset)
    {
        double ow = overlay.Bounds.Width, oh = overlay.Bounds.Height;
        double cw = card.Bounds.Width, ch = card.Bounds.Height;
        double left = Canvas.GetLeft(card), top = Canvas.GetTop(card);
        if (cw > 0 && left + cw > ow) left = Math.Max(0, ow - cw - 2);
        if (ch > 0 && top + ch > oh)
        {
            double above = top - ch - flipOffset;
            top = above >= 0 ? above : Math.Max(0, oh - ch - 2);
        }
        Canvas.SetLeft(card, left);
        Canvas.SetTop(card, top);
    }
}
