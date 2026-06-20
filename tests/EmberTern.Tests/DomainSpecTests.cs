using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

// DomainSpec parses BaseType/Size/Scale from the formatted Type string so the rich
// picker columns always match the displayed type. NotNull/Charset come from the reader.
public class DomainSpecTests
{
    [Fact]
    public void TwoArgCtor_DefaultsNotNullFalseCharsetNull()
    {
        var d = new DomainSpec("T_ID", "INTEGER");
        Assert.False(d.NotNull);
        Assert.Null(d.Charset);
    }

    [Theory]
    [InlineData("INTEGER", "INTEGER", null, null)]
    [InlineData("VARCHAR(20)", "VARCHAR", 20, null)]
    [InlineData("NUMERIC(15,2)", "NUMERIC", 15, 2)]
    [InlineData("CHAR(7)", "CHAR", 7, null)]
    [InlineData("BLOB SUB_TYPE 1", "BLOB", null, null)]
    [InlineData("TIMESTAMP", "TIMESTAMP", null, null)]
    public void Parses_BaseType_Size_Scale(string type, string baseType, int? size, int? scale)
    {
        var d = new DomainSpec("D", type);
        Assert.Equal(baseType, d.BaseType);
        Assert.Equal(size, d.Size);
        Assert.Equal(scale, d.Scale);
    }

    [Fact]
    public void CarriesNotNullAndCharset()
    {
        var d = new DomainSpec("T_KOD", "VARCHAR(20)", NotNull: true, Charset: "WIN1250");
        Assert.True(d.NotNull);
        Assert.Equal("WIN1250", d.Charset);
        Assert.Equal("VARCHAR", d.BaseType);
        Assert.Equal(20, d.Size);
    }
}
