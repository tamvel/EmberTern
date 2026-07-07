using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Pins the Etap 4 Open/Save VM state transitions (the file picker + IO are view-side,
/// verified by manual smoke). The VM constructs without a live connection.</summary>
public class ScriptExecutorFileTests
{
    private static ScriptExecutorTabViewModel Make(FirebirdConnectionService cs)
    {
        var ts = new TransactionService(cs);
        return new ScriptExecutorTabViewModel(new FirebirdScriptParser(), new FirebirdScriptExecutor(cs, ts), ts);
    }

    [Fact]
    public void LoadScript_SetsTextAndOpenedStatus_ClearsError()
    {
        using var cs = new FirebirdConnectionService();
        var vm = Make(cs);
        vm.ReportFileError("boom");
        Assert.True(vm.HasError);

        vm.LoadScript("select 1 from rdb$database", "migrate.sql");

        Assert.Equal("select 1 from rdb$database", vm.ScriptText);
        Assert.False(vm.HasError);
        Assert.Contains("migrate.sql", vm.StatusText);
    }

    [Fact]
    public void ReportFileSaved_SetsSavedStatus_NoError()
    {
        using var cs = new FirebirdConnectionService();
        var vm = Make(cs);
        vm.ReportFileSaved("out.sql");
        Assert.False(vm.HasError);
        Assert.Contains("out.sql", vm.StatusText);
    }

    [Fact]
    public void ReportFileError_SetsErrorAndSurfacesMessage()
    {
        using var cs = new FirebirdConnectionService();
        var vm = Make(cs);
        vm.ReportFileError("disk full");
        Assert.True(vm.HasError);
        Assert.Contains("disk full", vm.StatusText);
    }
}
