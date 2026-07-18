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

    // ── D6a: FOR SELECT INTO targets + AS CURSOR name (additive AST overlay) ──────────────────────────

    private static ForSelectStatement For(string sql)
        => Assert.IsType<ForSelectStatement>(Body(sql).Statements.Single());

    [Fact]
    public void ForSelect_IntoTargets_ColonForm_ExtractedAndFolded()
    {
        var loop = For("begin for select id, val from t where owner = :p into :a, :b do suspend; end");
        Assert.Equal(new[] { "A", "B" }, loop.IntoTargets);
        Assert.Null(loop.CursorName);
    }

    [Fact]
    public void ForSelect_IntoTargets_BareForm_ExtractedAndFolded()
    {
        var loop = For("begin for select id, val from t into a, b do suspend; end");
        Assert.Equal(new[] { "A", "B" }, loop.IntoTargets);
    }

    [Fact]
    public void ForSelect_AsCursor_NameExtracted_NoInto()
    {
        var loop = For("begin for select id from t as cursor c do suspend; end");
        Assert.Empty(loop.IntoTargets);
        Assert.Equal("C", loop.CursorName);
    }

    [Fact]
    public void ForSelect_IntoAndCursor_BothExtracted_EitherOrder()
    {
        var a = For("begin for select id from t into :x as cursor c do suspend; end");
        Assert.Equal(new[] { "X" }, a.IntoTargets);
        Assert.Equal("C", a.CursorName);

        var b = For("begin for select id from t as cursor c into :x do suspend; end");
        Assert.Equal(new[] { "X" }, b.IntoTargets);
        Assert.Equal("C", b.CursorName);
    }

    [Fact]
    public void ForSelect_SubqueryInWhere_IntoNotLeakedFromSubquery()
    {
        // A subquery's own (would-be) INTO/columns must not leak: the depth-0 INTO is the loop's.
        var loop = For("begin for select id from t where id in (select x from u) into :a do suspend; end");
        Assert.Equal(new[] { "A" }, loop.IntoTargets);
    }

    [Fact]
    public void ForSelect_NoInto_NoCursor_Empty()
    {
        var loop = For("begin for select id from t do suspend; end");
        Assert.Empty(loop.IntoTargets);
        Assert.Null(loop.CursorName);
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

    // ── Stage X / D8: EXECUTE PROCEDURE arguments + RETURNING_VALUES (additive AST overlay) ──────────

    private static ExecuteProcedureStatement Call(string sql)
        => Assert.IsType<ExecuteProcedureStatement>(Body(sql).Statements.Single());

    [Fact]
    public void ExecuteProcedure_NoArgs_NoReturning_HasEmptyLists()
    {
        var call = Call("begin execute procedure p; end");
        Assert.Equal("P", call.ProcedureName);
        Assert.Empty(call.Arguments);
        Assert.Empty(call.ReturningTargets);
    }

    [Fact]
    public void ExecuteProcedure_ParenthesizedArguments_AreSpans()
    {
        const string sql = "begin execute procedure p(:a, :b + 1); end";
        var call = Call(sql);
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal(":a", sql.Substring(call.Arguments[0].Start, call.Arguments[0].Length));
        Assert.Equal(":b + 1", sql.Substring(call.Arguments[1].Start, call.Arguments[1].Length));
        Assert.Empty(call.ReturningTargets);
    }

    [Fact]
    public void ExecuteProcedure_BareArguments_AndReturningValues_Folded()
    {
        const string sql = "begin execute procedure p :a, :b returning_values :x, :y; end";
        var call = Call(sql);
        Assert.Equal(new[] { ":a", ":b" }, call.Arguments.Select(a => sql.Substring(a.Start, a.Length)));
        Assert.Equal(new[] { "X", "Y" }, call.ReturningTargets);
    }

    [Fact]
    public void ExecuteProcedure_ParenthesizedReturningValues_NoArgs()
    {
        var call = Call("begin execute procedure p returning_values (:x); end");
        Assert.Empty(call.Arguments);
        Assert.Equal(new[] { "X" }, call.ReturningTargets);
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

    // ── Stage X / P1: WHEN … DO exception handlers ─────────────────────────────────────────────

    [Fact]
    public void WhenAny_Handler_IsParsed_StatementStaysAStatement()
    {
        const string sql = "begin insert into t values (1); when any do exception e; end";
        var body = Body(sql);
        // The preceding INSERT stays a (reused DSQL) statement; the handler is peeled into Handlers.
        Assert.IsType<InsertStatement>(body.Statements.Single());
        var handler = Assert.Single(body.Handlers);
        var cond = Assert.Single(handler.Conditions);
        Assert.Equal(WhenHandlerKind.Any, cond.Kind);
        Assert.Null(cond.ExceptionName);
        Assert.Equal(PsqlLeafKind.Exception, Assert.IsType<PsqlLeafStatement>(handler.Body).Kind);
        AssertSpansMapToSource(sql, body);
    }

    [Fact]
    public void WhenExceptionName_ExposesTheName()
    {
        const string sql = "begin x = 1; when exception my_exc do x = 2; end";
        var handler = Assert.Single(Body(sql).Handlers);
        var cond = Assert.Single(handler.Conditions);
        Assert.Equal(WhenHandlerKind.ExceptionName, cond.Kind);
        Assert.Equal("MY_EXC", cond.ExceptionName);
    }

    [Fact]
    public void WhenGdsCode_SqlCode_SqlState_Kinds()
    {
        const string sql = "begin x = 1; "
                         + "when gdscode grant_obj_notfound do x = 2; "
                         + "when sqlcode -803 do x = 3; "
                         + "when sqlstate '23000' do x = 4; end";
        var kinds = Body(sql).Handlers.Select(h => Assert.Single(h.Conditions).Kind).ToArray();
        Assert.Equal(new[] { WhenHandlerKind.GdsCode, WhenHandlerKind.SqlCode, WhenHandlerKind.SqlState }, kinds);
    }

    [Fact]
    public void MultiConditionWhen_KeepsEveryConditionInDeclarationOrder()
    {
        // Firebird allows a comma-separated condition list sharing one DO body (decision 3, refined).
        const string sql = "begin x = 1; when gdscode a, gdscode b, exception c do begin exit; end end";
        var handler = Assert.Single(Body(sql).Handlers);
        Assert.Equal(
            new[] { WhenHandlerKind.GdsCode, WhenHandlerKind.GdsCode, WhenHandlerKind.ExceptionName },
            handler.Conditions.Select(c => c.Kind).ToArray());
        Assert.Equal("C", handler.Conditions[2].ExceptionName);
        Assert.IsType<BlockStatement>(handler.Body);
        AssertSpansMapToSource(sql, Body(sql));
    }

    [Fact]
    public void MultipleHandlerClauses_AreInDeclarationOrder()
    {
        const string sql = "begin x = 1; when exception a do x = 2; when any do x = 3; end";
        var handlers = Body(sql).Handlers;
        Assert.Equal(2, handlers.Count);
        Assert.Equal(WhenHandlerKind.ExceptionName, Assert.Single(handlers[0].Conditions).Kind);
        Assert.Equal(WhenHandlerKind.Any, Assert.Single(handlers[1].Conditions).Kind);
    }

    [Fact]
    public void HandlerBodyCanBeABlock()
    {
        const string sql = "begin x = 1; when any do begin a = 1; b = 2; end end";
        var body = Body(sql);
        var handler = Assert.Single(body.Handlers);
        var block = Assert.IsType<BlockStatement>(handler.Body);
        Assert.Equal(2, block.Statements.Count);
        AssertSpansMapToSource(sql, body);
    }

    [Fact]
    public void MalformedWhen_NoDo_FallsBackToOtherLeaf_NotAHandler()
    {
        const string sql = "begin x = 1; when any exception e; end"; // no DO → unrecognised shape
        var body = Body(sql);
        Assert.Empty(body.Handlers);
        Assert.Contains(body.Statements.OfType<PsqlLeafStatement>(), s => s.Kind == PsqlLeafKind.Other);
        Assert.Equal(sql, SqlParser.Parse(sql).Root.ToSourceString()); // §0 — lossless
    }

    [Fact]
    public void MalformedWhen_EmptyConditionList_IsNotAHandler()
    {
        // WHEN immediately followed by DO — no conditions → the whole clause falls back to a lossless
        // leaf (never a handler). Its leaf Kind is incidental (ClassifyLeaf sees the '=' and calls it an
        // assignment); what matters is that it is NOT peeled into Handlers and nothing is dropped.
        const string sql = "begin x = 1; when do x = 2; end";
        var body = Body(sql);
        Assert.Empty(body.Handlers);
        Assert.Contains(body.Statements, s => sql.Substring(s.Start, s.Length).StartsWith("when", System.StringComparison.OrdinalIgnoreCase));
        Assert.Equal(sql, SqlParser.Parse(sql).Root.ToSourceString());
    }

    [Fact]
    public void UnrecognisedConditionKeyword_FallsBackToOtherLeaf()
    {
        const string sql = "begin x = 1; when frobnicate do x = 2; end"; // not a condition form
        var body = Body(sql);
        Assert.Empty(body.Handlers);
        Assert.Equal(sql, SqlParser.Parse(sql).Root.ToSourceString());
    }

    [Fact]
    public void Handler_IsPresentInDdlRoutineBody()
    {
        const string sql = "create procedure p as begin insert into t values (1); when any do exception e; end";
        var ddl = Assert.IsType<DdlStatement>(SqlParser.Parse(sql).Root.Statements[0]);
        var handler = Assert.Single(ddl.Body!.Handlers);
        Assert.Equal(WhenHandlerKind.Any, Assert.Single(handler.Conditions).Kind);
        AssertSpansMapToSource(sql, ddl.Body);
    }

    // ── Stage X / D9 seam (a): local DECLARE PROCEDURE/FUNCTION sub-routines ────────────────────

    private static BlockStatement RoutineBody(string sql)
    {
        var ddl = Assert.IsType<DdlStatement>(SqlParser.Parse(sql).Root.Statements[0]);
        Assert.NotNull(ddl.Body);
        return ddl.Body!;
    }

    [Fact]
    public void LocalProcedure_IsASubroutineDeclaration_OutOfStatementsAndDeclarations()
    {
        const string sql = "create procedure outer_p (n integer) returns (r integer) as "
                         + "declare procedure local_p (a integer) returns (o integer) as "
                         + "begin o = a * 2; end "
                         + "begin execute procedure local_p(n) returning_values r; end";
        var body = RoutineBody(sql);

        var sub = Assert.Single(body.LocalRoutines);
        Assert.Equal(SubroutineKind.Procedure, sub.Kind);
        Assert.Equal("LOCAL_P", sub.Name);
        Assert.NotNull(sub.Body);
        // Its body holds its own assignment — the header did not leak into it.
        Assert.Equal(PsqlLeafKind.Assignment, Assert.IsType<PsqlLeafStatement>(sub.Body!.Statements.Single()).Kind);
        // A sub-routine is neither a variable declaration nor a body statement.
        Assert.Empty(body.Declarations);
        Assert.IsType<ExecuteProcedureStatement>(body.Statements.Single());
        AssertSpansMapToSource(sql, body);
    }

    [Fact]
    public void LocalFunction_IsASubroutineDeclaration()
    {
        const string sql = "create procedure p as "
                         + "declare function f (a integer) returns integer as begin return a + 1; end "
                         + "begin end";
        var sub = Assert.Single(RoutineBody(sql).LocalRoutines);
        Assert.Equal(SubroutineKind.Function, sub.Kind);
        Assert.Equal("F", sub.Name);
        Assert.Equal(PsqlLeafKind.Return, Assert.IsType<PsqlLeafStatement>(sub.Body!.Statements.Single()).Kind);
    }

    [Fact]
    public void LocalRoutine_WithOwnLocalVariable_HeaderNotTruncatedAtDeclareSemicolon()
    {
        // The sub-routine's own "declare variable tmp integer;" ends in ';' — the header boundary must be the
        // top-level AS, not that ';', or the body (and the local variable) would be lost.
        const string sql = "create procedure p as "
                         + "declare procedure sp (a integer) returns (o integer) as "
                         + "declare variable tmp integer; "
                         + "begin tmp = a * 2; o = tmp; end "
                         + "begin end";
        var sub = Assert.Single(RoutineBody(sql).LocalRoutines);
        Assert.NotNull(sub.Body);
        var tmp = Assert.IsType<DeclareVariableStatement>(sub.Body!.Declarations.Single());
        Assert.Equal("TMP", tmp.Name);
        Assert.Equal(2, sub.Body.Statements.Count);
    }

    [Fact]
    public void LocalRoutine_ForwardDeclaration_HasNoBody()
    {
        // A forward declaration (";", no body) enables mutual recursion; the real definition supplies the body.
        const string sql = "create procedure p as "
                         + "declare procedure sp (a integer) returns (o integer); "
                         + "declare procedure sp (a integer) returns (o integer) as begin o = a; end "
                         + "begin end";
        var routines = RoutineBody(sql).LocalRoutines;
        Assert.Equal(2, routines.Count);
        Assert.Null(routines[0].Body);    // forward declaration
        Assert.NotNull(routines[1].Body); // the real definition
    }

    [Fact]
    public void DeclarationsAndLocalRoutines_InterleaveInSourceOrder()
    {
        const string sql = "create procedure p as "
                         + "declare variable v1 integer; "
                         + "declare procedure sp as begin end "
                         + "declare variable v2 integer; "
                         + "begin end";
        var body = RoutineBody(sql);
        Assert.Equal(2, body.Declarations.Count);
        Assert.Single(body.LocalRoutines);
        // Children (declarations + local routines merged) must be in non-decreasing source order.
        int prev = int.MinValue;
        foreach (var c in body.Children) { Assert.True(c.Start >= prev, "children out of source order"); prev = c.Start; }
        AssertSpansMapToSource(sql, body);
    }

    [Fact]
    public void LocalRoutine_RoundTripsByteForByte()
    {
        const string sql = "create procedure p as "
                         + "declare procedure sp (a integer) returns (o integer) as begin o = a * 2; end "
                         + "begin execute procedure sp(1) returning_values r; end";
        Assert.Equal(sql, SqlParser.Parse(sql).Root.ToSourceString());
    }
}
