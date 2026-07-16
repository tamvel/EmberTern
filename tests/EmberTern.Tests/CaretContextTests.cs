using AvaloniaEdit.Document;
using EmberTern.App.Completion;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins that the editor's bounded, document-based caret scan (<see cref="CaretContext"/>,
/// used per-keystroke instead of materializing the whole editor text) produces
/// <b>exactly</b> the same result as the Core string helpers it mirrors. This is the
/// anti-regression guarantee for Etap 0: moving off <c>_editor.Text</c> must not change
/// what completion sees. Driven through <see cref="StringTextSource"/> so no window is
/// needed.
/// </summary>
public class CaretContextTests
{
    private static void AssertWordMatches(string text, int caret)
    {
        var expected = SqlCompletionContext.GetCurrentWord(text, caret);
        var actual = CaretContext.GetCurrentWord(new StringTextSource(text), caret);
        Assert.Equal(expected.Start, actual.Start);
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected.Text, actual.Text);
    }

    private static void AssertDotMatches(string text, int caret)
    {
        var expected = SqlCompletionContext.GetDotContext(text, caret);
        var actual = CaretContext.GetDotContext(new StringTextSource(text), caret);
        Assert.Equal(expected.HasValue, actual.HasValue);
        if (expected.HasValue && actual.HasValue)
        {
            Assert.Equal(expected.Value.Qualifier, actual.Value.Qualifier);
            Assert.Equal(expected.Value.PrefixStart, actual.Value.PrefixStart);
            Assert.Equal(expected.Value.PrefixLength, actual.Value.PrefixLength);
            Assert.Equal(expected.Value.Prefix, actual.Value.Prefix);
        }
    }

    [Theory]
    [InlineData("SELECT col FROM t", 10)]   // caret after "col"
    [InlineData("SELECT col FROM t", 6)]    // caret after "SELECT"
    [InlineData("SELECT col FROM t", 7)]    // caret at start of "col" (empty word)
    [InlineData("  ", 1)]                    // whitespace only
    [InlineData("", 0)]                      // empty document
    [InlineData("UPDATE nagl SET x=1", 11)] // caret after "nagl"
    [InlineData("a_b1 c", 4)]                // underscores + digits in identifier
    public void GetCurrentWord_MatchesCoreHelper(string text, int caret)
        => AssertWordMatches(text, caret);

    [Theory]
    [InlineData("SELECT n.id FROM nagl n", 9)]   // "n.i|d" → after "i"
    [InlineData("SELECT n. FROM nagl n", 9)]      // "n.|" empty prefix
    [InlineData("WHERE p.id_nagl = 1", 12)]       // "p.id_nagl|"
    [InlineData("SELECT x FROM t", 8)]            // no dot → null
    [InlineData("SELECT . x", 8)]                 // dot with no qualifier → null
    public void GetDotContext_MatchesCoreHelper(string text, int caret)
        => AssertDotMatches(text, caret);

    [Fact]
    public void GetDotContext_QualifierUppercased_LikeCore()
    {
        // "from nagl n" then "n.i" — qualifier must uppercase to "N" as Core does.
        const string text = "select n.i from nagl n";
        var dot = CaretContext.GetDotContext(new StringTextSource(text), 10);
        Assert.True(dot.HasValue);
        Assert.Equal("N", dot!.Value.Qualifier);
        Assert.Equal("i", dot.Value.Prefix);
    }

    [Fact]
    public void GetCurrentWord_ReturnsDocumentAbsoluteOffsets()
    {
        // Offsets must be absolute in the document, not relative to any scan window.
        const string text = "aaaaaa bbbbbb cccccc";
        var word = CaretContext.GetCurrentWord(new StringTextSource(text), 17); // inside "cccccc"
        Assert.Equal(14, word.Start);
        Assert.Equal(3, word.Length);
        Assert.Equal("ccc", word.Text);
    }
}
