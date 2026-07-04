using EmberTern.Core.Trace;
using Xunit;

namespace EmberTern.Tests;

/// <summary>The V1.2 SQL operation classifier — the leading-keyword read that turns a generic
/// "Statement" into "SELECT"/"UPDATE"/… for the Event column and the operation filter.</summary>
public class TraceSqlOperationClassifierTests
{
    [Theory]
    [InlineData("SELECT * FROM NAGL", TraceSqlOperation.Select)]
    [InlineData("  select 1 from rdb$database", TraceSqlOperation.Select)]
    [InlineData("WITH x AS (SELECT 1) SELECT * FROM x", TraceSqlOperation.Select)]
    [InlineData("INSERT INTO T (A) VALUES (1)", TraceSqlOperation.Insert)]
    [InlineData("UPDATE NAGL SET STATUS = 5 WHERE ID = ?", TraceSqlOperation.Update)]
    [InlineData("DELETE FROM T WHERE ID = 1", TraceSqlOperation.Delete)]
    [InlineData("MERGE INTO T USING S ON (T.ID = S.ID)", TraceSqlOperation.Merge)]
    [InlineData("EXECUTE PROCEDURE P(1)", TraceSqlOperation.Execute)]
    [InlineData("EXECUTE BLOCK AS BEGIN END", TraceSqlOperation.Execute)]
    [InlineData("CREATE TABLE T (A INTEGER)", TraceSqlOperation.Ddl)]
    [InlineData("ALTER TABLE T ADD B INTEGER", TraceSqlOperation.Ddl)]
    [InlineData("DROP TABLE T", TraceSqlOperation.Ddl)]
    [InlineData("GRANT SELECT ON T TO U", TraceSqlOperation.Ddl)]
    [InlineData("SET GENERATOR G TO 1", TraceSqlOperation.Ddl)]
    [InlineData("SAVEPOINT S", TraceSqlOperation.Other)]
    public void Classify_ReadsLeadingKeyword(string sql, TraceSqlOperation expected)
        => Assert.Equal(expected, TraceSqlOperationClassifier.Classify(sql));

    [Fact]
    public void Classify_SkipsLeadingCommentsAndSeparators()
    {
        Assert.Equal(TraceSqlOperation.Update,
            TraceSqlOperationClassifier.Classify("-- a comment\nUPDATE T SET A = 1"));
        Assert.Equal(TraceSqlOperation.Select,
            TraceSqlOperationClassifier.Classify("/* block */ SELECT 1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_BlankIsNone(string? sql)
        => Assert.Equal(TraceSqlOperation.None, TraceSqlOperationClassifier.Classify(sql));

    [Fact]
    public void Label_IsUpperCase_AndDdlIsShort()
    {
        Assert.Equal("UPDATE", TraceSqlOperationClassifier.Label(TraceSqlOperation.Update));
        Assert.Equal("DDL", TraceSqlOperationClassifier.Label(TraceSqlOperation.Ddl));
        Assert.Equal(string.Empty, TraceSqlOperationClassifier.Label(TraceSqlOperation.None));
    }
}
