using System.Collections.Generic;
using AvaloniaEdit.Document;
using EmberTern.Core.Sql.Language.CodeActions;

namespace EmberTern.App.Completion;

/// <summary>
/// <b>The one owner of every change EmberTern makes to a user document.</b> Quick Fixes, safe local
/// rename, and every future code action go through here — there is deliberately no second path that
/// writes to a <see cref="TextDocument"/> on the user's behalf.
/// <para>
/// This is infrastructure, not a Quick Fix helper. It exists because mutating code the user did not
/// type is the single most dangerous thing this application does (Architecture rule #11), and that risk
/// is worth concentrating in one reviewable place rather than re-deriving per feature. Design:
/// <see href="../../docs/design/editor-quick-fixes.md">editor-quick-fixes.md</see> §2.2/§6.
/// </para>
/// <para><b>All or nothing.</b> Every edit is validated before a single character is written; any
/// problem refuses the whole set and leaves the document untouched. There is no partial application and
/// no "best effort".</para>
/// </summary>
internal static class TextEditApplier
{
    /// <summary>
    /// Applies <paramref name="edits"/> atomically, and reports where the caret should end up.
    /// <para>Returns <c>false</c> — having changed nothing — when the document is missing, the set is
    /// empty, any span lies outside the document, any span no longer holds the text its producer
    /// expected (drift), or two edits overlap.</para>
    /// </summary>
    /// <param name="document">The document to change.</param>
    /// <param name="edits">The edits, in any order. They must not overlap.</param>
    /// <param name="caretOffset">Where the caret is now.</param>
    /// <param name="newCaretOffset">Where the caret should go — see <see cref="AdjustCaret"/>. Equals
    /// <paramref name="caretOffset"/> when nothing was applied.</param>
    public static bool TryApply(
        TextDocument? document, IReadOnlyList<TextEdit>? edits, int caretOffset, out int newCaretOffset)
    {
        newCaretOffset = caretOffset;
        if (document is null || edits is null || edits.Count == 0) return false;

        // Ascending order is the validation order (overlap detection needs neighbours adjacent) and the
        // reverse of the application order. A copy: never reorder the caller's list.
        var ordered = new List<TextEdit>(edits);
        ordered.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        if (!Validate(document, ordered)) return false;

        // Last-to-first, so an earlier edit's length change cannot invalidate a later edit's offset.
        // BeginUpdate/EndUpdate makes the whole set ONE undo unit: a half-undone action would leave code
        // that neither the user nor EmberTern authored.
        document.BeginUpdate();
        try
        {
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                var edit = ordered[i];
                document.Replace(edit.Start, edit.Length, edit.NewText);
            }
        }
        finally
        {
            document.EndUpdate();
        }

        newCaretOffset = AdjustCaret(ordered, caretOffset);
        return true;
    }

    // Every reason to refuse, in one place — this IS the drift control, and no caller re-implements any
    // part of it.
    private static bool Validate(TextDocument document, List<TextEdit> ordered)
    {
        int previousEnd = -1;
        foreach (var edit in ordered)
        {
            if (edit.Start < 0 || edit.Length < 0) return false;
            if (edit.End > document.TextLength) return false;
            if (edit.NewText is null || edit.ExpectedOldText is null) return false;

            // Overlapping edits make the result depend on application order, i.e. undefined. A producer
            // that emits them has a bug; refusing is the only safe reading.
            if (edit.Start < previousEnd) return false;
            previousEnd = edit.End;

            // THE drift check: is what we are about to replace still what the producer saw? The user may
            // have typed since the action was offered. Ordinal — a case difference is a real difference
            // in the document, even where Firebird would fold it.
            if (!string.Equals(document.GetText(edit.Start, edit.Length), edit.ExpectedOldText, System.StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Where the caret belongs afterwards. ONE rule, no per-feature policy: if the caret was inside (or
    /// touching) an edited span it lands at the END of that span's replacement; otherwise it keeps its
    /// logical position, shifted by the length change of the edits before it.
    /// <para>
    /// That single rule is what "the natural place" means for both of today's callers. Qualifying a
    /// column leaves the caret after <c>k.nazwa</c>, ready to keep typing; renaming leaves it at the end
    /// of the occurrence the user was standing on rather than jumping to the last one in the document;
    /// and an edit elsewhere in the file does not drag the caret along.
    /// </para>
    /// </summary>
    private static int AdjustCaret(List<TextEdit> ordered, int caretOffset)
    {
        int delta = 0;
        foreach (var edit in ordered)
        {
            if (caretOffset < edit.Start) break;                  // this and every later edit is after the caret
            if (caretOffset <= edit.End)                          // the caret sat in (or on the edge of) this edit
            {
                return edit.Start + delta + edit.NewText.Length;
            }
            delta += edit.NewText.Length - edit.Length;           // wholly before the caret: shift it
        }
        return caretOffset + delta;
    }
}
