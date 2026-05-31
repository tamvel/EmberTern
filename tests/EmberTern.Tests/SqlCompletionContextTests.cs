using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

public class SqlCompletionContextTests
{
    [Fact]
    public void GetCurrentWord_EmptyText_ReturnsEmpty()
    {
        var w = SqlCompletionContext.GetCurrentWord(string.Empty, 0);
        Assert.True(w.IsEmpty);
        Assert.Equal(0, w.Start);
        Assert.Equal(string.Empty, w.Text);
    }

    [Fact]
    public void GetCurrentWord_CaretAtEndOfIdentifier_ReturnsFullWord()
    {
        // "SELE|" — caret right after "SELE".
        var w = SqlCompletionContext.GetCurrentWord("SELE", 4);
        Assert.Equal(0, w.Start);
        Assert.Equal(4, w.Length);
        Assert.Equal("SELE", w.Text);
    }

    [Fact]
    public void GetCurrentWord_CaretAfterWhitespace_ReturnsEmpty()
    {
        // "SELECT |" — caret after space; current word is empty.
        var w = SqlCompletionContext.GetCurrentWord("SELECT ", 7);
        Assert.True(w.IsEmpty);
        Assert.Equal(7, w.Start);
    }

    [Fact]
    public void GetCurrentWord_CaretMidLine_ReturnsTrailingPrefix()
    {
        // "SELECT * FROM CUST|OMER" — caret between CUST and OMER. We only walk
        // backward, so the "word" is "CUST" (start=14, length=4).
        const string sql = "SELECT * FROM CUSTOMER";
        var w = SqlCompletionContext.GetCurrentWord(sql, 18);
        Assert.Equal(14, w.Start);
        Assert.Equal("CUST", w.Text);
    }

    [Fact]
    public void GetCurrentWord_StopsAtPunctuation()
    {
        // "T.NA|" — caret after "NA", dot stops the walk.
        var w = SqlCompletionContext.GetCurrentWord("T.NA", 4);
        Assert.Equal(2, w.Start);
        Assert.Equal("NA", w.Text);
    }

    [Fact]
    public void GetCurrentWord_IncludesUnderscoresAndDigits()
    {
        var w = SqlCompletionContext.GetCurrentWord("RDB$RELATIONS", 13);
        // '$' is not a C# identifier char per our rule — walk stops at it.
        Assert.Equal("RELATIONS", w.Text);
        Assert.Equal(4, w.Start);

        var w2 = SqlCompletionContext.GetCurrentWord("FOO_123", 7);
        Assert.Equal("FOO_123", w2.Text);
        Assert.Equal(0, w2.Start);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("a", false)]
    [InlineData("ab", false)]
    [InlineData("abc", true)]
    [InlineData("SELECT", true)]
    [InlineData("123", false)] // pure numeric
    [InlineData("12a", true)]  // mixed but contains a letter
    [InlineData("__a", true)]  // underscores + letter
    public void ShouldAutoTrigger_HonoursMinLengthAndIdentifierShape(string input, bool expected)
    {
        Assert.Equal(expected, SqlCompletionContext.ShouldAutoTrigger(input));
    }

    [Fact]
    public void IsIdentifierChar_LettersDigitsUnderscoreOnly()
    {
        Assert.True(SqlCompletionContext.IsIdentifierChar('A'));
        Assert.True(SqlCompletionContext.IsIdentifierChar('z'));
        Assert.True(SqlCompletionContext.IsIdentifierChar('_'));
        Assert.True(SqlCompletionContext.IsIdentifierChar('9'));
        Assert.False(SqlCompletionContext.IsIdentifierChar(' '));
        Assert.False(SqlCompletionContext.IsIdentifierChar('.'));
        Assert.False(SqlCompletionContext.IsIdentifierChar('$'));
        Assert.False(SqlCompletionContext.IsIdentifierChar('('));
    }

    // -- GetDotContext ---------------------------------------------------------

    [Fact]
    public void GetDotContext_NoDot_ReturnsNull()
    {
        Assert.Null(SqlCompletionContext.GetDotContext("SELECT", 6));
    }

    [Fact]
    public void GetDotContext_ImmediatelyAfterDot_EmptyPrefix()
    {
        // "N."   caret at offset 2
        var dot = SqlCompletionContext.GetDotContext("N.", 2);
        Assert.NotNull(dot);
        Assert.Equal("N", dot!.Value.Qualifier);
        Assert.Equal(2, dot.Value.PrefixStart);
        Assert.Equal(0, dot.Value.PrefixLength);
        Assert.Equal(string.Empty, dot.Value.Prefix);
    }

    [Fact]
    public void GetDotContext_PartialPrefix()
    {
        // "ALIAS.ID|"  caret at offset 8
        const string text = "ALIAS.ID";
        var dot = SqlCompletionContext.GetDotContext(text, text.Length);
        Assert.NotNull(dot);
        Assert.Equal("ALIAS", dot!.Value.Qualifier);
        Assert.Equal(6, dot.Value.PrefixStart);
        Assert.Equal("ID", dot.Value.Prefix);
    }

    [Fact]
    public void GetDotContext_QualifierUppercased()
    {
        // "naGL.|" — qualifier should canonicalize to uppercase for FB catalog match.
        var dot = SqlCompletionContext.GetDotContext("naGL.", 5);
        Assert.NotNull(dot);
        Assert.Equal("NAGL", dot!.Value.Qualifier);
    }

    [Fact]
    public void GetDotContext_DotWithoutQualifier_ReturnsNull()
    {
        // ". X" — no qualifier left of the dot.
        Assert.Null(SqlCompletionContext.GetDotContext(". X", 1));
    }

    // -- GetWordAt -------------------------------------------------------------

    [Fact]
    public void GetWordAt_EmptyText_ReturnsEmpty()
    {
        var w = SqlCompletionContext.GetWordAt(string.Empty, 0);
        Assert.True(w.IsEmpty);
        Assert.Equal(0, w.Start);
    }

    [Fact]
    public void GetWordAt_CaretInMiddleOfIdentifier_ReturnsFullWord()
    {
        // "SELECT * FROM CUST|OMER" — caret between CUST and OMER.
        const string sql = "SELECT * FROM CUSTOMER";
        var w = SqlCompletionContext.GetWordAt(sql, 18);
        Assert.Equal(14, w.Start);
        Assert.Equal("CUSTOMER", w.Text);
    }

    [Fact]
    public void GetWordAt_CaretAtStartOfIdentifier_ReturnsFullWord()
    {
        // "SELECT * FROM |CUSTOMER" — caret right before CUSTOMER.
        const string sql = "SELECT * FROM CUSTOMER";
        var w = SqlCompletionContext.GetWordAt(sql, 14);
        Assert.Equal(14, w.Start);
        Assert.Equal("CUSTOMER", w.Text);
    }

    [Fact]
    public void GetWordAt_CaretAtEndOfIdentifier_ReturnsFullWord()
    {
        // "SELECT * FROM CUSTOMER|" — caret right after CUSTOMER.
        const string sql = "SELECT * FROM CUSTOMER";
        var w = SqlCompletionContext.GetWordAt(sql, sql.Length);
        Assert.Equal(14, w.Start);
        Assert.Equal("CUSTOMER", w.Text);
    }

    [Fact]
    public void GetWordAt_CaretBetweenWords_ReturnsEmpty()
    {
        // "SELECT | * FROM T" — caret in the spaces.
        var w = SqlCompletionContext.GetWordAt("SELECT  * FROM T", 7);
        Assert.True(w.IsEmpty);
        Assert.Equal(7, w.Start);
    }

    [Fact]
    public void GetWordAt_StopsAtDot()
    {
        // "T.NAME" — caret inside NAME shouldn't pick up T.
        var w = SqlCompletionContext.GetWordAt("T.NAME", 4);
        Assert.Equal(2, w.Start);
        Assert.Equal("NAME", w.Text);
    }

    [Fact]
    public void GetWordAt_StopsAtDollarSign()
    {
        // Firebird system names ("RDB$RELATIONS") — '$' isn't an identifier char.
        var w = SqlCompletionContext.GetWordAt("RDB$RELATIONS", 10);
        Assert.Equal("RELATIONS", w.Text);
        Assert.Equal(4, w.Start);
    }

    [Fact]
    public void GetWordAt_IncludesUnderscoresAndDigits()
    {
        // "MY_TABLE_2" — underscores and digits both count as identifier chars.
        var w = SqlCompletionContext.GetWordAt("FROM MY_TABLE_2 WHERE", 9);
        Assert.Equal("MY_TABLE_2", w.Text);
        Assert.Equal(5, w.Start);
    }

    [Fact]
    public void GetDotContext_MidWordWithDot()
    {
        // "SELECT N.A|B FROM T" — caret after the A in "N.AB" (between A and B).
        const string text = "SELECT N.AB FROM T";
        // caret at offset 10 → between A and B.
        var dot = SqlCompletionContext.GetDotContext(text, 10);
        Assert.NotNull(dot);
        Assert.Equal("N", dot!.Value.Qualifier);
        Assert.Equal(9, dot.Value.PrefixStart);
        Assert.Equal("A", dot.Value.Prefix);
    }
}
