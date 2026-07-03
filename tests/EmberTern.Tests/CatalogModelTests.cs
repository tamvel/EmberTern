using System.Collections.Generic;
using EmberTern.Core.Performance;
using Xunit;

namespace EmberTern.Tests;

public class CatalogModelTests
{
    private static IndexModel PlainIndex(params string[] columns)
        => new() { Name = "IX", Columns = columns };

    [Fact]
    public void LeadingColumn_IsFirstSegment_NullWhenExpressionOnly()
    {
        Assert.Equal("ID", PlainIndex("ID", "NAME").LeadingColumn);
        Assert.Null(new IndexModel { Name = "IXE", Expression = "UPPER(NAME)" }.LeadingColumn);
    }

    [Fact]
    public void CoversLeading_MatchesLeadingColumnCaseInsensitively()
    {
        var ix = PlainIndex("ID_NAGL", "DATA");
        Assert.True(ix.CoversLeading("id_nagl"));
        Assert.True(ix.CoversLeading("ID_NAGL"));
        Assert.False(ix.CoversLeading("DATA"));   // not the leading segment
    }

    [Fact]
    public void CoversLeading_FalseForInactiveOrExpressionIndex()
    {
        Assert.False(new IndexModel { Name = "IX", Columns = new[] { "ID" }, IsInactive = true }.CoversLeading("ID"));
        Assert.False(new IndexModel { Name = "IXE", Expression = "UPPER(NAME)" }.CoversLeading("NAME"));
    }

    [Fact]
    public void ExpressionAndPartialFlags()
    {
        Assert.True(new IndexModel { Name = "IXE", Expression = "UPPER(NAME)" }.IsExpression);
        Assert.True(new IndexModel { Name = "IXP", Columns = new[] { "ID" }, Condition = "ID > 0" }.IsPartial);
        Assert.False(PlainIndex("ID").IsExpression);
        Assert.False(PlainIndex("ID").IsPartial);
    }

    [Fact]
    public void ForTable_IsCaseInsensitive_AndEmptyModelReturnsNull()
    {
        var model = new CatalogModel { Tables = new List<TableCatalogInfo> { new() { Table = "NAGL" } } };
        Assert.NotNull(model.ForTable("nagl"));
        Assert.Null(model.ForTable("MISSING"));
        Assert.Null(CatalogModel.Empty.ForTable("NAGL"));
    }
}
