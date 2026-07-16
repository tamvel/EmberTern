using EmberTern.App.ViewModels;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage 7 (Diagnostics) — S3 follow-up. The Easy-mode routine editors seed the semantic model with
/// ambient symbols drawn from their grids (params / DECLAREd variables). These pin the VM signal that
/// drives a live model rebuild — <see cref="SourceObjectDetailTabViewModel.AmbientSymbolsChanged"/> — so
/// diagnostics/completion/highlighting refresh the moment a grid changes, instead of going stale until
/// the next body-text edit (the reported UX bug: a squiggle under <c>:test</c> lingering after the user
/// added <c>test</c> to the Variables grid).
/// </summary>
public class EasyModeAmbientRefreshTests
{
    [Fact]
    public void AddingAVariable_RaisesAmbientSymbolsChanged()
    {
        var vm = new ProcedureDetailTabViewModel("P");
        int raised = 0;
        vm.AmbientSymbolsChanged += (_, _) => raised++;

        vm.Variables.Add(new ProcedureVariableRowViewModel { Name = "TEST", TypeText = "INTEGER" });

        Assert.True(raised > 0);
    }

    [Fact]
    public void RenamingAVariable_RaisesAmbientSymbolsChanged()
    {
        var vm = new ProcedureDetailTabViewModel("P");
        var row = new ProcedureVariableRowViewModel { Name = "TEST", TypeText = "INTEGER" };
        vm.Variables.Add(row);

        int raised = 0;
        vm.AmbientSymbolsChanged += (_, _) => raised++;
        row.Name = "TEST2";

        Assert.True(raised > 0);
    }

    [Fact]
    public void AddingAndRenamingInputParam_RaisesAmbientSymbolsChanged()
    {
        var vm = new ProcedureDetailTabViewModel("P");
        int raised = 0;
        vm.AmbientSymbolsChanged += (_, _) => raised++;

        var row = new ProcedureParamRowViewModel { Name = "DATAOD", TypeText = "TIMESTAMP" };
        vm.InputParams.Add(row);
        Assert.True(raised > 0);

        int afterAdd = raised;
        row.Name = "DATAOD2";
        Assert.True(raised > afterAdd);
    }

    [Fact]
    public void EditingANonNameRowProperty_DoesNotRaiseAmbientSymbolsChanged()
    {
        // Only the NAME affects symbol resolution — type/size/etc. must not churn the model.
        var vm = new ProcedureDetailTabViewModel("P");
        var row = new ProcedureVariableRowViewModel { Name = "TEST", TypeText = "INTEGER" };
        vm.Variables.Add(row);

        int raised = 0;
        vm.AmbientSymbolsChanged += (_, _) => raised++;
        row.TypeText = "VARCHAR(10)";

        Assert.Equal(0, raised);
    }

    [Fact]
    public void FunctionArgument_Add_RaisesAmbientSymbolsChanged()
    {
        var vm = new FunctionDetailTabViewModel("F");
        int raised = 0;
        vm.AmbientSymbolsChanged += (_, _) => raised++;

        vm.Arguments.Add(new ProcedureParamRowViewModel { Name = "X", TypeText = "INTEGER" });

        Assert.True(raised > 0);
    }
}
