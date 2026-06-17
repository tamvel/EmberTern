using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

// Structured Easy mode round 2: full field-definition rows (Type/Domain/Size/Scale/
// TYPE OF/Charset/Collate), cursor Scroll + editable name, subprogram kind, and
// Add/Move/Delete order preservation through Easy → Source → Easy.
public class ProcedureEasyModeStructuredTests
{
    // ─── #1 field model: parse + compose + round-trip ─────────────────────

    [Fact]
    public void FieldRow_LoadType_ParsesVarcharSizeAndScale()
    {
        var r = ProcedureVariableRowViewModel.From(new ProcedureVariable { Name = "S", TypeText = "VARCHAR(50)" });
        Assert.Equal("VARCHAR", r.BaseType);
        Assert.Equal(50, r.Size);
        Assert.Null(r.Scale);
        Assert.Equal("VARCHAR(50)", r.TypeText);

        var n = ProcedureVariableRowViewModel.From(new ProcedureVariable { Name = "N", TypeText = "NUMERIC(18,4)" });
        Assert.Equal("NUMERIC", n.BaseType);
        Assert.Equal(18, n.Size);
        Assert.Equal(4, n.Scale);
    }

    [Fact]
    public void FieldRow_LoadType_PreservesExoticFormsVerbatim()
    {
        // TYPE OF and CHARACTER SET / COLLATE must survive a load untouched.
        var t = ProcedureVariableRowViewModel.From(new ProcedureVariable { Name = "X", TypeText = "TYPE OF COLUMN ADRES.MIASTO" });
        Assert.Equal("COLUMN ADRES.MIASTO", t.TypeOf);
        Assert.Equal("TYPE OF COLUMN ADRES.MIASTO", t.ToVariable().TypeText);

        var c = ProcedureVariableRowViewModel.From(new ProcedureVariable { Name = "Y", TypeText = "VARCHAR(10) CHARACTER SET WIN1250 COLLATE PXW_PLK" });
        Assert.Equal("VARCHAR", c.BaseType);
        Assert.Equal("WIN1250", c.Charset);
        Assert.Equal("PXW_PLK", c.Collate);
        Assert.Equal("VARCHAR(10) CHARACTER SET WIN1250 COLLATE PXW_PLK", c.ToVariable().TypeText);
    }

    [Fact]
    public void FieldRow_EditingStructuredFields_RecomposesTypeText()
    {
        var r = new ProcedureVariableRowViewModel { Name = "V" };
        r.BaseType = "VARCHAR";
        r.Size = 80;
        Assert.Equal("VARCHAR(80)", r.TypeText);

        // Picking a domain takes precedence — the canonical type becomes the domain name
        // (the Type cell stays populated for display; it is NOT blanked).
        r.DomainName = "T_KODPOCZ";
        Assert.Equal("T_KODPOCZ", r.DomainName);
        Assert.Equal("T_KODPOCZ", r.TypeText);
    }

    [Fact]
    public void FieldRow_OutputParam_OmitsDefault()
    {
        var p = new ProcedureParamRowViewModel { IsOutput = true, DefaultValue = "0" };
        p.Name = "R"; p.BaseType = "INTEGER";
        Assert.Null(p.ToParameter().DefaultValue);
    }

    // ─── #3 cursor: Scroll + editable name regenerate the declaration ──────

    [Fact]
    public void Cursor_ScrollToggle_RegeneratesDeclaration()
    {
        var c = ProcedureCursorRowViewModel.From(new ProcedureCursor
        {
            Name = "C",
            Declaration = "DECLARE C CURSOR FOR (SELECT 1 FROM RDB$DATABASE);",
        });
        Assert.False(c.Scroll);

        c.Scroll = true;
        Assert.Contains("SCROLL CURSOR", c.Declaration);
        Assert.True(ProcedureBodySplitter.CursorIsScroll(c.Declaration));

        c.Scroll = false;
        Assert.DoesNotContain("SCROLL", c.Declaration);
    }

    [Fact]
    public void Cursor_RenameInList_RewritesHeaderKeepsBody()
    {
        var c = ProcedureCursorRowViewModel.From(new ProcedureCursor
        {
            Name = "OLD",
            Declaration = "DECLARE OLD CURSOR FOR (SELECT ID FROM T);",
        });
        c.Name = "NEW";
        Assert.Contains("DECLARE NEW", c.Declaration);
        Assert.Contains("(SELECT ID FROM T)", c.Declaration);
    }

    [Fact]
    public void Cursor_ScrollSurvives_RoundTrip()
    {
        var vm = NewEasyVm();
        vm.Cursors.Add(ProcedureCursorRowViewModel.From(new ProcedureCursor
        {
            Name = "C", Declaration = "DECLARE C SCROLL CURSOR FOR (SELECT 1 FROM RDB$DATABASE);",
        }));
        vm.ExecutableBody = "BEGIN END";

        var vm2 = RoundTrip(vm);
        Assert.Single(vm2.Cursors);
        Assert.True(vm2.Cursors[0].Scroll);
    }

    // ─── #4 subprogram: kind chosen on add + preserved on round-trip ──────

    [Fact]
    public void Subprogram_RenameInList_RewritesHeaderKeepsKindAndBody()
    {
        var s = ProcedureSubprogramRowViewModel.From(new ProcedureSubprogram
        {
            Name = "OLD", Kind = "FUNCTION", Declaration = "DECLARE FUNCTION OLD RETURNS INTEGER AS BEGIN RETURN 0; END",
        });
        s.Name = "NEW";
        Assert.Contains("DECLARE FUNCTION NEW", s.Declaration);
        Assert.Contains("RETURNS INTEGER", s.Declaration);
    }

    [Fact]
    public void Subprogram_KindSurvives_RoundTrip()
    {
        var vm = NewEasyVm();
        vm.Subprograms.Add(ProcedureSubprogramRowViewModel.From(new ProcedureSubprogram
        {
            Name = "F", Kind = "FUNCTION", Declaration = "DECLARE FUNCTION F RETURNS INTEGER AS BEGIN RETURN 0; END",
        }));
        vm.ExecutableBody = "BEGIN END";

        var vm2 = RoundTrip(vm);
        Assert.Single(vm2.Subprograms);
        Assert.Equal("FUNCTION", vm2.Subprograms[0].Kind);
        Assert.Equal("F", vm2.Subprograms[0].Name);
    }

    [Fact]
    public void AddSubprogram_UsesChosenKindTemplate()
    {
        var vm = NewEasyVm();
        vm.SubprogramKindRequested = () => System.Threading.Tasks.Task.FromResult<string?>("FUNCTION");
        vm.AddSubprogramCommand.Execute(null);
        Assert.Single(vm.Subprograms);
        Assert.Equal("FUNCTION", vm.Subprograms[0].Kind);
    }

    // ─── #5 Add / Move / Delete order preserved through round-trip ─────────

    [Fact]
    public void Variables_MoveUp_OrderPreservedThroughRoundTrip()
    {
        var vm = NewEasyVm();
        AddVar(vm, "A"); AddVar(vm, "B"); AddVar(vm, "C");
        vm.ExecutableBody = "BEGIN END";

        vm.SelectedVariable = vm.Variables[2];   // C
        vm.MoveVariableUpCommand.Execute(null);  // A, C, B

        Assert.Equal(new[] { "A", "C", "B" }, RoundTrip(vm).Variables.Select(v => v.Name));
    }

    [Fact]
    public void Variables_Delete_OrderPreservedThroughRoundTrip()
    {
        var vm = NewEasyVm();
        AddVar(vm, "A"); AddVar(vm, "B"); AddVar(vm, "C");
        vm.ExecutableBody = "BEGIN END";

        vm.SelectedVariable = vm.Variables[1];   // B
        vm.DeleteVariableCommand.Execute(null);  // A, C

        Assert.Equal(new[] { "A", "C" }, RoundTrip(vm).Variables.Select(v => v.Name));
    }

    [Fact]
    public void Cursors_MoveDown_OrderPreservedThroughRoundTrip()
    {
        var vm = NewEasyVm();
        AddCursor(vm, "CA"); AddCursor(vm, "CB"); AddCursor(vm, "CC");
        vm.ExecutableBody = "BEGIN END";

        vm.SelectedCursor = vm.Cursors[0];       // CA
        vm.MoveCursorDownCommand.Execute(null);  // CB, CA, CC

        Assert.Equal(new[] { "CB", "CA", "CC" }, RoundTrip(vm).Cursors.Select(c => c.Name));
    }

    [Fact]
    public void Subprograms_MoveAndDelete_OrderPreservedThroughRoundTrip()
    {
        var vm = NewEasyVm();
        AddSub(vm, "SA"); AddSub(vm, "SB"); AddSub(vm, "SC");
        vm.ExecutableBody = "BEGIN END";

        vm.SelectedSubprogram = vm.Subprograms[2];     // SC
        vm.MoveSubprogramUpCommand.Execute(null);       // SA, SC, SB
        vm.SelectedSubprogram = vm.Subprograms[0];     // SA
        vm.DeleteSubprogramCommand.Execute(null);       // SC, SB

        Assert.Equal(new[] { "SC", "SB" }, RoundTrip(vm).Subprograms.Select(s => s.Name));
    }

    // ─── helpers ───────────────────────────────────────────────────────────

    private static ProcedureDetailTabViewModel NewEasyVm()
    {
        var vm = new ProcedureDetailTabViewModel("P");
        vm.EasyMode = true;          // empty source → guarded, model stays empty
        vm.ErrorMessage = null;
        return vm;
    }

    private static void AddVar(ProcedureDetailTabViewModel vm, string name)
        => vm.Variables.Add(new ProcedureVariableRowViewModel { Name = name, TypeText = "INTEGER" });

    private static void AddCursor(ProcedureDetailTabViewModel vm, string name)
        => vm.Cursors.Add(ProcedureCursorRowViewModel.From(new ProcedureCursor
        {
            Name = name, Declaration = $"DECLARE {name} CURSOR FOR (SELECT 1 FROM RDB$DATABASE);",
        }));

    private static void AddSub(ProcedureDetailTabViewModel vm, string name)
        => vm.Subprograms.Add(ProcedureSubprogramRowViewModel.From(new ProcedureSubprogram
        {
            Name = name, Kind = "PROCEDURE", Declaration = $"DECLARE PROCEDURE {name} AS BEGIN END",
        }));

    // Easy → Source → Easy.
    private static ProcedureDetailTabViewModel RoundTrip(ProcedureDetailTabViewModel vm)
    {
        var src = vm.BuildFullSource();
        var vm2 = new ProcedureDetailTabViewModel("P") { SourceText = src };
        vm2.EasyMode = true;
        return vm2;
    }
}
