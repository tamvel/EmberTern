using System.Linq;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

// Structured Easy-mode model: ProcedureBodySplitter.Split (Source → model) and
// DdlGenerator.BuildProcedureBody (model → Source) must round-trip with no
// information loss for variables / cursors / subprograms / executable body.
public class ProcedureBodyModelTests
{
    // ─── Split: declaration section parsing ───────────────────────────────

    [Fact]
    public void Split_Variables_ParsedWithTypeNotNullDefault()
    {
        var model = ProcedureBodySplitter.Split(
            "DECLARE VARIABLE X INTEGER;\nDECLARE VARIABLE S VARCHAR(50) NOT NULL = 'a';\nBEGIN X = 1; END");

        Assert.Equal(2, model.Variables.Count);
        Assert.Equal("X", model.Variables[0].Name);
        Assert.Equal("INTEGER", model.Variables[0].TypeText);
        Assert.False(model.Variables[0].NotNull);
        Assert.Null(model.Variables[0].Default);

        Assert.Equal("S", model.Variables[1].Name);
        Assert.Equal("VARCHAR(50)", model.Variables[1].TypeText);
        Assert.True(model.Variables[1].NotNull);
        Assert.Equal("'a'", model.Variables[1].Default);
    }

    [Fact]
    public void Split_Fb3Variable_NoVariableKeyword()
    {
        var model = ProcedureBodySplitter.Split("DECLARE N NUMERIC(18,4);\nBEGIN N = 0; END");
        var v = Assert.Single(model.Variables);
        Assert.Equal("N", v.Name);
        Assert.Equal("NUMERIC(18,4)", v.TypeText);
    }

    [Fact]
    public void Split_Cursor_KeptVerbatim()
    {
        var model = ProcedureBodySplitter.Split(
            "DECLARE C CURSOR FOR (SELECT ID FROM T);\nBEGIN OPEN C; END");
        var c = Assert.Single(model.Cursors);
        Assert.Equal("C", c.Name);
        Assert.Contains("CURSOR FOR (SELECT ID FROM T)", c.Declaration);
        Assert.StartsWith("DECLARE", c.Declaration);
        Assert.Empty(model.Variables);
    }

    [Fact]
    public void Split_Subprogram_KeptVerbatim()
    {
        var model = ProcedureBodySplitter.Split(
            "DECLARE PROCEDURE SUB (P INTEGER) AS BEGIN P = P + 1; END\nBEGIN X = 1; END");
        var sp = Assert.Single(model.Subprograms);
        Assert.Equal("SUB", sp.Name);
        Assert.Equal("PROCEDURE", sp.Kind);
        Assert.Contains("DECLARE PROCEDURE SUB", sp.Declaration);
        Assert.Contains("P = P + 1;", sp.Declaration);
    }

    [Fact]
    public void Split_ExecutableBody_FromBeginToEnd()
    {
        var model = ProcedureBodySplitter.Split("DECLARE VARIABLE X INTEGER;\nBEGIN\n  X = 1;\n  SUSPEND;\nEND");
        Assert.StartsWith("BEGIN", model.ExecutableBody);
        Assert.EndsWith("END", model.ExecutableBody);
        Assert.Contains("SUSPEND;", model.ExecutableBody);
    }

    [Fact]
    public void Split_NoDeclarations_AllBodyIsExecutable()
    {
        var model = ProcedureBodySplitter.Split("BEGIN X = 1; END");
        Assert.Empty(model.Variables);
        Assert.Empty(model.Cursors);
        Assert.Empty(model.Subprograms);
        Assert.Equal("BEGIN X = 1; END", model.ExecutableBody);
    }

    // ─── Build: model → body ───────────────────────────────────────────────

    [Fact]
    public void Build_EmitsDeclareThenBody()
    {
        var model = new ProcedureBodyModel { ExecutableBody = "BEGIN\n  X = 1;\nEND" };
        model.Variables.Add(new ProcedureVariable { Name = "X", TypeText = "INTEGER" });

        var body = DdlGenerator.BuildProcedureBody(model);
        Assert.Contains("DECLARE VARIABLE X INTEGER;", body);
        Assert.Contains("BEGIN", body);
        Assert.True(body.IndexOf("DECLARE", System.StringComparison.Ordinal)
                    < body.IndexOf("BEGIN", System.StringComparison.Ordinal));
    }

    // ─── Round-trip: model is preserved across Build → Split ───────────────

    [Fact]
    public void RoundTrip_ModelPreserved_AfterBuildAndReSplit()
    {
        var original = new ProcedureBodyModel { ExecutableBody = "BEGIN\n  SUSPEND;\nEND" };
        original.Variables.Add(new ProcedureVariable { Name = "X", TypeText = "INTEGER" });
        original.Variables.Add(new ProcedureVariable { Name = "S", TypeText = "VARCHAR(10)", NotNull = true, Default = "''" });
        original.Cursors.Add(new ProcedureCursor { Name = "C", Declaration = "DECLARE C CURSOR FOR (SELECT 1 FROM RDB$DATABASE);" });
        original.Subprograms.Add(new ProcedureSubprogram { Name = "SUB", Kind = "PROCEDURE", Declaration = "DECLARE PROCEDURE SUB AS BEGIN END" });

        var rebuilt = ProcedureBodySplitter.Split(DdlGenerator.BuildProcedureBody(original));

        AssertModelsEqual(original, rebuilt);
    }

    [Fact]
    public void RoundTrip_BodyText_IsIdempotent()
    {
        var body = "DECLARE VARIABLE X INTEGER;\nDECLARE C CURSOR FOR (SELECT 1 FROM RDB$DATABASE);\nBEGIN\n  OPEN C;\n  SUSPEND;\nEND";
        var once = DdlGenerator.BuildProcedureBody(ProcedureBodySplitter.Split(body));
        var twice = DdlGenerator.BuildProcedureBody(ProcedureBodySplitter.Split(once));
        Assert.Equal(once, twice);
    }

    // ─── Full Source ↔ Easy ↔ Source via signature parser + splitter ───────

    [Fact]
    public void FullProcedure_SourceToEasyToSource_PreservesParts()
    {
        var src =
            "CREATE OR ALTER PROCEDURE P (A INTEGER)\nRETURNS (R INTEGER)\nAS\n"
            + "DECLARE VARIABLE T INTEGER;\nBEGIN\n  T = A;\n  R = T;\n  SUSPEND;\nEND";

        // Source → Easy
        var sig = ProcedureSignatureParser.Parse(src);
        Assert.True(sig.Success);
        var model = ProcedureBodySplitter.Split(sig.Body);

        // Easy → Source
        var rebuiltBody = DdlGenerator.BuildProcedureBody(model);
        var rebuilt = DdlGenerator.BuildCreateOrAlterProcedure(sig.Name!, sig.Inputs, sig.Outputs, rebuiltBody);

        // Source → Easy again — every part identical.
        var sig2 = ProcedureSignatureParser.Parse(rebuilt);
        Assert.True(sig2.Success);
        Assert.Equal(sig.Name, sig2.Name);
        Assert.Equal(sig.Inputs.Select(p => (p.Name, p.TypeText)), sig2.Inputs.Select(p => (p.Name, p.TypeText)));
        Assert.Equal(sig.Outputs.Select(p => (p.Name, p.TypeText)), sig2.Outputs.Select(p => (p.Name, p.TypeText)));
        AssertModelsEqual(model, ProcedureBodySplitter.Split(sig2.Body));
    }

    private static void AssertModelsEqual(ProcedureBodyModel a, ProcedureBodyModel b)
    {
        Assert.Equal(
            a.Variables.Select(v => (v.Name, v.TypeText, v.NotNull, v.Default)),
            b.Variables.Select(v => (v.Name, v.TypeText, v.NotNull, v.Default)));
        Assert.Equal(a.Cursors.Select(c => c.Name), b.Cursors.Select(c => c.Name));
        Assert.Equal(a.Subprograms.Select(s => (s.Name, s.Kind)), b.Subprograms.Select(s => (s.Name, s.Kind)));
        Assert.Equal(Norm(a.ExecutableBody), Norm(b.ExecutableBody));
    }

    private static string Norm(string s) => string.Join(' ', s.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
}
