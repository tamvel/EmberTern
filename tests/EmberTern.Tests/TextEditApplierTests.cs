using AvaloniaEdit.Document;
using EmberTern.App.Completion;
using EmberTern.Core.Sql.Language.CodeActions;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage Q / Q2 — <see cref="TextEditApplier"/>, the ONE owner of every change EmberTern makes to a user
/// document (Quick Fixes, safe local rename, future code actions).
/// <para>
/// This is infrastructure, so it is pinned as infrastructure: the refusal cases carry more weight than
/// the happy path. Mutating code the user did not type is the most dangerous thing this application does
/// (Architecture rule #11), and every refusal below is a way that could go wrong.
/// </para>
/// <para>Needs no window: <see cref="TextDocument"/> is a plain model type.</para>
/// </summary>
public class TextEditApplierTests
{
    private static TextEdit Edit(int start, int length, string newText, string expectedOld)
        => new(start, length, newText, expectedOld);

    // ══ Applying ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AppliesASingleEdit()
    {
        var doc = new TextDocument("select nazwa from t");

        Assert.True(TextEditApplier.TryApply(doc, new[] { Edit(7, 5, "k.nazwa", "nazwa") }, 7, out _));
        Assert.Equal("select k.nazwa from t", doc.Text);
    }

    [Fact]
    public void AppliesSeveralEdits_WithoutLettingEarlierOnesShiftLaterOffsets()
    {
        // The classic corruption: apply front-to-back and every later offset is wrong by the length
        // delta. Given deliberately unsorted input, to prove the applier orders it itself.
        var doc = new TextDocument("a bb a bb a");
        var edits = new[]
        {
            Edit(10, 1, "XXXX", "a"),
            Edit(0, 1, "XXXX", "a"),
            Edit(5, 1, "XXXX", "a"),
        };

        Assert.True(TextEditApplier.TryApply(doc, edits, 0, out _));
        Assert.Equal("XXXX bb XXXX bb XXXX", doc.Text);
    }

    [Fact]
    public void AppliesAsOneUndoUnit()
    {
        // A half-undone action would leave code neither the user nor EmberTern authored.
        var doc = new TextDocument("a bb a bb a");
        doc.UndoStack.SizeLimit = 100;
        var edits = new[]
        {
            Edit(0, 1, "XXXX", "a"),
            Edit(5, 1, "XXXX", "a"),
            Edit(10, 1, "XXXX", "a"),
        };

        Assert.True(TextEditApplier.TryApply(doc, edits, 0, out _));
        Assert.True(doc.UndoStack.CanUndo);
        doc.UndoStack.Undo();

        Assert.Equal("a bb a bb a", doc.Text); // ONE undo restored everything
    }

    [Fact]
    public void AppliesAPureInsertion()
    {
        var doc = new TextDocument("ab");
        Assert.True(TextEditApplier.TryApply(doc, new[] { Edit(1, 0, "X", "") }, 0, out _));
        Assert.Equal("aXb", doc.Text);
    }

    // ══ Refusing — the drift control ═════════════════════════════════════════════════════════

    [Fact]
    public void RefusesWhenTheTextIsNotWhatTheProducerExpected()
    {
        // The user typed after the action was offered. Applying anyway would put text at a stale offset.
        var doc = new TextDocument("select inne from t");

        Assert.False(TextEditApplier.TryApply(doc, new[] { Edit(7, 5, "k.nazwa", "nazwa") }, 7, out _));
        Assert.Equal("select inne from t", doc.Text); // untouched
    }

    [Fact]
    public void RefusesOnACaseDifference()
    {
        // Ordinal: a case difference is a real difference in the document, even where Firebird folds it.
        var doc = new TextDocument("select NAZWA from t");
        Assert.False(TextEditApplier.TryApply(doc, new[] { Edit(7, 5, "k.nazwa", "nazwa") }, 7, out _));
    }

    [Fact]
    public void RefusesTheWholeSetWhenOnlyOneEditHasDrifted()
    {
        // All-or-nothing: a partially-applied rename is worse than no rename.
        var doc = new TextDocument("a bb a");
        var edits = new[]
        {
            Edit(0, 1, "X", "a"),
            Edit(5, 1, "X", "ZZZ"), // wrong expectation
        };

        Assert.False(TextEditApplier.TryApply(doc, edits, 0, out _));
        Assert.Equal("a bb a", doc.Text);
    }

    [Fact]
    public void RefusesOutOfBoundsSpans()
    {
        var doc = new TextDocument("abc");
        Assert.False(TextEditApplier.TryApply(doc, new[] { Edit(2, 5, "X", "c") }, 0, out _));
        Assert.False(TextEditApplier.TryApply(doc, new[] { Edit(-1, 1, "X", "a") }, 0, out _));
        Assert.Equal("abc", doc.Text);
    }

    [Fact]
    public void RefusesOverlappingEdits()
    {
        // Overlap makes the result depend on application order, i.e. undefined. A producer emitting them
        // has a bug, and guessing which one wins would be exactly the wrong response.
        var doc = new TextDocument("abcdef");
        var edits = new[]
        {
            Edit(0, 3, "X", "abc"),
            Edit(2, 3, "Y", "cde"),
        };

        Assert.False(TextEditApplier.TryApply(doc, edits, 0, out _));
        Assert.Equal("abcdef", doc.Text);
    }

    [Fact]
    public void RefusesNothingToDo()
    {
        var doc = new TextDocument("abc");
        Assert.False(TextEditApplier.TryApply(doc, System.Array.Empty<TextEdit>(), 1, out int caret));
        Assert.Equal(1, caret);                                    // the caret is left alone
        Assert.False(TextEditApplier.TryApply(null, new[] { Edit(0, 1, "X", "a") }, 0, out _));
        Assert.False(TextEditApplier.TryApply(doc, null, 0, out _));
    }

    // ══ Caret — "the natural place" ══════════════════════════════════════════════════════════

    [Fact]
    public void Caret_InsideAnEdit_LandsAtTheEndOfTheReplacement()
    {
        // Qualifying a column leaves the caret after 'k.nazwa', ready to keep typing.
        var doc = new TextDocument("select nazwa from t");
        Assert.True(TextEditApplier.TryApply(doc, new[] { Edit(7, 5, "k.nazwa", "nazwa") }, 9, out int caret));
        Assert.Equal(7 + "k.nazwa".Length, caret);
    }

    [Fact]
    public void Caret_OnAnEditBoundary_CountsAsInsideIt()
    {
        var doc = new TextDocument("select nazwa from t");
        Assert.True(TextEditApplier.TryApply(doc, new[] { Edit(7, 5, "k.nazwa", "nazwa") }, 12, out int caret));
        Assert.Equal(7 + "k.nazwa".Length, caret);
    }

    [Fact]
    public void Caret_StaysOnTheOccurrenceTheUserWasStandingOn_WhenRenamingSeveral()
    {
        // Renaming must not drag the caret to the last occurrence in the document.
        var doc = new TextDocument("v = v + v");
        var edits = new[]
        {
            Edit(0, 1, "total", "v"),
            Edit(4, 1, "total", "v"),
            Edit(8, 1, "total", "v"),
        };

        Assert.True(TextEditApplier.TryApply(doc, edits, 4, out int caret)); // caret on the SECOND 'v'
        Assert.Equal("total = total + total", doc.Text);
        Assert.Equal("total = total".Length, caret);                          // end of that same occurrence
    }

    [Fact]
    public void Caret_AfterTheEdits_ShiftsByTheirLengthChange()
    {
        // An edit earlier in the file must not leave the caret on a different character.
        var doc = new TextDocument("a bb ccc");
        Assert.True(TextEditApplier.TryApply(doc, new[] { Edit(0, 1, "XXX", "a") }, 5, out int caret));
        Assert.Equal("XXX bb ccc", doc.Text);
        Assert.Equal(5 + 2, caret);
        Assert.Equal('c', doc.Text[caret]); // the same logical character it was on before
    }

    [Fact]
    public void Caret_BeforeTheEdits_DoesNotMove()
    {
        var doc = new TextDocument("aaa bbb");
        Assert.True(TextEditApplier.TryApply(doc, new[] { Edit(4, 3, "X", "bbb") }, 1, out int caret));
        Assert.Equal(1, caret);
    }
}
