using EmberTern.Core.Sql.Language.Ergonomics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Typing Ergonomics — character delimiter pairing (design §3.3): opener pairs, closer types through,
/// backspace on an empty pair removes both. Pure: a function of (text, caret, typed char).
/// </summary>
public class DelimiterPairingTests
{
    /// <summary>Types <paramref name="typed"/> at the caret marked `|`, applying either our edit or the
    /// ordinary insertion, and returns the result with the caret marked — so each case reads as what the
    /// developer sees.</summary>
    private static string Type(string textWithCaret, char typed)
    {
        int caret = textWithCaret.IndexOf('|');
        Assert.True(caret >= 0, "the case must mark the caret with '|'");
        var text = textWithCaret.Remove(caret, 1);

        var edit = DelimiterPairing.OnCharacterTyped(text, caret, typed)
                   // Null = "insert it normally", which is what the editor would have done.
                   ?? new PairEdit(caret, 0, typed.ToString(), 1);

        var result = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.InsertText);
        return result.Insert(edit.Start + edit.CaretOffset, "|");
    }

    private static string Backspace(string textWithCaret)
    {
        int caret = textWithCaret.IndexOf('|');
        var text = textWithCaret.Remove(caret, 1);

        var edit = DelimiterPairing.OnBackspace(text, caret)
                   ?? (caret > 0 ? new PairEdit(caret - 1, 1, string.Empty, 0) : new PairEdit(caret, 0, string.Empty, 0));

        var result = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.InsertText);
        return result.Insert(edit.Start + edit.CaretOffset, "|");
    }

    // ── Opener pairs ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData('(', "()")]
    [InlineData('[', "[]")]
    [InlineData('\'', "''")]
    public void Opener_AtEndOfText_Pairs(char typed, string expected)
        => Assert.Equal("select " + expected.Insert(1, "|"), Type("select |", typed));

    [Fact]
    public void Opener_BeforeWhitespaceOrCloser_Pairs()
    {
        Assert.Equal("f(|) x", Type("f| x", '('));
        Assert.Equal("f(g(|))", Type("f(g|)", '('));
    }

    [Fact]
    public void Opener_BeforeExistingWord_DoesNotPair()
    {
        // Typing '(' at "|abc" means "wrap this", not "open an empty pair" — an inserted ')' would land
        // in front of the word and be deleted (Rule 0).
        Assert.Equal("(|abc", Type("|abc", '('));
        Assert.Equal("'|abc", Type("|abc", '\''));
    }

    // ── Closer types through ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Closer_WhenAlreadyThere_TypesThrough()
    {
        Assert.Equal("f()|", Type("f(|)", ')'));
        Assert.Equal("a[1]|", Type("a[1|]", ']'));
        Assert.Equal("'abc'|", Type("'abc|'", '\''));
    }

    [Fact]
    public void Closer_NestedCalls_TypeThroughEachLevel()
    {
        var once = Type("f(g(x|))", ')');
        Assert.Equal("f(g(x)|)", once);
        Assert.Equal("f(g(x))|", Type(once, ')'));
    }

    [Fact]
    public void Closer_WhenNotAlreadyThere_IsInsertedNormally()
        => Assert.Equal("f(a)|", Type("f(a|", ')'));

    // ── Literals and comments are text, not code ─────────────────────────────────────────────

    [Fact]
    public void Opener_InsideStringLiteral_DoesNotPair()
    {
        Assert.Equal("x = 'a(|b'", Type("x = 'a|b'", '('));
        Assert.Equal("x = 'a[|b'", Type("x = 'a|b'", '['));
    }

    [Fact]
    public void Opener_InsideComment_DoesNotPair()
    {
        Assert.Equal("-- note (|\nselect 1", Type("-- note |\nselect 1", '('));
        Assert.Equal("/* note (| */\nselect 1", Type("/* note | */\nselect 1", '('));
    }

    // The quote-parity rule: a literal the developer hasn't closed yet still contains the caret, so the
    // closing quote they are about to type must not pair into a THIRD quote.
    [Fact]
    public void Quote_ClosingAnUnterminatedLiteral_DoesNotPair()
        => Assert.Equal("x = 'abc'|", Type("x = 'abc|", '\''));

    // `'it''` is three quotes — still an OPEN literal, even though its last character is a quote. Parity
    // gets this right where "does it end with a quote?" would not.
    [Fact]
    public void Quote_AfterEscapedQuoteInsideLiteral_DoesNotPair()
        => Assert.Equal("x = 'it''s'|", Type("x = 'it''s|", '\''));

    [Fact]
    public void Paren_AfterCompleteLiteral_PairsNormally()
        => Assert.Equal("x = 'abc' + f(|)", Type("x = 'abc' + f|", '('));

    // ── Smart backspace ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("f(|)", "f|")]
    [InlineData("a[|]", "a|")]
    [InlineData("x = '|'", "x = |")]
    public void Backspace_OnEmptyPair_RemovesBoth(string before, string after)
        => Assert.Equal(after, Backspace(before));

    [Fact]
    public void Backspace_OnNonEmptyPair_DeletesOneCharNormally()
        => Assert.Equal("f(a|)", Backspace("f(ab|)"));

    [Fact]
    public void Backspace_NotBetweenAPair_IsOrdinary()
        => Assert.Equal("selec|", Backspace("select|"));

    [Fact]
    public void OutOfRange_IsNull()
    {
        Assert.Null(DelimiterPairing.OnCharacterTyped("abc", 99, '('));
        Assert.Null(DelimiterPairing.OnBackspace("abc", 0));
        Assert.Null(DelimiterPairing.OnBackspace("", 0));
    }
}
