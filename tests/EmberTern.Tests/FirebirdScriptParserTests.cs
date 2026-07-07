using System.Linq;
using EmberTern.Core.Scripting;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins <see cref="FirebirdScriptParser"/> — which wraps the driver's offline
/// <c>FbScript.Parse()</c>. No live database: FbScript splits + classifies purely from text.
/// </summary>
public class FirebirdScriptParserTests
{
    private static readonly FirebirdScriptParser Parser = new();

    [Fact]
    public void Parse_SetTermScript_SplitsAndClassifies()
    {
        const string script =
            "SET TERM ^ ;\n" +
            "CREATE OR ALTER PROCEDURE P1 (A INTEGER) RETURNS (R INTEGER) AS\n" +
            "BEGIN\n  R = A + 1;\n  SUSPEND;\nEND^\n" +
            "SET TERM ; ^\n" +
            "UPDATE T SET X = 1 WHERE ID = 5;\n" +
            "COMMENT ON PROCEDURE P1 IS 'hi';\n" +
            "INSERT INTO T (A) VALUES ('a;b');\n";

        var statements = Parser.Parse(script);

        Assert.Equal(4, statements.Count);
        Assert.Equal(ScriptStatementKind.Ddl, statements[0].Kind);          // CREATE OR ALTER PROCEDURE
        Assert.Equal(ScriptStatementKind.Dml, statements[1].Kind);          // UPDATE
        Assert.Equal(ScriptStatementKind.Ddl, statements[2].Kind);          // COMMENT ON
        Assert.Equal(ScriptStatementKind.Dml, statements[3].Kind);          // INSERT

        // PSQL body kept whole (internal semicolons not split).
        Assert.Contains("BEGIN", statements[0].Text);
        Assert.Contains("SUSPEND", statements[0].Text);
        // Semicolon inside a string literal preserved, not treated as a terminator.
        Assert.Contains("'a;b'", statements[3].Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t  \n")]
    public void Parse_EmptyOrWhitespace_ReturnsEmpty(string script)
        => Assert.Empty(Parser.Parse(script));

    [Fact]
    public void Parse_PlainMultiStatement_SplitsOnSemicolonWithLocatedOffsets()
    {
        const string script = "SELECT 1 FROM RDB$DATABASE;\nSELECT 2 FROM RDB$DATABASE;";

        var statements = Parser.Parse(script);

        Assert.Equal(2, statements.Count);
        Assert.All(statements, s => Assert.Equal(ScriptStatementKind.Select, s.Kind));
        // Each located offset points at the exact source substring for that statement.
        foreach (var s in statements)
        {
            Assert.True(s.HasSourceRange);
            Assert.Equal(s.Text, script.Substring(s.SourceOffset, s.SourceLength));
        }
        // Second statement is located AFTER the first (forward cursor).
        Assert.True(statements[1].SourceOffset > statements[0].SourceOffset);
    }

    [Theory]
    [InlineData("COMMIT;")]
    [InlineData("ROLLBACK;")]
    public void Parse_TransactionControl_IsDetected(string script)
    {
        var statements = Parser.Parse(script);
        Assert.Single(statements);
        Assert.Equal(ScriptStatementKind.TransactionControl, statements[0].Kind);
    }

    [Fact]
    public void Parse_ExecuteBlock_KeptWholeAndClassified()
    {
        const string script =
            "SET TERM ^ ;\n" +
            "EXECUTE BLOCK RETURNS (X INTEGER) AS BEGIN X = 1; SUSPEND; END^\n" +
            "SET TERM ; ^\n";

        var statements = Parser.Parse(script);

        Assert.Single(statements);
        Assert.Equal(ScriptStatementKind.ExecuteBlock, statements[0].Kind);
        Assert.Contains("SUSPEND", statements[0].Text);
    }
}
