using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

public class SqlStatementClassifierTests
{
    // --- Data lane: reads, DML, procedure/block execution ---
    [Theory]
    [InlineData("SELECT * FROM CUSTOMERS")]
    [InlineData("select id from t")]
    [InlineData("WITH cte AS (SELECT 1 FROM RDB$DATABASE) SELECT * FROM cte")]
    [InlineData("INSERT INTO T (A) VALUES (1)")]
    [InlineData("UPDATE T SET A = 1 WHERE ID = 2")]
    [InlineData("DELETE FROM T WHERE ID = 2")]
    [InlineData("MERGE INTO T USING S ON T.ID = S.ID WHEN MATCHED THEN UPDATE SET T.A = S.A")]
    [InlineData("EXECUTE PROCEDURE SP_BALANCE(1)")]
    [InlineData("EXECUTE BLOCK RETURNS (X INTEGER) AS BEGIN X = 1; SUSPEND; END")]
    [InlineData("execute block as begin update t set a = 1; end")]
    public void DataStatements_ClassifyAsData(string sql)
        => Assert.Equal(StatementLane.Data, SqlStatementClassifier.Classify(sql));

    // --- Metadata lane: DDL + DCL ---
    [Theory]
    [InlineData("CREATE TABLE T (ID INTEGER)")]
    [InlineData("create or alter procedure sp as begin end")]
    [InlineData("ALTER TABLE T ADD B VARCHAR(10)")]
    [InlineData("ALTER PROCEDURE SP AS BEGIN END")]
    [InlineData("ALTER TRIGGER TR INACTIVE")]
    [InlineData("DROP INDEX IX_T")]
    [InlineData("RECREATE VIEW V AS SELECT 1 FROM RDB$DATABASE")]
    [InlineData("COMMENT ON TABLE T IS 'hi'")]
    [InlineData("DECLARE EXTERNAL FUNCTION ABS DOUBLE PRECISION RETURNS DOUBLE PRECISION")]
    [InlineData("GRANT SELECT ON T TO PUBLIC")]
    [InlineData("REVOKE SELECT ON T FROM PUBLIC")]
    [InlineData("SET GENERATOR GEN_T TO 0")]
    [InlineData("SET STATISTICS INDEX IX_T")]
    public void MetadataStatements_ClassifyAsMetadata(string sql)
        => Assert.Equal(StatementLane.Metadata, SqlStatementClassifier.Classify(sql));

    // --- Ambiguous: undecidable leading token / directives / empty ---
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("SET TERM ^ ;")]
    [InlineData("SET TRANSACTION READ COMMITTED")]
    [InlineData("FROBNICATE THE WIDGET")]
    public void UndecidableInput_ClassifiesAsAmbiguous(string? sql)
        => Assert.Equal(StatementLane.Ambiguous, SqlStatementClassifier.Classify(sql));

    [Fact]
    public void LeadingLineComment_IsSkippedBeforeClassifying()
    {
        const string sql = "-- pick everything\nSELECT * FROM T";
        Assert.Equal(StatementLane.Data, SqlStatementClassifier.Classify(sql));
    }

    [Fact]
    public void LeadingBlockComment_IsSkippedBeforeClassifying()
    {
        const string sql = "/* daily report\n   v2 */ CREATE TABLE T (ID INTEGER)";
        Assert.Equal(StatementLane.Metadata, SqlStatementClassifier.Classify(sql));
    }

    [Fact]
    public void LeadingWhitespaceAndComments_BeforeAlter_ClassifyAsMetadata()
    {
        const string sql = "\n\n   -- fix proc\r\n  ALTER PROCEDURE SP AS BEGIN END";
        Assert.Equal(StatementLane.Metadata, SqlStatementClassifier.Classify(sql));
    }

    [Fact]
    public void OnlyComments_ClassifyAsAmbiguous()
    {
        Assert.Equal(StatementLane.Ambiguous, SqlStatementClassifier.Classify("-- just a note"));
        Assert.Equal(StatementLane.Ambiguous, SqlStatementClassifier.Classify("/* nothing here */"));
    }

    [Fact]
    public void FirstStatementWins_InMultiStatementInput()
    {
        // Classification is by the leading statement only (the executor sends one
        // command per Execute). A SELECT followed by DDL is routed as Data.
        const string sql = "SELECT * FROM T; DROP TABLE T;";
        Assert.Equal(StatementLane.Data, SqlStatementClassifier.Classify(sql));
    }
}
