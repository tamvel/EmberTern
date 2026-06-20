using System.Linq;
using EmberTern.App.ViewModels;
using Xunit;

namespace EmberTern.Tests;

// VM-level behaviour for the Trigger Detail editor — Source⇄Easy round-trip, the
// auto-name generator, dirty tracking / WorkGuard, and Compile reassembly. All run
// against the parameterless ctor (null readers) so no DB is needed.
public class TriggerDetailTests
{
    private const string Src =
        "CREATE OR ALTER TRIGGER MY_TRIG FOR CUSTOMERS\n"
        + "ACTIVE BEFORE INSERT OR UPDATE POSITION 5\n"
        + "AS\n"
        + "DECLARE VARIABLE T INTEGER;\n"
        + "DECLARE VARIABLE S VARCHAR(20) NOT NULL = '';\n"
        + "BEGIN\n  T = 1;\n  S = '';\nEND";

    // ─── Source → Easy ─────────────────────────────────────────────────────

    [Fact]
    public void SourceToEasy_PopulatesHeaderAndVariables()
    {
        var vm = new TriggerDetailTabViewModel("MY_TRIG") { SourceText = Src };
        vm.EasyMode = true;

        Assert.Null(vm.ErrorMessage);
        Assert.Equal("CUSTOMERS", vm.TableName);
        Assert.Equal("BEFORE", vm.SelectedTiming);
        Assert.True(vm.IsBefore);
        Assert.True(vm.FiresInsert);
        Assert.True(vm.FiresUpdate);
        Assert.False(vm.FiresDelete);
        Assert.Equal(5, vm.Position);
        Assert.True(vm.Active);

        Assert.Equal(2, vm.Variables.Count);
        Assert.Equal("T", vm.Variables[0].Name);
        Assert.Equal("INTEGER", vm.Variables[0].TypeText);
        Assert.Equal("S", vm.Variables[1].Name);
        Assert.True(vm.Variables[1].NotNull);
        Assert.StartsWith("BEGIN", vm.ExecutableBody);
    }

    [Fact]
    public void SourceToEasy_BadShape_KeepsLastStateAndNotes()
    {
        var vm = new TriggerDetailTabViewModel("X") { SourceText = "this is not a trigger" };
        vm.EasyMode = true;
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    // ─── Round-trips ────────────────────────────────────────────────────────

    [Fact]
    public void SourceToEasyToSourceToEasy_PreservesEverything()
    {
        var vm = new TriggerDetailTabViewModel("MY_TRIG") { SourceText = Src };
        vm.EasyMode = true;
        var first = Snapshot(vm);

        vm.EasyMode = false;
        Assert.Contains("CREATE OR ALTER TRIGGER MY_TRIG", vm.SourceText);
        Assert.Contains("FOR CUSTOMERS", vm.SourceText);
        Assert.Contains("BEFORE INSERT OR UPDATE", vm.SourceText);

        vm.EasyMode = true;
        Assert.Equal(first, Snapshot(vm));
    }

    [Fact]
    public void Compile_Easy_ReassemblesFromModel()
    {
        var vm = new TriggerDetailTabViewModel("MY_TRIG") { SourceText = Src };
        vm.EasyMode = true;
        var sql = vm.BuildCompileSql();

        Assert.Contains("CREATE OR ALTER TRIGGER MY_TRIG FOR CUSTOMERS", sql);
        Assert.Contains("ACTIVE BEFORE INSERT OR UPDATE POSITION 5", sql);
        Assert.Contains("DECLARE VARIABLE T INTEGER;", sql);
        Assert.Contains("DECLARE VARIABLE S VARCHAR(20) NOT NULL", sql);
    }

    [Fact]
    public void Compile_Source_UsesRawText()
    {
        var vm = new TriggerDetailTabViewModel("T") { SourceText = Src };
        Assert.False(vm.EasyMode);
        Assert.Equal(Src, vm.BuildCompileSql());
    }

    // ─── Auto-name ──────────────────────────────────────────────────────────

    [Fact]
    public void AutoName_NewTrigger_DerivesFromMetadata()
    {
        var vm = new TriggerDetailTabViewModel("New Trigger") { IsNew = true };
        vm.SelectedTiming = "AFTER";
        vm.FiresInsert = true;
        vm.Position = 100;
        vm.TableName = "CUSTOMERS";
        Assert.Equal("CUSTOMERS_AI_100", vm.EditableTriggerName);

        vm.FiresUpdate = true;
        vm.FiresDelete = true;
        Assert.Equal("CUSTOMERS_AIUD_100", vm.EditableTriggerName);
    }

    [Fact]
    public void AutoName_StopsAfterUserOverride()
    {
        var vm = new TriggerDetailTabViewModel("New Trigger") { IsNew = true };
        vm.TableName = "ORDERS";        // auto-name fires
        vm.EditableTriggerName = "MY_CUSTOM_NAME"; // user override
        vm.FiresUpdate = true;          // metadata change after override
        Assert.Equal("MY_CUSTOM_NAME", vm.EditableTriggerName);
    }

    [Fact]
    public void AutoName_DoesNotFireForExistingTrigger()
    {
        var vm = new TriggerDetailTabViewModel("EXISTING_TRIG");
        vm.TableName = "ORDERS";
        vm.FiresInsert = true;
        Assert.Equal("EXISTING_TRIG", vm.EditableTriggerName);
    }

    // ─── Dirty tracking / WorkGuard ────────────────────────────────────────

    [Fact]
    public void FreshTrigger_IsNotDirty()
    {
        var vm = new TriggerDetailTabViewModel("T");
        Assert.False(vm.IsDirty);
        Assert.Null(vm.GetUnsavedWork());
    }

    [Fact]
    public void EditingSource_MarksDirty_ModifiedSource()
    {
        var vm = new TriggerDetailTabViewModel("T");
        vm.SourceText = "CREATE OR ALTER TRIGGER T FOR X BEFORE INSERT AS BEGIN END";
        Assert.True(vm.IsDirty);
        var work = vm.GetUnsavedWork();
        Assert.NotNull(work);
        Assert.Equal(UnsavedWorkKind.ModifiedSource, work!.Kind);
    }

    [Fact]
    public void EditingNew_MarksDirty_NewObject()
    {
        var vm = new TriggerDetailTabViewModel("New Trigger") { IsNew = true };
        vm.TableName = "X";
        Assert.True(vm.IsDirty);
        Assert.Equal(UnsavedWorkKind.NewObject, vm.GetUnsavedWork()!.Kind);
    }

    [Fact]
    public void ModeToggle_IsNotAnEdit()
    {
        var vm = new TriggerDetailTabViewModel("T") { SourceText = Src };
        vm.ClearDirty();
        vm.EasyMode = true;   // pure projection
        vm.EasyMode = false;  // regenerate
        Assert.False(vm.IsDirty);
    }

    // ─── Capabilities ───────────────────────────────────────────────────────

    [Fact]
    public void NoExecutor_CannotCompileOrEditDescription()
    {
        var vm = new TriggerDetailTabViewModel("T");
        Assert.False(vm.CanCompile);
        Assert.False(vm.CanEditDescription);
    }

    [Fact]
    public void SelectedTable_IgnoresNullClobber()
    {
        // The ComboBox nulls SelectedItem when TableName isn't yet in AvailableTables
        // (async load) — the wrapper must keep the value (gotcha #71).
        var vm = new TriggerDetailTabViewModel("T");
        vm.TableName = "CUSTOMERS";
        vm.SelectedTable = null;
        Assert.Equal("CUSTOMERS", vm.TableName);
        vm.SelectedTable = "ORDERS";
        Assert.Equal("ORDERS", vm.TableName);
    }

    private static string Snapshot(TriggerDetailTabViewModel vm)
    {
        string Norm(string s) => string.Join(' ', s.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
        return string.Join("\n", new[]
        {
            $"HDR:{vm.TableName},{vm.SelectedTiming},{vm.FiresInsert}{vm.FiresUpdate}{vm.FiresDelete},{vm.Position},{vm.Active}",
            "VAR:" + string.Join("|", vm.Variables.Select(v => $"{v.Name},{v.TypeText},{v.NotNull},{v.DefaultValue}")),
            "BODY:" + Norm(vm.ExecutableBody),
        });
    }
}
