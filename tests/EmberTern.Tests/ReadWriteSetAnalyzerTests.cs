using System.Linq;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X — Firebird Debugger, milestone D2 seam (b): the read/write-set analyzer (spec §3.5). It consumes
/// the real <see cref="SemanticModel"/> (never re-parses / re-resolves) — so these tests build a model from
/// PSQL with declared variables and assert the reads/writes it derives from the binder's references. Pure
/// Core, no server.
/// </summary>
public class ReadWriteSetAnalyzerTests
{
    // Builds the model for a whole routine and returns (model, its body statements). Uses the STRICT parse
    // via CREATE PROCEDURE — the debugger works on a whole, properly-parsed routine, kept as ONE DdlStatement
    // with its declares + body together, so bare identifiers in the body resolve against the declared scope
    // (VariableSymbols / ParameterSymbols). The editor's lenient newline-split segmentation would instead
    // break the routine apart and bind the body without its declared variables in scope.
    private static (SemanticModel Model, System.Collections.Generic.IReadOnlyList<SqlNode> Body) Build(string sql)
    {
        var model = SemanticModel.Build(SqlParser.Parse(sql).Root);
        var ddl = model.Syntax.Statements.OfType<DdlStatement>().First();
        return (model, ddl.Body!.Statements);
    }

    private const string Sql = """
        create procedure p (a integer) returns (r integer) as
        declare v integer;
        declare w integer;
        begin
          v = w + 1;
          r = v;
          if (v > 0) then
            w = v;
          insert into log (msg) values (:v);
        end
        """;

    [Fact]
    public void Assignment_Reads_ReferencedVariables_Writes_TheTarget()
    {
        var (model, body) = Build(Sql);
        var assign = body.OfType<PsqlLeafStatement>().First(s => s.Kind == PsqlLeafKind.Assignment); // v = w + 1;

        var set = ReadWriteSetAnalyzer.Analyze(assign, model);

        Assert.Equal(new[] { "V", "W" }, set.Reads.OrderBy(x => x));
        Assert.Equal(new[] { "V" }, set.Writes);   // the leftmost l-value
    }

    [Fact]
    public void Assignment_WriteTarget_IsTheLeftmostLValue_EvenWhenAlsoRead()
    {
        var (model, body) = Build(Sql);
        var rEqV = body.First(s => Sql.Substring(s.Start, s.Length).StartsWith("r =", System.StringComparison.Ordinal));

        var set = ReadWriteSetAnalyzer.Analyze(rEqV, model);

        Assert.Contains("R", set.Writes);            // R (a RETURNS param) is the target
        Assert.Single(set.Writes);
        Assert.Contains("V", set.Reads);             // V is read on the RHS
        Assert.Contains("R", set.Reads);             // the target also appears as a reference (harmless)
    }

    [Fact]
    public void IfCondition_Reads_ButWritesNothing()
    {
        var (model, body) = Build(Sql);
        var iff = body.OfType<IfStatement>().First();

        var set = ReadWriteSetAnalyzer.Analyze(iff, model);

        // The IF node spans the whole IF (condition + THEN branch). It reads V (condition + `w = v`),
        // but the analyzer never treats a control-flow node itself as writing anything.
        Assert.Contains("V", set.Reads);
        Assert.Empty(set.Writes);
    }

    [Fact]
    public void PlainDml_Writes_AreASafeSuperset_OfReads()
    {
        var (model, body) = Build(Sql);
        // insert into log (msg) values (:v); — reads V, changes no frame variable, but writes = reads
        // (a correct, chattier superset; write-back of an unchanged value is harmless).
        var insert = body.First(s => Sql.Substring(s.Start, s.Length).StartsWith("insert", System.StringComparison.OrdinalIgnoreCase));

        var set = ReadWriteSetAnalyzer.Analyze(insert, model);

        Assert.Contains("V", set.Reads);
        Assert.Equal(set.Reads.OrderBy(x => x), set.Writes.OrderBy(x => x));
    }

    [Fact]
    public void InScopeLocals_ReturnsEveryDeclaredLocal()
    {
        var (model, _) = Build(Sql);
        int offset = Sql.IndexOf("v = w + 1", System.StringComparison.Ordinal);

        var locals = ReadWriteSetAnalyzer.InScopeLocals(model, offset);

        Assert.Contains("V", locals);
        Assert.Contains("W", locals);
        Assert.Contains("R", locals); // the RETURNS parameter is an in-scope local too
    }

    // Stage X / D2 seam c: a reused SELECT … INTO surfaces NO local references — the query binder records its
    // FROM/columns, not the :colon-refs in the WHERE nor the INTO targets. So its precise read/write set is
    // empty even though it reads the WHERE param and WRITES the INTO variable. This pins the exact condition
    // the FirebirdDebugExecutor detects (empty/empty) to fall back to the §3.5 "inject all in-scope" set —
    // otherwise the INTO write-back is silently dropped (a §F divergence). If the binder later surfaces these
    // refs, this test flips and the executor's fallback simply stops firing.
    private const string SelectIntoSql = """
        create procedure p (pid integer) returns (r integer) as
        declare v_exists integer;
        begin
          select count(*) from customers where customer_id = :pid into :v_exists;
          if (v_exists = 0) then r = 0;
        end
        """;

    // ── Transitive fixpoint over the sub-routine call graph (D9 seam b Part 2) ───────────────────────

    private static (SemanticModel Model, BlockStatement Body) BuildBody(string sql)
    {
        var model = SemanticModel.Build(SqlParser.Parse(sql).Root);
        var ddl = model.Syntax.Statements.OfType<DdlStatement>().First();
        return (model, ddl.Body!);
    }

    private const string FnCaptureSql = """
        create procedure p (seed integer) returns (total integer) as
        declare variable hidden integer;
        declare function bump_hidden (delta integer) returns integer
        as
        begin
          hidden = hidden + delta;
          return hidden;
        end
        begin
          hidden = seed;
          total = bump_hidden(10);
        end
        """;

    [Fact]
    public void Fixpoint_FoldsInACalledFunctionsCapturedOuterVar_ButNotItsOwnParams()
    {
        var (model, body) = BuildBody(FnCaptureSql);
        var catalog = new SubroutineCatalog(body.LocalRoutines);
        var call = body.Statements.OfType<PsqlLeafStatement>().First(s =>
            FnCaptureSql.Substring(s.Start, s.Length).StartsWith("total =", System.StringComparison.OrdinalIgnoreCase));

        var direct = ReadWriteSetAnalyzer.Analyze(call, model);           // no catalog → direct refs only
        var folded = ReadWriteSetAnalyzer.Analyze(call, model, catalog);  // with the call-graph fixpoint

        // Direct: HIDDEN is NOT named at the call site (only the literal 10 and the target TOTAL) → dropped.
        Assert.DoesNotContain("HIDDEN", direct.Reads);
        Assert.DoesNotContain("HIDDEN", direct.Writes);
        // Folded: the fixpoint injects + returns the captured HIDDEN the callee reads+writes.
        Assert.Contains("HIDDEN", folded.Reads);
        Assert.Contains("HIDDEN", folded.Writes);
        // The callee's OWN parameter (DELTA) is out of scope at the call site → the in-scope filter drops it.
        Assert.DoesNotContain("DELTA", folded.Reads);
        Assert.DoesNotContain("DELTA", folded.Writes);
    }

    private const string TransitiveSql = """
        create procedure p (seed integer) returns (total integer) as
        declare variable hidden integer;
        declare procedure inner_p
        as
        begin
          hidden = hidden + 1;
        end
        declare procedure outer_p
        as
        begin
          execute procedure inner_p;
        end
        begin
          hidden = seed;
          execute procedure outer_p;
          total = hidden;
        end
        """;

    [Fact]
    public void Fixpoint_IsTransitive_AcrossTheSubRoutineCallGraph()
    {
        var (model, body) = BuildBody(TransitiveSql);
        var catalog = new SubroutineCatalog(body.LocalRoutines);
        var call = body.Statements.OfType<ExecuteProcedureStatement>().First(); // execute procedure outer_p

        var folded = ReadWriteSetAnalyzer.Analyze(call, model, catalog);

        // outer_p (no captures of its own) calls inner_p, which mutates the outer HIDDEN — the fixpoint reaches
        // it through the call graph, so the harness for `execute procedure outer_p` injects + returns HIDDEN.
        Assert.Contains("HIDDEN", folded.Reads);
        Assert.Contains("HIDDEN", folded.Writes);
    }

    [Fact]
    public void Fixpoint_NoCatalog_IsTheDirectSet_Unchanged()
    {
        var (model, body) = BuildBody(FnCaptureSql);
        var call = body.Statements.OfType<PsqlLeafStatement>().First(s =>
            FnCaptureSql.Substring(s.Start, s.Length).StartsWith("total =", System.StringComparison.OrdinalIgnoreCase));

        var withNull = ReadWriteSetAnalyzer.Analyze(call, model, subroutines: null);
        var withEmpty = ReadWriteSetAnalyzer.Analyze(call, model, SubroutineCatalog.Empty);

        // Null / empty catalog ⇒ exactly the direct-reference behaviour (D2–D8 unchanged).
        Assert.DoesNotContain("HIDDEN", withNull.Reads);
        Assert.DoesNotContain("HIDDEN", withEmpty.Reads);
        Assert.Equal(withNull.Reads.OrderBy(x => x), withEmpty.Reads.OrderBy(x => x));
    }

    [Fact]
    public void SelectInto_SurfacesNoLocalRefs_SoTheFallbackIsInScopeLocals()
    {
        var model = SemanticModel.Build(SqlParser.Parse(SelectIntoSql).Root);
        var body = model.Syntax.Statements.OfType<DdlStatement>().First().Body!;
        var selectInto = body.Statements.First(s =>
            SelectIntoSql.Substring(s.Start, s.Length).StartsWith("select", System.StringComparison.OrdinalIgnoreCase));

        var precise = ReadWriteSetAnalyzer.Analyze(selectInto, model);
        var fallback = ReadWriteSetAnalyzer.InScopeLocals(model, selectInto.Start);

        // The precise set is empty (the gap the executor's fallback exists for) …
        Assert.Empty(precise.Reads);
        Assert.Empty(precise.Writes);
        // … while the fallback carries the WHERE read AND the INTO write target.
        Assert.Contains("PID", fallback);
        Assert.Contains("V_EXISTS", fallback);
    }
}
