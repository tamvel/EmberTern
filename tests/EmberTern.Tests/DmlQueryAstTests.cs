using System.Linq;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap 6.9 / B3.1 — queries embedded in OTHER statements are now real <see cref="QueryNode"/>s: an
/// <c>INSERT … SELECT</c> source, an <c>UPDATE</c>/<c>DELETE</c>/<c>MERGE</c> embedded subquery, a
/// <c>MERGE USING (…)</c> source, a <c>CREATE VIEW … AS &lt;query&gt;</c> body, a PSQL <c>FOR SELECT</c>
/// cursor, and a <c>DECLARE … CURSOR FOR (…)</c> query. This closes the last "query as a token blob"
/// gap (editor-ast-deepening.md §12 #1) so the parser is the single structural source for every query
/// reachable from a top-level statement or a PSQL control-flow node. Round-trip byte-identity + tree
/// well-formedness over the corpus are covered by <see cref="StructuralAstDifferentialTests"/>; these
/// pin the shapes.
/// </summary>
public class DmlQueryAstTests
{
    private static SqlStatement First(string sql) => SqlParser.Parse(sql).Root.Statements[0];

    // ── INSERT ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InsertSelect_HasSourceQuery()
    {
        var ins = Assert.IsType<InsertStatement>(First("INSERT INTO t (a, b) SELECT x, y FROM s WHERE z = 1"));
        var q = Assert.IsType<SelectQuery>(ins.SourceQuery);
        Assert.Equal("s", Assert.IsType<TableReference>(q.From!.Items.Single()).NameToken!.Text);
        Assert.Contains(ins.SourceQuery!, ins.Children);
    }

    [Fact]
    public void InsertWithCteSource_SourceIsWithQuery()
    {
        var ins = Assert.IsType<InsertStatement>(First("INSERT INTO t (a) WITH c AS (SELECT id FROM u) SELECT id FROM c"));
        Assert.IsType<WithQuery>(ins.SourceQuery);
    }

    [Fact]
    public void InsertValues_HasNoSourceQuery_ButCapturesScalarSubquery()
    {
        var ins = Assert.IsType<InsertStatement>(First("INSERT INTO t (a) VALUES ((SELECT MAX(id) FROM u))"));
        Assert.Null(ins.SourceQuery);
        var scalar = Assert.Single(ins.Subqueries.OfType<ScalarSubquery>());
        Assert.IsType<SelectQuery>(scalar.Query);
    }

    [Fact]
    public void InsertSelect_SubqueryInsideSourceIsNotAlsoATopLevelStatementSubquery()
    {
        // The subquery lives inside the source query's WHERE, so it belongs to the SourceQuery tree —
        // NOT to InsertStatement.Subqueries (no double representation).
        var ins = Assert.IsType<InsertStatement>(First("INSERT INTO t (a) SELECT x FROM s WHERE z IN (SELECT k FROM u)"));
        Assert.Empty(ins.Subqueries);
        var src = Assert.IsType<SelectQuery>(ins.SourceQuery);
        Assert.Single(src.Where!.Children.OfType<ScalarSubquery>());
    }

    [Fact]
    public void InsertSelect_ReturningSubquery_IsAStatementSubquery()
    {
        var ins = Assert.IsType<InsertStatement>(First("INSERT INTO t (a) SELECT x FROM s RETURNING (SELECT c FROM w) AS r"));
        Assert.IsType<SelectQuery>(ins.SourceQuery);
        Assert.Single(ins.Subqueries.OfType<ScalarSubquery>());
    }

    // ── UPDATE / DELETE ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_CapturesSetAndWhereSubqueries()
    {
        var upd = Assert.IsType<UpdateStatement>(
            First("UPDATE t SET a = (SELECT MAX(x) FROM u) WHERE EXISTS (SELECT 1 FROM v WHERE v.k = t.id)"));
        Assert.Single(upd.Subqueries.OfType<ScalarSubquery>());
        Assert.Single(upd.Subqueries.OfType<ExistsExpression>());
    }

    [Fact]
    public void Delete_CapturesWhereSubquery()
    {
        var del = Assert.IsType<DeleteStatement>(First("DELETE FROM t WHERE x IN (SELECT y FROM u)"));
        var scalar = Assert.Single(del.Subqueries.OfType<ScalarSubquery>());
        Assert.Equal("u", Assert.IsType<TableReference>(Assert.IsType<SelectQuery>(scalar.Query).From!.Items.Single()).NameToken!.Text);
    }

    [Fact]
    public void UpdateOrInsert_CapturesValueSubquery()
    {
        var uoi = Assert.IsType<UpdateOrInsertStatement>(
            First("UPDATE OR INSERT INTO t (a, b) VALUES (1, (SELECT k FROM u)) MATCHING (a)"));
        Assert.Single(uoi.Subqueries.OfType<ScalarSubquery>());
    }

    // ── MERGE ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MergeUsingSubquery_HasSourceQuery()
    {
        var merge = Assert.IsType<MergeStatement>(First(
            "MERGE INTO t USING (SELECT id, v FROM s) src ON t.id = src.id "
            + "WHEN MATCHED THEN UPDATE SET t.v = src.v"));
        var q = Assert.IsType<SelectQuery>(merge.SourceQuery);
        Assert.Equal("s", Assert.IsType<TableReference>(q.From!.Items.Single()).NameToken!.Text);
    }

    [Fact]
    public void MergeUsingSubquery_SourceIsNotAlsoAStatementSubquery()
    {
        // The USING (SELECT …) is the SourceQuery — it must not ALSO appear in Subqueries.
        var merge = Assert.IsType<MergeStatement>(First(
            "MERGE INTO t USING (SELECT id FROM s) src ON t.id = src.id WHEN MATCHED THEN DELETE"));
        Assert.NotNull(merge.SourceQuery);
        Assert.Empty(merge.Subqueries);
    }

    [Fact]
    public void MergeUsingBareTable_HasNoSourceQuery()
    {
        var merge = Assert.IsType<MergeStatement>(First(
            "MERGE INTO t USING s ON t.id = s.id WHEN MATCHED THEN UPDATE SET t.v = s.v"));
        Assert.Null(merge.SourceQuery);
        Assert.Empty(merge.Subqueries);
    }

    [Fact]
    public void MergeOnConditionSubquery_IsCapturedOutsideTheSource()
    {
        var merge = Assert.IsType<MergeStatement>(First(
            "MERGE INTO t USING (SELECT id FROM s) src ON t.id = src.id AND t.k IN (SELECT k FROM w) "
            + "WHEN MATCHED THEN DELETE"));
        Assert.NotNull(merge.SourceQuery);
        Assert.Single(merge.Subqueries.OfType<ScalarSubquery>()); // the IN (SELECT …) in the ON clause
    }

    // ── CREATE VIEW ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateView_HasBodyQuery()
    {
        var ddl = Assert.IsType<DdlStatement>(First("CREATE VIEW v (a, b) AS SELECT x, y FROM t"));
        Assert.Equal(DdlObjectKind.View, ddl.ObjectKind);
        var q = Assert.IsType<SelectQuery>(ddl.Query);
        Assert.Equal("t", Assert.IsType<TableReference>(q.From!.Items.Single()).NameToken!.Text);
        Assert.Contains(ddl.Query!, ddl.Children);
    }

    [Fact]
    public void CreateViewWithCte_BodyIsWithQuery()
    {
        var ddl = Assert.IsType<DdlStatement>(First("CREATE OR ALTER VIEW v AS WITH c AS (SELECT id FROM t) SELECT id FROM c"));
        Assert.IsType<WithQuery>(ddl.Query);
    }

    [Fact]
    public void CreateViewSetOperation_BodyIsSetOperationQuery()
    {
        var ddl = Assert.IsType<DdlStatement>(First("RECREATE VIEW v AS SELECT a FROM t UNION SELECT a FROM u"));
        Assert.IsType<SetOperationQuery>(ddl.Query);
    }

    [Fact]
    public void DropView_HasNoBodyQuery()
    {
        var ddl = Assert.IsType<DdlStatement>(First("DROP VIEW v"));
        Assert.Null(ddl.Query);
    }

    [Fact]
    public void CreateTable_HasNoBodyQuery()
    {
        var ddl = Assert.IsType<DdlStatement>(First("CREATE TABLE t (id INTEGER NOT NULL, name VARCHAR(50))"));
        Assert.Null(ddl.Query);
    }

    // ── PSQL cursor queries ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ForSelect_CursorIsAQueryNode()
    {
        var body = Assert.IsType<AnonymousBlockStatement>(First("begin for select id, name from t where x > 0 into :i, :n do suspend; end")).Body!;
        var forStmt = Assert.IsType<ForSelectStatement>(body.Statements.Single());
        var q = Assert.IsType<SelectQuery>(forStmt.Query);
        Assert.Equal("t", Assert.IsType<TableReference>(q.From!.Items.Single()).NameToken!.Text);
        // The cursor query precedes the body in Children (source order); the body is still present.
        Assert.Same(forStmt.Query, forStmt.Children[0]);
        Assert.Same(forStmt.Body, forStmt.Children[1]);
    }

    [Fact]
    public void ForSelect_CursorStopsBeforeInto_NotSwallowingColumnAliasAs()
    {
        // A column alias's own AS must not truncate the cursor query; INTO ends it.
        var body = Assert.IsType<AnonymousBlockStatement>(First("begin for select a as n from t into :n do suspend; end")).Body!;
        var forStmt = Assert.IsType<ForSelectStatement>(body.Statements.Single());
        var q = Assert.IsType<SelectQuery>(forStmt.Query);
        Assert.NotNull(q.From); // the FROM survived — the query was not cut at "a AS"
        Assert.Equal("t", Assert.IsType<TableReference>(q.From!.Items.Single()).NameToken!.Text);
    }

    [Fact]
    public void ForExecuteStatement_HasNoCursorQuery()
    {
        var body = Assert.IsType<AnonymousBlockStatement>(First("begin for execute statement 'select 1 from rdb$database' into :i do suspend; end")).Body!;
        var forStmt = Assert.IsType<ForSelectStatement>(body.Statements.Single());
        Assert.Null(forStmt.Query);
        Assert.NotNull(forStmt.Body);
    }

    [Fact]
    public void DeclareCursor_ParenthesisedQuery_IsAQueryNode()
    {
        var ddl = Assert.IsType<DdlStatement>(First("create procedure p as declare c cursor for (select id from t where x = 1); begin open c; end"));
        var cur = Assert.IsType<DeclareCursorStatement>(ddl.Body!.Declarations.Single());
        var q = Assert.IsType<SelectQuery>(cur.Query);
        Assert.Equal("t", Assert.IsType<TableReference>(q.From!.Items.Single()).NameToken!.Text);
    }
}
