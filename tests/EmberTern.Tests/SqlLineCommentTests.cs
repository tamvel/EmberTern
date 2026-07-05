using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

public class SqlLineCommentTests
{
    [Fact]
    public void Comment_SingleLine_CaretNoSelection_PrefixesCaretLine()
    {
        var r = SqlLineComment.Apply("select 1", caretAt("select 1", 3), 0, LineCommentMode.Comment);
        Assert.Equal("-- select 1", r.Text);
    }

    [Fact]
    public void Uncomment_SingleCommentedLine_RemovesTokenAndOneSpace()
    {
        var r = SqlLineComment.Apply("-- select 1", 0, 0, LineCommentMode.Uncomment);
        Assert.Equal("select 1", r.Text);
    }

    [Fact]
    public void Uncomment_TokenWithoutFollowingSpace_RemovesJustDashes()
    {
        var r = SqlLineComment.Apply("--select 1", 0, 0, LineCommentMode.Uncomment);
        Assert.Equal("select 1", r.Text);
    }

    [Fact]
    public void Toggle_AllCommented_Uncomments()
    {
        var text = "-- a\n-- b";
        var r = SqlLineComment.Apply(text, 0, text.Length, LineCommentMode.Toggle);
        Assert.Equal("a\nb", r.Text);
    }

    [Fact]
    public void Toggle_MixedLines_CommentsAll()
    {
        var text = "-- a\nb";
        var r = SqlLineComment.Apply(text, 0, text.Length, LineCommentMode.Toggle);
        Assert.Equal("-- -- a\n-- b", r.Text);
    }

    [Fact]
    public void Comment_MultiLineSelection_PrefixesEachNonBlankLine()
    {
        var text = "select a\nfrom t\nwhere x = 1";
        var r = SqlLineComment.Apply(text, 0, text.Length, LineCommentMode.Comment);
        Assert.Equal("-- select a\n-- from t\n-- where x = 1", r.Text);
    }

    [Fact]
    public void Comment_SkipsBlankLines()
    {
        var text = "a\n\nb";
        var r = SqlLineComment.Apply(text, 0, text.Length, LineCommentMode.Comment);
        Assert.Equal("-- a\n\n-- b", r.Text);
    }

    [Fact]
    public void PreservesCrLfLineEndings()
    {
        var text = "a\r\nb";
        var r = SqlLineComment.Apply(text, 0, text.Length, LineCommentMode.Comment);
        Assert.Equal("-- a\r\n-- b", r.Text);
    }

    [Fact]
    public void OnlyAffectsSelectedLineBlock_NotTheWholeDocument()
    {
        // Selection covers only the second line ("b").
        var text = "a\nb\nc";
        int start = "a\n".Length; // start of "b"
        var r = SqlLineComment.Apply(text, start, 1, LineCommentMode.Comment);
        Assert.Equal("a\n-- b\nc", r.Text);
    }

    [Fact]
    public void SelectionEndingAtNextLineStart_DoesNotCommentThatNextLine()
    {
        // Select "a\n" — the caret sits at the very start of line "b"; only "a" is affected.
        var text = "a\nb";
        var r = SqlLineComment.Apply(text, 0, "a\n".Length, LineCommentMode.Comment);
        Assert.Equal("-- a\nb", r.Text);
    }

    [Fact]
    public void EmptyText_ReturnsEmpty()
    {
        var r = SqlLineComment.Apply("", 0, 0, LineCommentMode.Comment);
        Assert.Equal("", r.Text);
        Assert.Equal(0, r.SelectionStart);
        Assert.Equal(0, r.SelectionLength);
    }

    [Fact]
    public void CommentThenUncomment_RoundTrips()
    {
        var text = "select a\n  from t\nwhere x = 1";
        var commented = SqlLineComment.Apply(text, 0, text.Length, LineCommentMode.Comment);
        var back = SqlLineComment.Apply(commented.Text, 0, commented.Text.Length, LineCommentMode.Uncomment);
        Assert.Equal(text, back.Text);
    }

    [Fact]
    public void Result_SelectionCoversTransformedBlock()
    {
        var text = "a\nb";
        var r = SqlLineComment.Apply(text, 0, text.Length, LineCommentMode.Comment);
        // Selection should span the new block "-- a\n-- b".
        Assert.Equal(0, r.SelectionStart);
        Assert.Equal("-- a\n-- b".Length, r.SelectionLength);
    }

    private static int caretAt(string _, int offset) => offset;
}
