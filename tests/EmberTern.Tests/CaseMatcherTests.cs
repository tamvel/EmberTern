using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

public class CaseMatcherTests
{
    [Fact]
    public void AllLowercasePrefix_LowercasesCandidate()
    {
        Assert.Equal("nagl_table", CaseMatcher.Match("nagl", "NAGL_TABLE"));
    }

    [Fact]
    public void AllUppercasePrefix_UppercasesCandidate()
    {
        Assert.Equal("NAGL_TABLE", CaseMatcher.Match("NAGL", "nagl_table"));
    }

    [Fact]
    public void MixedCasePrefix_PreservesCandidate()
    {
        Assert.Equal("NAGL_TABLE", CaseMatcher.Match("Nagl", "NAGL_TABLE"));
    }

    [Fact]
    public void EmptyPrefix_PreservesCandidate()
    {
        Assert.Equal("NAGL_TABLE", CaseMatcher.Match("", "NAGL_TABLE"));
    }

    [Fact]
    public void NullPrefix_PreservesCandidate()
    {
        Assert.Equal("NAGL_TABLE", CaseMatcher.Match(null, "NAGL_TABLE"));
    }

    [Fact]
    public void DigitsAndUnderscoresOnly_PreservesCandidate()
    {
        // No letter-case signal → keep the catalog form.
        Assert.Equal("NAGL_TABLE", CaseMatcher.Match("_1", "NAGL_TABLE"));
    }

    [Fact]
    public void LowerWithDigitsAndUnderscores_LowercasesCandidate()
    {
        Assert.Equal("nagl_table", CaseMatcher.Match("n_1", "NAGL_TABLE"));
    }

    [Fact]
    public void UpperWithDigitsAndUnderscores_UppercasesCandidate()
    {
        Assert.Equal("NAGL_TABLE", CaseMatcher.Match("N_1", "nagl_table"));
    }

    [Fact]
    public void EmptyCandidate_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CaseMatcher.Match("anything", string.Empty));
    }

    [Fact]
    public void SingleLowerLetter_LowercasesCandidate()
    {
        Assert.Equal("select", CaseMatcher.Match("s", "SELECT"));
    }

    [Fact]
    public void SingleUpperLetter_UppercasesCandidate()
    {
        Assert.Equal("SELECT", CaseMatcher.Match("S", "select"));
    }
}
