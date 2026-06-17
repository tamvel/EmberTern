using System.Linq;
using EmberTern.App.ViewModels;
using Xunit;

namespace EmberTern.Tests;

// VM-level Source ↔ Easy ↔ Source synchronization for the structured Easy mode:
// toggling EasyMode parses the source into editable params / variables / cursors /
// subprograms / executable body and regenerates it with no information loss.
public class ProcedureEasyModeRoundTripTests
{
    private const string Src =
        "CREATE OR ALTER PROCEDURE P (A INTEGER, B VARCHAR(10))\n"
        + "RETURNS (R INTEGER)\n"
        + "AS\n"
        + "DECLARE VARIABLE T INTEGER;\n"
        + "DECLARE VARIABLE S VARCHAR(20) NOT NULL = '';\n"
        + "DECLARE C CURSOR FOR (SELECT ID FROM ITEMS);\n"
        + "DECLARE PROCEDURE SUB (P INTEGER) AS BEGIN P = P + 1; END\n"
        + "BEGIN\n  T = A;\n  R = T;\n  SUSPEND;\nEND";

    [Fact]
    public void SourceToEasy_PopulatesStructuredModel()
    {
        var vm = new ProcedureDetailTabViewModel("P") { SourceText = Src };
        vm.EasyMode = true;

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(new[] { "A", "B" }, vm.InputParams.Select(p => p.Name));
        Assert.Equal(new[] { "R" }, vm.OutputParams.Select(p => p.Name));

        Assert.Equal(2, vm.Variables.Count);
        Assert.Equal("T", vm.Variables[0].Name);
        Assert.Equal("INTEGER", vm.Variables[0].TypeText);
        Assert.Equal("S", vm.Variables[1].Name);
        Assert.Equal("VARCHAR(20)", vm.Variables[1].TypeText);
        Assert.True(vm.Variables[1].NotNull);

        Assert.Single(vm.Cursors);
        Assert.Equal("C", vm.Cursors[0].Name);
        Assert.Contains("CURSOR FOR (SELECT ID FROM ITEMS)", vm.Cursors[0].Declaration);

        Assert.Single(vm.Subprograms);
        Assert.Equal("SUB", vm.Subprograms[0].Name);
        Assert.Equal("PROCEDURE", vm.Subprograms[0].Kind);

        Assert.StartsWith("BEGIN", vm.ExecutableBody);
        Assert.Contains("SUSPEND;", vm.ExecutableBody);
    }

    [Fact]
    public void SourceToEasyToSourceToEasy_PreservesEverything()
    {
        var vm = new ProcedureDetailTabViewModel("P") { SourceText = Src };

        vm.EasyMode = true;
        var first = Snapshot(vm);

        vm.EasyMode = false;            // Easy → Source (regenerate text)
        Assert.Contains("CREATE OR ALTER PROCEDURE P", vm.SourceText);

        vm.EasyMode = true;             // Source → Easy again
        var second = Snapshot(vm);

        Assert.Equal(first, second);
    }

    [Fact]
    public void EasyToSourceToEasy_PreservesEverything()
    {
        // Build the model purely in Easy mode (no text typed into the body editor for
        // declarations — they're model elements), then round-trip through Source.
        var vm = new ProcedureDetailTabViewModel("MY_PROC");
        vm.EasyMode = true;             // empty source → guarded parse, model stays empty
        vm.ErrorMessage = null;
        vm.InputParams.Add(new ProcedureParamRowViewModel { Name = "A", TypeText = "INTEGER" });
        vm.OutputParams.Add(new ProcedureParamRowViewModel { Name = "R", TypeText = "INTEGER" });
        vm.Variables.Add(new ProcedureVariableRowViewModel { Name = "T", TypeText = "VARCHAR(10)", NotNull = true });
        vm.Cursors.Add(new ProcedureCursorRowViewModel { Declaration = "DECLARE C CURSOR FOR (SELECT 1 FROM RDB$DATABASE);" });
        vm.Subprograms.Add(new ProcedureSubprogramRowViewModel { Declaration = "DECLARE PROCEDURE SUB AS BEGIN END" });
        vm.ExecutableBody = "BEGIN\n  SUSPEND;\nEND";

        var source = vm.BuildFullSource();

        var vm2 = new ProcedureDetailTabViewModel("MY_PROC") { SourceText = source };
        vm2.EasyMode = true;

        Assert.Equal(Snapshot(vm), Snapshot(vm2));
        // The cursor/subprogram display names were derived from their declarations.
        Assert.Equal("C", vm2.Cursors[0].Name);
        Assert.Equal("SUB", vm2.Subprograms[0].Name);
    }

    [Fact]
    public void Compile_Easy_ReassemblesFromStructuredModel()
    {
        var vm = new ProcedureDetailTabViewModel("P") { SourceText = Src };
        vm.EasyMode = true;
        var sql = vm.BuildCompileSql();

        Assert.Contains("CREATE OR ALTER PROCEDURE P", sql);
        Assert.Contains("DECLARE VARIABLE T INTEGER;", sql);
        Assert.Contains("DECLARE VARIABLE S VARCHAR(20) NOT NULL", sql);
        Assert.Contains("CURSOR FOR (SELECT ID FROM ITEMS)", sql);
        Assert.Contains("DECLARE PROCEDURE SUB", sql);
        Assert.Contains("SUSPEND;", sql);
    }

    // A compact, comparable snapshot of the whole structured model.
    private static string Snapshot(ProcedureDetailTabViewModel vm)
    {
        string Norm(string s) => string.Join(' ', s.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
        var parts = new[]
        {
            "IN:" + string.Join("|", vm.InputParams.Select(p => $"{p.Name},{p.TypeText},{p.NotNull},{p.DefaultValue}")),
            "OUT:" + string.Join("|", vm.OutputParams.Select(p => $"{p.Name},{p.TypeText},{p.NotNull}")),
            "VAR:" + string.Join("|", vm.Variables.Select(v => $"{v.Name},{v.TypeText},{v.NotNull},{v.DefaultValue}")),
            "CUR:" + string.Join("|", vm.Cursors.Select(c => $"{c.Name}::{Norm(c.Declaration)}")),
            "SUB:" + string.Join("|", vm.Subprograms.Select(s => $"{s.Name},{s.Kind}::{Norm(s.Declaration)}")),
            "BODY:" + Norm(vm.ExecutableBody),
        };
        return string.Join("\n", parts);
    }
}
