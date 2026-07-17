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
}
