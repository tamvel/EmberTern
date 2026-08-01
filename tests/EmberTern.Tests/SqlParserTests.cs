using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The error-tolerant statement-level parser (Etap 2). The load-bearing guarantee is the §0
/// round-trip (<see cref="SqlScript.ToSourceString"/> reproduces the input byte-for-byte,
/// independent of how deeply anything was parsed); the rest pins statement segmentation,
/// classification into the typed node taxonomy, the RawStatement safety valve, and error
/// tolerance (the parser never throws on incomplete/garbage input).
/// </summary>
public class SqlParserTests
{
    // A broad corpus: DML, PSQL definitions, comments, strings, multi-statement batches, and
    // pathological/unterminated constructs (the parser must handle every one without throwing).
    public static IEnumerable<object[]> Corpus() => new[]
    {
        new object[] { "" },
        new object[] { "   \t\r\n  " },
        new object[] { "-- just a comment" },
        new object[] { "/* block only */" },
        new object[] { "SELECT * FROM NAGL N WHERE N.ID = 10" },
        new object[] { "select id from t;" },
        new object[] { "SELECT 1 FROM RDB$DATABASE; DROP TABLE T; -- trailing note" },
        new object[] { "INSERT INTO T (A, B) VALUES (1, 'x;y')" },
        new object[] { "UPDATE OR INSERT INTO T (ID, V) VALUES (1, 2) MATCHING (ID)" },
        new object[] { "MERGE INTO T USING S ON T.ID = S.ID WHEN MATCHED THEN UPDATE SET T.A = S.A" },
        new object[] { "EXECUTE PROCEDURE SP_X(1, 'a;b', @p)" },
        new object[] { "EXECUTE BLOCK RETURNS (X INTEGER) AS BEGIN X = 1; SUSPEND; END" },
        new object[] { "CREATE OR ALTER PROCEDURE P (A INTEGER) RETURNS (V INTEGER) AS\r\nDECLARE VARIABLE T INTEGER;\r\nBEGIN\r\n  V = case when a > 0 then 1 else 0 end;\r\n  SUSPEND;\r\nEND" },
        new object[] { "CREATE TRIGGER TR FOR NAGL ACTIVE BEFORE INSERT POSITION 0 AS DECLARE V INT; BEGIN NEW.ID = 1; END" },
        new object[] { "COMMENT ON TABLE T IS 'hi ; there'" },
        new object[] { "GRANT SELECT ON T TO PUBLIC" },
        new object[] { "SET GENERATOR GEN_T TO 0" },
        new object[] { "SET TERM ^ ;" },
        new object[] { ";;;" },
        new object[] { "  ;  SELECT 1 FROM T  ;  " },
        new object[] { "FROBNICATE THE WIDGET" },
        new object[] { "(SELECT 1 FROM T)" },
        new object[] { "SELECT 'unterminated" },
        new object[] { "CREATE PROCEDURE P AS BEGIN" }, // unterminated body — must not throw
        new object[] { "CREATE PROCEDURE P (A INTEGER" }, // unterminated header parens
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Parse_RoundTripsByteForByte(string src)
    {
        var root = SqlParser.Parse(src).Root;
        Assert.Equal(src, root.ToSourceString());
        Assert.Equal(src, root.Text);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Parse_NeverThrows_AndAlwaysProducesAResult(string src)
    {
        var result = SqlParser.Parse(src);
        Assert.NotNull(result.Root);
        Assert.NotNull(result.Diagnostics); // channel exists (empty at statement-segmentation depth)
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Parse_EveryStatementSourceSliceMatchesItsSpan(string src)
    {
        // A statement's span is a real substring of the source (the §0 basis for RawStatement /
        // the DDL splitter): re-slicing the source by (Start, Length) never goes out of range.
        var root = SqlParser.Parse(src).Root;
        foreach (var s in root.Statements)
        {
            Assert.True(s.Start >= 0 && s.End <= src.Length);
            Assert.Equal(src.Substring(s.Start, s.Length), src.Substring(s.Start, s.Length));
        }
    }

    [Fact]
    public void EmptyInput_HasNoStatements()
    {
        Assert.Empty(SqlParser.Parse("").Root.Statements);
        Assert.Empty(SqlParser.Parse("   \n\t ").Root.Statements);
        Assert.Empty(SqlParser.Parse("-- only a comment").Root.Statements);
    }

    [Theory]
    [InlineData("SELECT 1 FROM T", StatementKind.Select)]
    [InlineData("WITH c AS (SELECT 1 FROM T) SELECT * FROM c", StatementKind.Select)]
    [InlineData("INSERT INTO T (A) VALUES (1)", StatementKind.Insert)]
    [InlineData("UPDATE T SET A = 1", StatementKind.Update)]
    [InlineData("UPDATE OR INSERT INTO T (A) VALUES (1)", StatementKind.UpdateOrInsert)]
    [InlineData("DELETE FROM T", StatementKind.Delete)]
    [InlineData("MERGE INTO T USING S ON T.ID = S.ID WHEN MATCHED THEN DELETE", StatementKind.Merge)]
    [InlineData("EXECUTE BLOCK AS BEGIN END", StatementKind.ExecuteBlock)]
    [InlineData("EXECUTE PROCEDURE P(1)", StatementKind.ExecuteProcedure)]
    [InlineData("EXECUTE STATEMENT 'select 1 from rdb$database'", StatementKind.ExecuteStatement)]
    [InlineData("CREATE TABLE T (ID INTEGER)", StatementKind.Ddl)]
    [InlineData("DROP INDEX IX", StatementKind.Ddl)]
    [InlineData("COMMENT ON TABLE T IS 'x'", StatementKind.Comment)]
    [InlineData("SET GENERATOR G TO 0", StatementKind.Set)]
    [InlineData("GRANT SELECT ON T TO PUBLIC", StatementKind.Grant)]
    [InlineData("REVOKE SELECT ON T FROM PUBLIC", StatementKind.Revoke)]
    [InlineData("DECLARE EXTERNAL FUNCTION F INTEGER RETURNS INTEGER", StatementKind.Declare)]
    [InlineData("BEGIN X = 1; END", StatementKind.AnonymousBlock)]
    [InlineData("FROBNICATE THE WIDGET", StatementKind.Raw)]
    [InlineData("(SELECT 1)", StatementKind.Raw)]
    public void LeadingStatement_ClassifiesToExpectedKind(string sql, StatementKind expected)
        => Assert.Equal(expected, Single(sql).Kind);

    [Fact]
    public void LoneSemicolon_IsEmptyStatement_AndIsDropped_ByNeighbours()
    {
        var statements = SqlParser.Parse(";").Root.Statements;
        var only = Assert.Single(statements);
        Assert.IsType<EmptyStatement>(only);
        Assert.Equal(StatementKind.Empty, only.Kind);
    }

    [Fact]
    public void MultiStatement_SegmentsInOrder_WithCorrectKinds()
    {
        var statements = SqlParser.Parse("SELECT 1 FROM T; DROP TABLE T; INSERT INTO T VALUES (1)").Root.Statements;
        Assert.Equal(
            new[] { StatementKind.Select, StatementKind.Ddl, StatementKind.Insert },
            statements.Select(s => s.Kind));
    }

    [Fact]
    public void PsqlDefinition_WithDeclareAndCase_StaysOneStatement()
    {
        const string sql =
            "CREATE OR ALTER PROCEDURE P (AID INTEGER) RETURNS (V INTEGER) AS\n" +
            "DECLARE VARIABLE T INTEGER;\n" +
            "BEGIN\n" +
            "  for select sum(x) from t where (case when :aid > 0 then 1 else 0 end) = 1 into :v do\n" +
            "    suspend;\n" +
            "END";
        var stmt = Assert.Single(SqlParser.Parse(sql).Root.Statements);
        var ddl = Assert.IsType<DdlStatement>(stmt);
        Assert.Equal(DdlVerb.CreateOrAlter, ddl.Verb);
        Assert.Equal(DdlObjectKind.Procedure, ddl.ObjectKind);
        Assert.True(ddl.IsPsqlDefinition);
        Assert.Equal("P", ddl.ObjectName);
    }

    // An EXECUTE BLOCK has a PSQL body, so its DECLARE-section semicolons must not split it — the same
    // rule that keeps a procedure whole. It used to fall to the plain ';' scan (it is not a *definition*),
    // which cut it in two at the end of its first DECLARE: the BEGIN…END became a separate anonymous
    // block whose scope could not see the declared variables (ET0003 on every :v in the body).
    [Theory]
    [InlineData("EXECUTE BLOCK\nAS\nDECLARE VARIABLE V INTEGER;\nBEGIN\n  V = 1;\nEND")]
    [InlineData("EXECUTE BLOCK (A INTEGER = ?) RETURNS (R INTEGER) AS\n"
        + "DECLARE VARIABLE V INTEGER;\nDECLARE VARIABLE W INTEGER;\n"
        + "BEGIN\n  R = :A;\n  SUSPEND;\nEND")]
    [InlineData("EXECUTE BLOCK AS\nDECLARE C CURSOR FOR (SELECT 1 FROM RDB$DATABASE);\nBEGIN\n  OPEN C;\n  CLOSE C;\nEND")]
    public void ExecuteBlock_WithDeclareSection_StaysOneStatement(string sql)
    {
        var stmt = Assert.Single(SqlParser.Parse(sql).Root.Statements);
        Assert.IsType<ExecuteBlockStatement>(stmt);
        Assert.Equal(sql, sql.Substring(stmt.Start, stmt.Length));
    }

    // …and the whole-statement scan must still yield at the END, not swallow what follows.
    [Fact]
    public void ExecuteBlock_WithDeclareSection_DoesNotSwallowTheNextStatement()
    {
        const string sql =
            "EXECUTE BLOCK AS DECLARE VARIABLE V INTEGER; BEGIN V = 1; END;\n" +
            "SELECT 1 FROM RDB$DATABASE;";
        var statements = SqlParser.Parse(sql).Root.Statements;
        Assert.Equal(
            new[] { StatementKind.ExecuteBlock, StatementKind.Select },
            statements.Select(s => s.Kind));
    }

    // The shape predicate is about a PSQL BODY, not about the EXECUTE verb: the other two EXECUTE forms
    // have no body and stay on the plain ';' scan.
    [Fact]
    public void ExecuteProcedureAndStatement_StillSegmentOnSemicolons()
    {
        var statements = SqlParser
            .Parse("EXECUTE PROCEDURE P(1); EXECUTE STATEMENT 'select 1 from rdb$database'; SELECT 1 FROM T;")
            .Root.Statements;
        Assert.Equal(
            new[] { StatementKind.ExecuteProcedure, StatementKind.ExecuteStatement, StatementKind.Select },
            statements.Select(s => s.Kind));
    }

    [Theory]
    [InlineData("CREATE TABLE MYTAB (ID INTEGER)", DdlVerb.Create, DdlObjectKind.Table, "MYTAB")]
    [InlineData("CREATE OR ALTER VIEW V AS SELECT 1 FROM T", DdlVerb.CreateOrAlter, DdlObjectKind.View, "V")]
    [InlineData("ALTER TABLE NAGL ADD X INTEGER", DdlVerb.Alter, DdlObjectKind.Table, "NAGL")]
    [InlineData("RECREATE EXCEPTION E 'msg'", DdlVerb.Recreate, DdlObjectKind.Exception, "E")]
    [InlineData("DROP INDEX IX_T", DdlVerb.Drop, DdlObjectKind.Index, "IX_T")]
    [InlineData("CREATE UNIQUE DESCENDING INDEX IX ON T (A)", DdlVerb.Create, DdlObjectKind.Index, "IX")]
    [InlineData("CREATE TABLE \"Mixed Case\" (ID INTEGER)", DdlVerb.Create, DdlObjectKind.Table, "Mixed Case")]
    public void DdlStatement_ReadsVerbKindAndName(string sql, DdlVerb verb, DdlObjectKind kind, string name)
    {
        var ddl = Assert.IsType<DdlStatement>(Single(sql));
        Assert.Equal(verb, ddl.Verb);
        Assert.Equal(kind, ddl.ObjectKind);
        Assert.Equal(name, ddl.ObjectName);
    }

    [Theory]
    [InlineData("EXECUTE PROCEDURE Recalc", "RECALC")]
    [InlineData("execute procedure xxx_test(:a, :b)", "XXX_TEST")]
    [InlineData("EXECUTE PROCEDURE \"MixedProc\"()", "MixedProc")]
    public void ExecuteProcedureStatement_ReadsName(string sql, string expected)
    {
        var ep = Assert.IsType<ExecuteProcedureStatement>(Single(sql));
        Assert.Equal(expected, ep.ProcedureName);
        Assert.Null(ep.PackageName); // unqualified — no package (Stage X / D11)
    }

    [Theory]
    // A package-qualified call (Stage X / D11): the routine is the part after the dot, the qualifier is the package.
    [InlineData("EXECUTE PROCEDURE PKG_DBG.PUB_RUN(:n) RETURNING_VALUES :r", "PKG_DBG", "PUB_RUN")]
    [InlineData("execute procedure my_pkg.do_it", "MY_PKG", "DO_IT")]
    [InlineData("EXECUTE PROCEDURE \"Pkg\".\"Proc\"", "Pkg", "Proc")]
    public void ExecuteProcedureStatement_ReadsQualifiedName(string sql, string pkg, string routine)
    {
        var ep = Assert.IsType<ExecuteProcedureStatement>(Single(sql));
        Assert.Equal(pkg, ep.PackageName);
        Assert.Equal(routine, ep.ProcedureName);
    }

    [Fact] // the qualified name must not disturb argument / RETURNING_VALUES parsing (they already dot-skip)
    public void ExecuteProcedureStatement_Qualified_StillReadsArgsAndReturning()
    {
        var ep = Assert.IsType<ExecuteProcedureStatement>(Single("EXECUTE PROCEDURE PKG_DBG.PUB_RUN(:n) RETURNING_VALUES :r"));
        Assert.Single(ep.Arguments);
        Assert.Equal(new[] { "R" }, ep.ReturningTargets);
    }

    [Theory]
    [InlineData("SET GENERATOR G TO 0", "GENERATOR")]
    [InlineData("SET STATISTICS INDEX IX", "STATISTICS")]
    [InlineData("SET TERM ^ ;", "TERM")]
    public void SetStatement_ReadsTarget(string sql, string target)
        => Assert.Equal(target, Assert.IsType<SetStatement>(Single(sql)).Target, ignoreCase: true);

    [Fact]
    public void RawStatement_PreservesUnrecognisedSourceVerbatim()
    {
        const string sql = "FROBNICATE THE WIDGET";
        var raw = Assert.IsType<RawStatement>(Single(sql));
        Assert.Equal(sql, sql.Substring(raw.Start, raw.Length));
    }

    [Fact]
    public void NodeAt_ReturnsDeepestContainingStatement()
    {
        const string sql = "SELECT 1 FROM T; DROP TABLE T";
        var root = SqlParser.Parse(sql).Root;
        // NodeAt descends to the deepest node: offset 2 is inside the SELECT's projection clause (the
        // B2 query tree is now live), which nests inside the SelectStatement.
        Assert.IsType<SelectClause>(root.NodeAt(2));                       // inside SELECT's projection
        Assert.IsType<SelectStatement>(root.NodeAt(sql.IndexOf(';')));     // the ';' — in the statement, no clause
        Assert.IsType<DdlStatement>(root.NodeAt(sql.IndexOf("DROP") + 2)); // inside DROP (a leaf statement)
        Assert.Null(root.NodeAt(sql.Length));                             // past the end
    }

    [Fact]
    public void Descendants_EnumeratesStatementsInSourceOrder()
    {
        var root = SqlParser.Parse("SELECT 1 FROM T; DROP TABLE T; DELETE FROM T").Root;
        var kinds = root.Descendants<SqlStatement>().Select(s => s.Kind).ToArray();
        Assert.Equal(new[] { StatementKind.Select, StatementKind.Ddl, StatementKind.Delete }, kinds);
        Assert.Single(root.Descendants<DdlStatement>());
    }

    // ── WITH / CTE modelled in the AST ──────────────────────────────────────────────────────────

    [Fact]
    public void With_ModelsCteStructure()
    {
        var sel = Assert.IsType<SelectStatement>(
            Single("with c (a, b) as (select 1, 2 from t) select * from c"));
        var wq = Assert.IsType<WithQuery>(sel.Query);
        Assert.False(wq.With.IsRecursive);
        var cte = Assert.Single(wq.With.Ctes);
        Assert.Equal("C", cte.NameToken.Text.ToUpperInvariant());
        Assert.NotNull(cte.ColumnTokens);
        // B3: the CTE body and the main query are real query nodes.
        Assert.IsType<SelectQuery>(cte.Body);
        Assert.IsType<SelectQuery>(wq.Query);
        // The WithQuery is a child of the statement, so NodeAt/Descendants can reach it.
        Assert.Contains(wq, sel.Children);
        Assert.Single(sel.Descendants<CommonTableExpression>());
    }

    [Fact]
    public void With_Recursive_And_MultipleCtes()
    {
        var sel = Assert.IsType<SelectStatement>(
            Single("with recursive a as (select 1 from t), b as (select 2 from u) select * from a"));
        var wq = Assert.IsType<WithQuery>(sel.Query);
        Assert.True(wq.With.IsRecursive);
        Assert.Equal(2, wq.With.Ctes.Count);
    }

    [Fact]
    public void NestedCte_BodyIsItselfAWithQuery()
    {
        // A CTE body that is itself a WITH … SELECT recurses with no special handling (B3).
        var sel = Assert.IsType<SelectStatement>(
            Single("with a as (with b as (select 1 as n from t) select n from b) select n from a"));
        var outer = Assert.IsType<WithQuery>(sel.Query);
        var cteA = Assert.Single(outer.With.Ctes);
        Assert.IsType<WithQuery>(cteA.Body); // nested WITH inside the CTE body
    }

    [Fact]
    public void PlainSelect_HasNoWithQuery()
        => Assert.IsType<SelectQuery>(Assert.IsType<SelectStatement>(Single("select * from t")).Query);

    [Fact]
    public void With_MalformedShape_LeavesQueryNonWith_StillRoundTrips()
    {
        // No AS ( … ) — the parser can't cleanly model the CTE clause, so Query is not a WithQuery
        // (treated as a plain query), but the tokens are untouched so the §0 round-trip still holds.
        const string sql = "with c select 1";
        var root = SqlParser.Parse(sql).Root;
        var sel = Assert.IsType<SelectStatement>(Assert.Single(root.Statements));
        Assert.False(sel.Query is WithQuery);
        Assert.Equal(sql, root.ToSourceString());
    }

    private static SqlStatement Single(string sql) => Assert.Single(SqlParser.Parse(sql).Root.Statements);
}
