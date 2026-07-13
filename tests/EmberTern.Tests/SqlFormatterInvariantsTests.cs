using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Tests for the Etap-3 AST-based formatter (<see cref="SqlFormatter"/>). The existing
/// <c>SqlFormatterTests</c> + <c>PsqlFormatterTests</c> remain the byte-for-byte regression gate for
/// the default style; this suite adds the guarantees that matter most for a feature that MODIFIES
/// user code:
/// <list type="bullet">
/// <item>determinism / idempotency — <c>Format(Format(x)) == Format(x)</c> over a broad corpus;</item>
/// <item>§0 Paramount Law — never lose information: every significant token and every comment in the
/// input survives in the output (nothing dropped, reordered, or mangled);</item>
/// <item>statement-kind coverage (SELECT/INSERT/UPDATE/DELETE/MERGE/EXECUTE BLOCK/CREATE
/// PROCEDURE/FUNCTION/TRIGGER/DDL/comments/incomplete/erroneous/unusual);</item>
/// <item>the AST safety valve — an unrecognised statement is preserved verbatim.</item>
/// </list>
/// </summary>
public class SqlFormatterInvariantsTests
{
    // A broad, representative corpus exercising every statement kind + edge cases (comments,
    // incomplete input, erroneous input, unusual fragments, multi-statement scripts).
    public static IEnumerable<object[]> Corpus() => new[]
    {
        // SELECT / DML
        new object[] { "SELECT A, B FROM T WHERE X = 1 AND Y = 2 ORDER BY A" },
        new object[] { "select n.id, count(p.amount) from nagl n join pozycje p on p.id_nagl = n.id group by n.id having count(*) > 1" },
        new object[] { "WITH c AS (SELECT id FROM t) SELECT * FROM c WHERE id IN (1, 2, 3)" },
        new object[] { "INSERT INTO T (A, B, C) VALUES (1, 'x', :p)" },
        new object[] { "INSERT INTO T (A, B) SELECT X, Y FROM S WHERE Z = 1" },
        new object[] { "UPDATE T SET A = 1, B = 'y' WHERE ID = 10" },
        new object[] { "UPDATE OR INSERT INTO T (A, B) VALUES (1, 2) MATCHING (A)" },
        new object[] { "DELETE FROM T WHERE X IN (1, 2, 3)" },
        new object[] { "MERGE INTO T USING S ON T.ID = S.ID WHEN MATCHED THEN UPDATE SET T.V = S.V WHEN NOT MATCHED THEN INSERT (ID, V) VALUES (S.ID, S.V)" },
        // Execution / PSQL
        new object[] { "EXECUTE PROCEDURE MY_PROC(1, 'two', :three)" },
        new object[] { "EXECUTE BLOCK RETURNS (R INTEGER) AS BEGIN R = 1; SUSPEND; END" },
        new object[] { "CREATE OR ALTER PROCEDURE P (A INTEGER) RETURNS (V INTEGER) AS DECLARE VARIABLE T INTEGER; BEGIN V = case when A > 0 then 1 else 0 end; SUSPEND; END" },
        new object[] { "CREATE OR ALTER FUNCTION F (A INTEGER) RETURNS INTEGER AS BEGIN RETURN A * 2; END" },
        new object[] { "CREATE OR ALTER TRIGGER TR FOR NAGL ACTIVE BEFORE INSERT POSITION 0 AS BEGIN NEW.ID = GEN_ID(G, 1); END" },
        new object[] { "begin x = 1; if (a = 1) then b = 2; else b = 3; end" },
        new object[] { "declare variable x integer; declare variable y varchar(5); begin x = 1; end" },
        // DDL
        new object[] { "CREATE TABLE T (ID INTEGER NOT NULL, NAME VARCHAR(50), PRIMARY KEY (ID))" },
        new object[] { "CREATE OR ALTER VIEW V (A, B) AS SELECT X.A, X.B FROM T X" },
        new object[] { "ALTER TABLE T ADD CONSTRAINT FK1 FOREIGN KEY (PID) REFERENCES P (ID)" },
        new object[] { "DROP INDEX IX_T_NAME" },
        new object[] { "COMMENT ON TABLE T IS 'a table'" },
        new object[] { "SET GENERATOR G TO 100" },
        new object[] { "GRANT SELECT ON T TO PUBLIC" },
        // Comments
        new object[] { "SELECT a -- trailing comment\nFROM t" },
        new object[] { "SELECT /* inline */ a FROM t" },
        new object[] { "begin x = 1; -- note\n y = 2; end" },
        new object[] { "/* leading */ SELECT 1 FROM RDB$DATABASE" },
        // Literals / identifiers preserved
        new object[] { "SELECT 'It''s ok', \"From\", 0x1F, 3.14 FROM t" },
        // Incomplete / mid-typing (error tolerance)
        new object[] { "SELECT " },
        new object[] { "SELECT * FROM" },
        new object[] { "CREATE PROCEDURE P AS BEGIN" },
        // Unusual / unrecognised (RawStatement — verbatim)
        new object[] { "FROBNICATE THE WIDGET" },
        new object[] { "(SELECT 1)" },
        new object[] { "a , b , c" },
        // Multi-statement
        new object[] { "SELECT 1 FROM T; DELETE FROM U; INSERT INTO V VALUES (1)" },
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Format_IsIdempotent(string sql)
    {
        var once = SqlFormatter.Format(sql);
        var twice = SqlFormatter.Format(once);
        Assert.Equal(once, twice);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Format_NeverLosesSignificantTokens(string sql)
    {
        // §0: the sequence of significant tokens (normalised: case-insensitive for unquoted words +
        // parameters; exact for quoted identifiers / strings / numbers / punctuation) must be
        // identical before and after formatting. Catches any dropped, added, reordered, or mangled
        // token.
        Assert.Equal(TokenSignature(sql), TokenSignature(SqlFormatter.Format(sql)));
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Format_NeverLosesComments(string sql)
    {
        // §0: every comment survives (line comments compared trimmed of trailing whitespace, block
        // comments exact), in order.
        Assert.Equal(Comments(sql), Comments(SqlFormatter.Format(sql)));
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Format_NeverThrows(string sql)
    {
        var ex = Record.Exception(() => SqlFormatter.Format(sql));
        Assert.Null(ex);
    }

    // ── Statement-kind coverage (spot assertions) ──────────────────────────────────────────────

    [Fact]
    public void Select_BreaksClausesAndConjunctions()
    {
        Assert.Equal(
            "select a, b\nfrom t\nwhere x = 1\n  and y = 2\norder by a",
            SqlFormatter.Format("SELECT a, b FROM t WHERE x = 1 AND y = 2 ORDER BY a"));
    }

    [Fact]
    public void Insert_LowercasesAndSpaces()
    {
        // §P8 INSERT layout: "insert into t (…)" then "values (…)" on its own line; short lists inline.
        Assert.Equal(
            "insert into t (a, b)\nvalues (1, 'X')",
            SqlFormatter.Format("INSERT INTO T (A, B) VALUES (1, 'X')"));
    }

    [Fact]
    public void Update_And_Delete()
    {
        // Default style: WHERE/FROM are clause breaks; SET (the UPDATE assignment clause) is not
        // broken in Etap 3 (a deeper-clause improvement for a later etap). Parity with the shipped style.
        Assert.Equal("update t set a = 1\nwhere id = 2", SqlFormatter.Format("UPDATE t SET a = 1 WHERE id = 2"));
        Assert.Equal("delete\nfrom t\nwhere id = 2", SqlFormatter.Format("DELETE FROM t WHERE id = 2"));
    }

    [Fact]
    public void CreateProcedure_HeaderVerbatim_BodyStructured()
    {
        var outp = SqlFormatter.Format(
            "CREATE OR ALTER PROCEDURE P\nRETURNS (R INTEGER)\nAS\nBEGIN R = 0; SUSPEND; END");
        Assert.StartsWith("CREATE OR ALTER PROCEDURE P", outp); // header kept verbatim (not lowercased)
        Assert.Contains("\nbegin\n", outp);
        Assert.Contains("  r = 0;", outp);
        Assert.Contains("  suspend;", outp);
        Assert.EndsWith("end", outp);
    }

    [Fact]
    public void ExecuteBlock_HeaderKept_BodyStructured()
    {
        var outp = SqlFormatter.Format(
            "EXECUTE BLOCK RETURNS (R INTEGER) AS BEGIN R = 1; SUSPEND; END");
        Assert.StartsWith("EXECUTE BLOCK RETURNS (R INTEGER) AS", outp); // header verbatim
        Assert.Contains("\nbegin\n", outp);
        Assert.Contains("  r = 1;", outp);
        Assert.Contains("  suspend;", outp);
    }

    [Fact]
    public void AnonymousBlock_BareBody_IsStructured()
    {
        // The procedure/function/trigger body editor holds a bare BEGIN…END — it must be formatted,
        // not emitted verbatim as a RawStatement.
        var outp = SqlFormatter.Format("begin x = 1; y = 2; end");
        Assert.Equal("begin\n  x = 1;\n  y = 2;\nend", outp);
    }

    [Fact]
    public void AnonymousBlock_DeclareLedBody_IsStructured()
    {
        var outp = SqlFormatter.Format("declare variable x integer; begin x = 1; end");
        Assert.Equal("declare variable x integer;\nbegin\n  x = 1;\nend", outp);
    }

    // ── §0 safety valve ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RawStatement_IsVerbatim()
    {
        Assert.Equal("FROBNICATE THE WIDGET", SqlFormatter.Format("FROBNICATE THE WIDGET"));
        Assert.Equal("a , b , c", SqlFormatter.Format("a , b , c"));
    }

    [Fact]
    public void RawStatement_KeepsLeadingComment()
    {
        var outp = SqlFormatter.Format("/* keep me */ FROBNICATE X");
        Assert.Contains("/* keep me */", outp);
        Assert.Contains("FROBNICATE X", outp);
    }

    [Fact]
    public void TrailingComment_IsPreserved()
    {
        var outp = SqlFormatter.Format("SELECT 1 FROM T\n-- trailing note");
        Assert.Contains("-- trailing note", outp);
        Assert.StartsWith("select 1", outp);
    }

    [Fact]
    public void CommentOnly_IsPreserved()
    {
        Assert.Equal("-- just a comment", SqlFormatter.Format("-- just a comment"));
    }

    [Fact]
    public void Empty_And_Whitespace()
    {
        Assert.Equal(string.Empty, SqlFormatter.Format(string.Empty));
        Assert.Equal(string.Empty, SqlFormatter.Format("   \t  \n "));
    }

    // ── Multi-statement ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultiStatement_EachFormatted_JoinedByNewline()
    {
        Assert.Equal(
            "select 1\nfrom t;\ndelete\nfrom u",
            SqlFormatter.Format("SELECT 1 FROM T; DELETE FROM U"));
    }

    // ── §0 helpers ────────────────────────────────────────────────────────────────────────────

    private static string TokenSignature(string sql)
    {
        var sb = new StringBuilder();
        foreach (var t in SqlLexer.Tokenize(sql))
        {
            if (t.Kind == TokenKind.EndOfFile) continue;
            sb.Append(t.Kind switch
            {
                // Unquoted words + parameter names are case-insensitive (the formatter lowercases them).
                TokenKind.Keyword or TokenKind.Identifier or TokenKind.Parameter => t.Text.ToUpperInvariant(),
                // Everything else must survive exactly.
                _ => t.Text,
            });
            sb.Append(''); // token separator
        }
        return sb.ToString();
    }

    private static List<string> Comments(string sql)
    {
        var result = new List<string>();
        foreach (var t in SqlLexer.Tokenize(sql))
        {
            foreach (var tr in t.LeadingTrivia)
            {
                if (tr.Kind == TriviaKind.LineComment) result.Add(tr.Text.TrimEnd());
                else if (tr.Kind == TriviaKind.BlockComment) result.Add(tr.Text);
            }
        }
        return result;
    }
}
