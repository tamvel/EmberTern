using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language.Ergonomics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Typing Ergonomics — structural auto-indent (design §3.2). Deliberately simpler than the formatter: it
/// indents the constructs written interactively (begin/end, if/then, while/do, for … do, else) and leaves
/// the formatter's parenthesis/column alignment to Alt+F. The bar it must clear is "never produce
/// indentation that obviously fights the formatter".
/// </summary>
public class AutoIndentTests
{
    /// <summary>The indent computed for the line marked `»` (the marker stands where the line begins).</summary>
    private static string Indent(string textWithMarker)
    {
        int at = textWithMarker.IndexOf('»');
        Assert.True(at >= 0, "the case must mark the line start with '»'");
        return AutoIndent.ForLine(textWithMarker.Remove(at, 1), at);
    }

    [Fact]
    public void TopLevel_IsNotIndented()
        => Assert.Equal("", Indent("»select 1"));

    [Fact]
    public void InsideBlock_IsOneLevel()
        => Assert.Equal("  ", Indent("begin\n»"));

    [Fact]
    public void NestedBlock_IsTwoLevels()
        => Assert.Equal("    ", Indent("begin\n  begin\n»"));

    [Fact]
    public void ClosingLine_BacksOutToItsOpener()
    {
        Assert.Equal("", Indent("begin\n  x = 1;\n»end"));
        Assert.Equal("  ", Indent("begin\n  begin\n    x = 1;\n»end\nend"));
    }

    // `if (x) then` ⏎ → the body statement is one level deeper, which is exactly what the formatter emits.
    [Fact]
    public void AfterThen_BodyStatementIsOneDeeper()
        => Assert.Equal("    ", Indent("begin\n  if (x = 1) then\n»"));

    [Fact]
    public void AfterDo_BodyStatementIsOneDeeper()
    {
        Assert.Equal("    ", Indent("begin\n  while (x = 1) do\n»"));
        Assert.Equal("    ", Indent("begin\n  for select a from t into :v do\n»"));
    }

    [Fact]
    public void AfterElse_BodyStatementIsOneDeeper()
        => Assert.Equal("    ", Indent("begin\n  if (x = 1) then\n    y = 2;\n  else\n»"));

    // The `then`-body ends at its `;`, so the ELSE line itself returns to the `if`'s level.
    [Fact]
    public void ElseLineItself_ReturnsToTheIfLevel()
        => Assert.Equal("  ", Indent("begin\n  if (x = 1) then\n    y = 2;\n»else"));

    [Fact]
    public void AfterAStatement_StaysAtTheBlockLevel()
        => Assert.Equal("  ", Indent("begin\n  x = 1;\n»"));

    // CASE-aware (gotchas #117/#128/#129): the CASE's END must not pop a block level.
    [Fact]
    public void CaseEnd_DoesNotPopABlockLevel()
        => Assert.Equal("  ", Indent("begin\n  x = case when a then 1 else 2 end;\n»"));

    [Fact]
    public void KeywordsInsideLiteralsAndComments_DoNotCount()
    {
        Assert.Equal("", Indent("select 'begin' from T;\n»"));
        Assert.Equal("", Indent("-- begin\n»"));
    }

    [Fact]
    public void SurplusEnd_NeverGoesNegative()
        => Assert.Equal("", Indent("begin\nend\nend\n»"));

    // The whole point of taking the unit from the formatter: what auto-indent produces for a body line is
    // exactly what the formatter produces for the same code.
    [Fact]
    public void IndentMatchesTheFormatterForTheSameCode()
    {
        const string src = "begin\n  if (x = 1) then\n    y = 2;\nend";
        Assert.Equal(src, SqlFormatter.Format(src));
        Assert.Equal("  ", Indent("begin\n»  if (x = 1) then\n    y = 2;\nend"));
        Assert.Equal("    ", Indent("begin\n  if (x = 1) then\n»    y = 2;\nend"));
        Assert.Equal("", Indent("begin\n  if (x = 1) then\n    y = 2;\n»end"));
    }

    [Fact]
    public void EmptyOrOutOfRange_IsEmpty()
    {
        Assert.Equal("", AutoIndent.ForLine("", 0));
        Assert.Equal("", AutoIndent.ForLine("begin", -1));
        Assert.Equal("", AutoIndent.ForLine("begin", 99));
    }
}
