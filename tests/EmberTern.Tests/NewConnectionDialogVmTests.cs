using EmberTern.App.ViewModels;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class NewConnectionDialogVmTests
{
    [Fact]
    public void Save_BlankName_DerivesFromDatabaseFileBaseName()
    {
        using var service = new FirebirdConnectionService();
        var vm = new NewConnectionDialogViewModel(new EmberTern.App.Licensing.LicensedConnections(service, license: null))
        {
            DatabasePath = @"D:\Bazy\Firma\Magazyn.fdb",
            Name = string.Empty,
        };

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Equal("Magazyn", vm.Result!.Name);
        // The dialog field is populated too, not just the saved profile.
        Assert.Equal("Magazyn", vm.Name);
    }

    [Fact]
    public void Save_ExplicitName_IsNotOverwrittenByPath()
    {
        using var service = new FirebirdConnectionService();
        var vm = new NewConnectionDialogViewModel(new EmberTern.App.Licensing.LicensedConnections(service, license: null))
        {
            DatabasePath = @"D:\Bazy\Firma\Magazyn.fdb",
            Name = "Production",
        };

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Equal("Production", vm.Result!.Name);
    }

    [Fact]
    public void Save_BlankNameAndBlankPath_FailsValidation_NoResult()
    {
        using var service = new FirebirdConnectionService();
        var vm = new NewConnectionDialogViewModel(new EmberTern.App.Licensing.LicensedConnections(service, license: null)) { DatabasePath = string.Empty, Name = string.Empty };

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Result);
        Assert.True(vm.HasValidationMessage);
    }
}
