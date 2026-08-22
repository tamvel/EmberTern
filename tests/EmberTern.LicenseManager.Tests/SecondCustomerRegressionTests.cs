using System;
using System.Linq;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⚠⚠ <b>The reported defect: after adding a SECOND customer with a licence, the Licences view showed
/// only one licence.</b>
///
/// <para>Reproduced the way the operator produced it — through the view model, in the order a person
/// clicks — rather than by calling the register directly. That distinction is the whole point: the
/// register's query was never wrong.</para>
/// </summary>
public sealed class SecondCustomerRegressionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 10, 0, 0, TimeSpan.Zero);

    private readonly ManagerFixture _manager = new(Now);

    private ShellViewModel NewShell() =>
        new(_manager.Register, _manager.Session, _manager.Paths, () => Now);

    private static void AddCustomerWithLicence(ShellViewModel shell, string name)
    {
        shell.NewCustomerCommand.Execute(null);
        shell.CustomerName = name;
        shell.SaveCustomerCommand.Execute(null);

        shell.NewLicenseCommand.Execute(null);
        shell.SaveLicenseCommand.Execute(null);
    }

    [Fact]
    public void TwoCustomersWithALicenceEachProduceTwoLicences()
    {
        var shell = NewShell();

        AddCustomerWithLicence(shell, "First");
        AddCustomerWithLicence(shell, "Second");

        Assert.Equal(2, _manager.Register.GetCustomers().Count);
        Assert.Equal(2, _manager.Register.QueryLicenses().Count);

        shell.ShowLicensesCommand.Execute(null);
        Assert.Equal(2, shell.Browser.Results.Count);
    }

    [Fact]
    public void EachLicenceStaysWithTheCustomerItWasCreatedFor()
    {
        var shell = NewShell();

        AddCustomerWithLicence(shell, "First");
        var firstCustomer = _manager.Register.GetCustomers().Single(c => c.Name == "First");
        var firstLicence = Assert.Single(_manager.Register.GetLicenses(firstCustomer.CustomerId));

        AddCustomerWithLicence(shell, "Second");

        // ⭐⭐ THE ACTUAL DEFECT. The licence FORM was not cleared when a new customer was started, so the
        //     stale licence id was still in it. Saving terms for the second customer then wrote that id
        //     with the new customer_id — silently RE-PARENTING the first customer's licence instead of
        //     creating one. One row where there should be two, and the first customer quietly lost theirs.
        Assert.Single(_manager.Register.GetLicenses(firstCustomer.CustomerId));
        Assert.Equal(
            firstLicence.LicenseId,
            _manager.Register.GetLicenses(firstCustomer.CustomerId)[0].LicenseId);
    }

    [Fact]
    public void SavingTermsForANewCustomerWithoutStartingANewLicenceDoesNotStealTheOldOne()
    {
        // ⭐⭐ THE REPORTED PATH, exactly. The operator adds a second customer and — without clicking
        //     "New licence" — fills in the terms and presses Save. The stale licence id from the previous
        //     customer is still in the form, so the save addresses THAT row and moves it across. Result:
        //     one licence where there should be two, and the first customer silently loses theirs.
        var shell = NewShell();
        AddCustomerWithLicence(shell, "First");

        var first = _manager.Register.GetCustomers().Single(c => c.Name == "First");
        var firstLicence = Assert.Single(_manager.Register.GetLicenses(first.CustomerId));

        shell.NewCustomerCommand.Execute(null);
        shell.CustomerName = "Second";
        shell.SaveCustomerCommand.Execute(null);

        shell.LicenseSeats = 3;
        shell.LicenseNotBefore = new DateTime(2026, 8, 16);
        shell.LicenseExpiresAt = new DateTime(2027, 8, 16);
        shell.SaveLicenseCommand.Execute(null);

        Assert.Single(_manager.Register.GetLicenses(first.CustomerId));
        Assert.Equal(
            firstLicence.LicenseId,
            _manager.Register.GetLicenses(first.CustomerId)[0].LicenseId);
        Assert.Equal(2, _manager.Register.QueryLicenses().Count);
    }

    [Fact]
    public void StartingANewCustomerClearsTheLicenceFormEntirely()
    {
        // ⚠ The root cause, pinned at its source: a licence form belongs to ONE customer. Leaving an id in
        //   it across a customer change is what let a save address the wrong row.
        var shell = NewShell();
        AddCustomerWithLicence(shell, "First");

        Assert.NotEqual(string.Empty, shell.LicenseId);

        shell.NewCustomerCommand.Execute(null);

        Assert.Equal(string.Empty, shell.LicenseId);
        Assert.Null(shell.LicenseNotBefore);
        Assert.Null(shell.LicenseExpiresAt);
        Assert.Empty(shell.Licenses);
    }

    [Fact]
    public void SwitchingBetweenCustomersNeverCarriesALicenceAcross()
    {
        var shell = NewShell();
        AddCustomerWithLicence(shell, "First");
        AddCustomerWithLicence(shell, "Second");

        var first = shell.Customers.Single(c => c.Name == "First");
        var second = shell.Customers.Single(c => c.Name == "Second");

        shell.SelectedCustomer = first;
        var firstLid = Assert.Single(shell.Licenses).LicenseId;

        shell.SelectedCustomer = second;
        var secondLid = Assert.Single(shell.Licenses).LicenseId;

        Assert.NotEqual(firstLid, secondLid);
    }

    [Fact]
    public void TheRegisterRefusesToMoveALicenceToAnotherCustomer()
    {
        // ⭐⭐ The domain guard behind the UI fix. Clearing a form is a good habit; refusing the write is a
        //     GUARANTEE. A licence's customer is part of its identity — every artifact ever signed for it
        //     carries that customer's name (D6), so re-parenting the row would make the register disagree
        //     with the files it already sent. Architecture rule 11 on the admin side.
        var one = _manager.SaveCustomer("First");
        var two = _manager.SaveCustomer("Second");
        var licence = _manager.SaveLicense(one);

        var moved = Assert.Throws<RegisterIntegrityException>(
            () => _manager.Register.SaveLicense(licence with { CustomerId = two.CustomerId }));

        Assert.Contains(licence.LicenseId, moved.Message, StringComparison.Ordinal);
        Assert.Equal(one.CustomerId, _manager.Register.GetLicense(licence.LicenseId)!.CustomerId);
    }

    [Fact]
    public void TheLicencesViewIsNotFilteredByTheCustomerSelectedInTheOtherView()
    {
        // ⚠ The other hypothesis the report named. Asserted so it stays refuted: the cross-customer view
        //   must not inherit the customers view's selection.
        var shell = NewShell();
        AddCustomerWithLicence(shell, "First");
        AddCustomerWithLicence(shell, "Second");

        shell.SelectedCustomer = shell.Customers.Single(c => c.Name == "First");
        shell.ShowLicensesCommand.Execute(null);

        Assert.Equal(2, shell.Browser.Results.Count);
    }

    public void Dispose() => _manager.Dispose();
}
