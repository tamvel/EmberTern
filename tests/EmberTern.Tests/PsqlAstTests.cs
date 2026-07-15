using System.Linq;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap 6.9 / B1 — the PSQL body tree the parser now attaches to an <see cref="AnonymousBlockStatement"/>
/// (the BEGIN…END shape the routine BODY editors hold). Asserts the structural shape (blocks, IF/WHILE/FOR
/// control flow, executable-leaf spans) and that every executable node's span maps back to real source —
/// the property the future debugger's breakpoints/stepping depend on. Round-trip byte-identity and tree
/// well-formedness over the corpus are covered by <see cref="StructuralAstDifferentialTests"/>; these tests
/// pin the concrete node shapes.
/// </summary>
public class PsqlAstTests
{
    private static BlockStatement Body(string sql)
    {
        var root = SqlParser.Parse(sql).Root;
        var block = Assert.IsType<AnonymousBlockStatement>(root.Statements[0]);
        Assert.NotNull(block.Body);
        return block.Body!;
    }

    // The span of every executable node must map to a non-empty slice of the exact source (a breakpoint
    // target). Verified structurally: a node's span lies within its source and re-slices to real text.
    private static void AssertSpansMapToSource(string sql, SqlNode node)
    {
        foreach (var d in node.DescendantNodesAndSelf())
        {
            Assert.True(d.Start >= 0 && d.End <= sql.Length, $"span [{d.Start},{d.End}) outside source len {sql.Length}");
            if (d is IExecutableStatement)
            {
                Assert.True(d.Length > 0, $"{d.GetType().Name} has an empty span");
                Assert.False(string.IsNullOrWhiteSpace(sql.Substring(d.Start, d.Length)), "executable span is whitespace");
            }
        }
    }

    [Fact]
    public void SimpleBlock_LeafAssignments()
    {
        const string sql = "begin a = 1; b = 2; end";
        var body = Body(sql);
        Assert.Equal(2, body.Statements.Count);
        Assert.All(body.Statements, s => Assert.Equal(PsqlLeafKind.Assignment, Assert.IsType<PsqlLeafStatement>(s).Kind));
        // First leaf span covers "a = 1;"
        var first = body.Statements[0];
        Assert.Equal("a = 1;", sql.Substring(first.Start, first.Length));
        AssertSpansMapToSource(sql, body);
    }

    [Fact]
    public void IfElse_ProducesIfStatementWithBothBranches()
    {
        const string sql = "begin if (x = 1) then y = 2; else y = 3; end";
        var body = Body(sql);
        var iff = Assert.IsType<IfStatement>(body.Statements.Single());
        Assert.IsType<PsqlLeafStatement>(iff.Then);
        Assert.IsType<PsqlLeafStatement>(iff.Else);
        Assert.IsAssignableFrom<IExecutableStatement>(iff); // the IF header is a step point
        AssertSpansMapToSource(sql, body);
    }

    [Fact]
    public void IfWithoutElse_HasNoElseBranch()
    {
        const string sql = "begin if (x = 1) then y = 2; end";
        var iff = Assert.IsType<IfStatement>(Body(sql).Statements.Single());
        Assert.NotNull(iff.Then);
        Assert.Null(iff.Else);
    }

    [Fact]
    public void While_ProducesWhileStatementWithBody()
    {
        const string sql = "begin while (x < 10) do x = x + 1; end";
        var body = Body(sql);
        var loop = Assert.IsType<WhileStatement>(body.Statements.Single());
        Assert.IsType<PsqlLeafStatement>(loop.Body);
        AssertSpansMapToSource(sql, body);
    }

    [Fact]
    public void ForSelect_ProducesForStatementWithBody()
    {
        const string sql = "begin for select id from t into :i do suspend; end";
        var body = Body(sql);
        var loop = Assert.IsType<ForSelectStatement>(body.Statements.Single());
        var leaf = Assert.IsType<PsqlLeafStatement>(loop.Body);
        Assert.Equal(PsqlLeafKind.Suspend, leaf.Kind);
        AssertSpansMapToSource(sql, body);
    }

    [Fact]
    public void NestedBegin_ProducesNestedBlock()
    {
        const string sql = "begin begin a = 1; end end";
        var body = Body(sql);
        var inner = Assert.IsType<BlockStatement>(body.Statements.Single());
        Assert.Equal(PsqlLeafKind.Assignment, Assert.IsType<PsqlLeafStatement>(inner.Statements.Single()).Kind);
        AssertSpansMapToSource(sql, body);
    }

    [Fact]
    public void IfBranchCanBeABlock()
    {
        const string sql = "begin if (x = 1) then begin a = 1; b = 2; end end";
        var iff = Assert.IsType<IfStatement>(Body(sql).Statements.Single());
        var thenBlock = Assert.IsType<BlockStatement>(iff.Then);
        Assert.Equal(2, thenBlock.Statements.Count);
    }

    [Fact]
    public void LeafKinds_AreClassified()
    {
        // PSQL-only leaves keep a PsqlLeafKind; the assignment + EXCEPTION here are such leaves.
        var body = Body("begin v = 1; suspend; exception e; end");
        var kinds = body.Statements.OfType<PsqlLeafStatement>().Select(s => s.Kind).ToArray();
        Assert.Equal(new[] { PsqlLeafKind.Assignment, PsqlLeafKind.Suspend, PsqlLeafKind.Exception }, kinds);
    }

    [Fact]
    public void EmbeddedDsqlStatements_AreReusedTopLevelNodes_WithQueryStructure()
    {
        // B5: an embedded SELECT/INSERT/UPDATE/DELETE/MERGE/EXECUTE inside a body is the SAME node the
        // top-level parser builds — carrying its query structure — not a PsqlLeafStatement.
        var body = Body("begin insert into t (a) select x from s; update t set a = 1; execute procedure p; end");
        var types = body.Statements.Select(s => s.GetType().Name).ToArray();
        Assert.Equal(new[] { nameof(InsertStatement), nameof(UpdateStatement), nameof(ExecuteProcedureStatement) }, types);
        // The embedded INSERT's source query is a real QueryNode (the §12 #1 residual is closed).
        var insert = Assert.IsType<InsertStatement>(body.Statements[0]);
        Assert.IsType<SelectQuery>(insert.SourceQuery);
        // The reused nodes are debugger step points.
        Assert.All(body.Statements, s => Assert.IsAssignableFrom<IExecutableStatement>(s));
    }

    [Fact]
    public void MalformedIf_WithoutThen_StaysLosslessLeaf()
    {
        // No top-level THEN → the IF falls back to a leaf (never throws, never drops tokens).
        const string sql = "begin if (x = 1) end";
        var body = Body(sql);
        Assert.NotEmpty(body.Statements);
        // Round-trip is still exact (the hard §0 guarantee).
        Assert.Equal(sql, SqlParser.Parse(sql).Root.ToSourceString());
    }

    // ── B1b-prep: bodies now parsed for CREATE PROCEDURE/FUNCTION/TRIGGER + EXECUTE BLOCK ──────────

    [Fact]
    public void CreateProcedure_HasBodyWithDeclarationAndStatements()
    {
        const string sql = "create procedure p (a integer) returns (v integer) as "
                         + "declare variable t integer; begin v = 1; suspend; end";
        var ddl = Assert.IsType<DdlStatement>(SqlParser.Parse(sql).Root.Statements[0]);
        Assert.NotNull(ddl.Body);
        var decl = Assert.IsType<DeclareVariableStatement>(ddl.Body!.Declarations.Single());
        Assert.Equal("T", decl.Name);
        Assert.Equal(2, ddl.Body.Statements.Count); // v = 1;  and  suspend;
        AssertSpansMapToSource(sql, ddl.Body);
    }

    [Fact]
    public void CreateProcedure_MultipleDeclarations()
    {
        const string sql = "create procedure p as declare variable a integer; declare variable b integer; begin a = b; end";
        var ddl = Assert.IsType<DdlStatement>(SqlParser.Parse(sql).Root.Statements[0]);
        Assert.Equal(2, ddl.Body!.Declarations.Count);
        Assert.Equal(new[] { "A", "B" },
            ddl.Body.Declarations.Cast<DeclareVariableStatement>().Select(d => d.Name).ToArray());
    }

    [Fact]
    public void DeclareCursor_IsCursorNode()
    {
        const string sql = "create procedure p as declare c cursor for (select id from t); begin end";
        var ddl = Assert.IsType<DdlStatement>(SqlParser.Parse(sql).Root.Statements[0]);
        var cur = Assert.IsType<DeclareCursorStatement>(ddl.Body!.Declarations.Single());
        Assert.Equal("C", cur.Name);
    }

    [Fact]
    public void ExecuteBlock_HasBody()
    {
        const string sql = "execute block returns (r integer) as begin r = 1; suspend; end";
        var eb = Assert.IsType<ExecuteBlockStatement>(SqlParser.Parse(sql).Root.Statements[0]);
        Assert.NotNull(eb.Body);
        Assert.Equal(2, eb.Body!.Statements.Count);
        AssertSpansMapToSource(sql, eb.Body);
    }

    [Fact]
    public void CreateTrigger_HasBody()
    {
        const string sql = "create trigger tr for t before insert as begin new.id = 1; end";
        var ddl = Assert.IsType<DdlStatement>(SqlParser.Parse(sql).Root.Statements[0]);
        Assert.NotNull(ddl.Body);
        Assert.Single(ddl.Body!.Statements);
    }

    [Fact]
    public void NonPsqlDdl_AndDrop_HaveNoBody()
    {
        Assert.Null(Assert.IsType<DdlStatement>(SqlParser.Parse("create table t (id integer)").Root.Statements[0]).Body);
        Assert.Null(Assert.IsType<DdlStatement>(SqlParser.Parse("drop procedure p").Root.Statements[0]).Body);
    }

    [Fact]
    public void RoutineBody_ControlFlow_IsStructured()
    {
        const string sql = "create procedure p as begin if (a = 1) then begin b = 2; end while (c < 3) do c = c + 1; end";
        var ddl = Assert.IsType<DdlStatement>(SqlParser.Parse(sql).Root.Statements[0]);
        var kinds = ddl.Body!.Statements.Select(s => s.GetType().Name).ToArray();
        Assert.Equal(new[] { nameof(IfStatement), nameof(WhileStatement) }, kinds);
        AssertSpansMapToSource(sql, ddl.Body);
    }
}
