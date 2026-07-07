using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Pins Part 2 — the pure dependent-candidate filter + the checklist dialog VM
/// (no DB / no services).</summary>
public class RecompileDependentsTests
{
    private static DependencyInfo Dep(string name, string type) => new() { ObjectName = name, ObjectType = type };
    private static MetadataObject Obj(string name, MetadataObjectKind kind) => new(name, kind);

    [Fact]
    public void RecompilableDependents_KeepsOnlyRecompilableKinds()
    {
        var deps = new[]
        {
            Dep("PROC_B", "Procedure"),
            Dep("V1", "View"),
            Dep("F1", "Function"),
            Dep("TRG1", "Trigger"),
            Dep("PKG1", "Package"),
            Dep("T1", "Table"),       // not recompilable
            Dep("D1", "Domain"),      // not recompilable
        };

        var result = MainWindowViewModel.RecompilableDependents(deps, Obj("PROC_A", MetadataObjectKind.Procedure));

        Assert.Equal(5, result.Count);
        Assert.DoesNotContain(result, o => o.Kind is MetadataObjectKind.Table or MetadataObjectKind.Domain);
        Assert.Contains(result, o => o.Name == "PROC_B" && o.Kind == MetadataObjectKind.Procedure);
        Assert.Contains(result, o => o.Name == "V1" && o.Kind == MetadataObjectKind.View);
    }

    [Fact]
    public void RecompilableDependents_DedupesAndDropsSelfAndUnknown()
    {
        var deps = new[]
        {
            Dep("PROC_B", "Procedure"),
            Dep("PROC_B", "Procedure"),                 // duplicate
            Dep("PROC_A", "Procedure"),                 // self — the object just compiled
            Dep("X", "SomethingUnknown"),               // unmapped → dropped
            Dep("", "Procedure"),                        // empty name → dropped
        };

        var result = MainWindowViewModel.RecompilableDependents(deps, Obj("PROC_A", MetadataObjectKind.Procedure));

        Assert.Single(result);
        Assert.Equal("PROC_B", result[0].Name);
    }

    [Fact]
    public void RecompilableDependents_EmptyInput_EmptyResult()
        => Assert.Empty(MainWindowViewModel.RecompilableDependents(
            System.Array.Empty<DependencyInfo>(), Obj("P", MetadataObjectKind.Procedure)));

    // ── Dialog VM ──────────────────────────────────────────────────────────────
    private static RecompileDependentsDialogViewModel Dialog(params MetadataObject[] candidates)
        => new(new RecompileDependentsRequest(Obj("PROC_A", MetadataObjectKind.Procedure), candidates));

    [Fact]
    public void Dialog_ItemsAllCheckedByDefault_HeaderNamesTheObject()
    {
        var vm = Dialog(Obj("PROC_B", MetadataObjectKind.Procedure), Obj("V1", MetadataObjectKind.View));
        Assert.All(vm.Items, i => Assert.True(i.IsChecked));
        Assert.Contains("PROC_A", vm.HeaderText);
    }

    [Fact]
    public void Dialog_Recompile_ReturnsCheckedSelection()
    {
        var vm = Dialog(Obj("PROC_B", MetadataObjectKind.Procedure), Obj("V1", MetadataObjectKind.View));
        vm.Items[1].IsChecked = false; // skip V1
        vm.DontAskAgain = true;
        vm.RecompileCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.True(vm.Result!.DontAskAgain);
        Assert.Single(vm.Result.Selected);
        Assert.Equal("PROC_B", vm.Result.Selected[0].Name);
    }

    [Fact]
    public void Dialog_SelectNone_ThenRecompile_EmptySelection()
    {
        var vm = Dialog(Obj("PROC_B", MetadataObjectKind.Procedure));
        vm.SelectNoneCommand.Execute(null);
        vm.RecompileCommand.Execute(null);
        Assert.NotNull(vm.Result);
        Assert.Empty(vm.Result!.Selected);
    }

    [Fact]
    public void Dialog_Skip_WithoutDontAsk_ReturnsNull()
    {
        var vm = Dialog(Obj("PROC_B", MetadataObjectKind.Procedure));
        vm.SkipCommand.Execute(null);
        Assert.Null(vm.Result);
    }

    [Fact]
    public void Dialog_Skip_WithDontAsk_ReturnsEmptyButSuppresses()
    {
        var vm = Dialog(Obj("PROC_B", MetadataObjectKind.Procedure));
        vm.DontAskAgain = true;
        vm.SkipCommand.Execute(null);
        Assert.NotNull(vm.Result);
        Assert.True(vm.Result!.DontAskAgain);
        Assert.Empty(vm.Result.Selected);
    }
}
