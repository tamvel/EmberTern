using EmberTern.Core.Search;
using Xunit;

namespace EmberTern.Tests;

public class SearchTextMatchTests
{
    [Theory]
    [InlineData("SELECT ID_NAGL FROM NAGL", "nagl", false, false, true)]
    [InlineData("SELECT ID_NAGL FROM NAGL", "nagl", true, false, false)] // case-sensitive miss
    [InlineData("SELECT ID_NAGL FROM NAGL", "NAGL", true, false, true)]
    [InlineData("", "x", false, false, false)]
    [InlineData("abc", "", false, false, false)]
    public void Contains_Substring(string text, string term, bool cs, bool ww, bool expected)
        => Assert.Equal(expected, SearchTextMatch.Contains(text, term, cs, ww));

    [Theory]
    [InlineData("ID_NAGL", "nagl", true)]     // whole word: NAGL bounded by _ / end → ident chars → NOT whole word
    [InlineData("ID NAGL X", "nagl", true)]   // bounded by spaces → whole word
    [InlineData("NAGLOWEK", "nagl", true)]    // substring, not whole word
    public void Contains_WholeWord_Semantics(string text, string term, bool _)
    {
        // Whole word only matches when bounded by non-identifier chars.
        bool ww = SearchTextMatch.Contains(text, term, caseSensitive: false, wholeWord: true);
        bool sub = SearchTextMatch.Contains(text, term, caseSensitive: false, wholeWord: false);
        Assert.True(sub); // substring always matches these
        Assert.Equal(text == "ID NAGL X", ww); // only the space-bounded one is a whole word
    }

    [Fact]
    public void CountOccurrences_CaseInsensitive_CountsEveryLetter()
        => Assert.Equal(8, SearchTextMatch.CountOccurrences("aaa AAA aXa", "a", caseSensitive: false)); // 3+3+2

    [Fact]
    public void CountOccurrences_CaseSensitive_OnlyExactCase()
        => Assert.Equal(1, SearchTextMatch.CountOccurrences("Foo foo FOO", "foo", caseSensitive: true)); // only middle "foo"

    [Fact]
    public void CountOccurrences_Multichar_NonOverlapping()
        => Assert.Equal(2, SearchTextMatch.CountOccurrences("abcabcab", "abc", caseSensitive: true));

    [Fact]
    public void CountOccurrences_Overlap_IsNonOverlapping()
        => Assert.Equal(2, SearchTextMatch.CountOccurrences("aaaa", "aa", caseSensitive: true));

    [Fact]
    public void CountOccurrences_EmptyInputs_Zero()
    {
        Assert.Equal(0, SearchTextMatch.CountOccurrences("", "x", false));
        Assert.Equal(0, SearchTextMatch.CountOccurrences("x", "", false));
        Assert.Equal(0, SearchTextMatch.CountOccurrences(null, "x", false));
    }

    [Fact]
    public void CountOccurrences_WholeWord_CountsOnlyBoundedHits()
        // "nagl " and "nagl." are whole-word bounded; "NAGL_X" is not (trailing '_').
        => Assert.Equal(2, SearchTextMatch.CountOccurrences("nagl NAGL_X nagl.", "nagl", caseSensitive: false, wholeWord: true));
}
