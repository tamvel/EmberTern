using EmberTern.Core.Sql.Language.Constructs;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The pure construct-expansion edit (App applies it verbatim): the replaced span, the casing decision
/// (match what the developer typed), and the resulting caret offset. Pure Core.
/// </summary>
public class ConstructExpansionTests
{
    private static ExpansionEdit Build(string text)
    {
        var m = LanguageConstructResolver.Resolve(text, text.Length);
        Assert.NotNull(m);
        return ConstructExpansion.For(text, text.Length, m!);
    }

    [Fact]
    public void Lowercase_TypedPrefix_LowercasesExpansion()
    {
        var e = Build("if");
        Assert.Equal(0, e.Start);
        Assert.Equal(2, e.Length);              // replaces "if"
        Assert.Equal("if () then", e.InsertText);
        Assert.Equal(4, e.CaretOffset);          // inside the parens
    }

    [Fact]
    public void Uppercase_TypedPrefix_UppercasesExpansion()
    {
        var e = Build("IF");
        Assert.Equal("IF () THEN", e.InsertText);
        Assert.Equal(4, e.CaretOffset);
    }

    [Fact]
    public void MultiWord_ReplacesTypedSpan_AndCases()
    {
        var e = Build("select * from t gro");
        Assert.Equal("group by ", e.InsertText);        // lowercase like the typed text
        Assert.Equal(3, e.Length);                       // replaces "gro"
        Assert.Equal("select * from t ".Length, e.Start);
        Assert.Equal(9, e.CaretOffset);                  // end of "group by "
    }

    [Fact]
    public void CaretOffset_UnaffectedByCasing()
    {
        // Upper vs lower must keep the same caret offset (casing never changes length).
        var lower = ConstructExpansion.For("if", 2, LanguageConstructResolver.Resolve("if", 2)!);
        var upper = ConstructExpansion.For("IF", 2, LanguageConstructResolver.Resolve("IF", 2)!);
        Assert.Equal(lower.CaretOffset, upper.CaretOffset);
    }
}
