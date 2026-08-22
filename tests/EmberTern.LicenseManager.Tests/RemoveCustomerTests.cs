using System;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>Removing a customer — the only destructive act on the main window.</b>
///
/// <para>⭐ <b>"Removed" means the working row goes and the HISTORY keeps them</b>, and that is not a
/// softening of the word: the audit log is append-only at the DATABASE (a trigger aborts every DELETE on
/// it), so the <c>customer.deleted</c> line carrying the whole record outlives the row permanently. Rule
/// #11 applied to an operator's mistake rather than to a signature.</para>
///
/// <para>⭐⭐ <b>A customer with even ONE licence is refused, and the schema is why.</b>
/// <c>licenses.customer_id</c> is a foreign key with <c>PRAGMA foreign_keys</c> ON, and the cascade an
/// impatient version would need is unreachable because <c>issued_artifacts</c> aborts every DELETE by
/// trigger. There is no order of operations that removes a customer who has ever been issued anything —
/// so refusing WITH THE COUNT is the honest answer rather than a limitation.</para>
/// </summary>
public sealed class RemoveCustomerTests
{
    // ── The register ────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ A customer with no licences goes, and the history keeps the whole record.</summary>
    [Fact]
    public void ACustomerWithNoLicencesIsRemovedAndRemembered()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer("ACME Sp. z o.o.");

        manager.Register.DeleteCustomer(customer.CustomerId);

        Assert.Null(manager.Register.GetCustomer(customer.CustomerId));
        Assert.DoesNotContain(manager.Register.GetCustomers(), c => c.CustomerId == customer.CustomerId);

        // ⭐⭐ THE POINT OF THE WHOLE SHAPE: the register can still answer "who was c-0001?".
        var line = Assert.Single(
            manager.Register.GetAudit(new AuditQuery { Action = "customer.deleted" }));

        Assert.Equal(customer.CustomerId, line.TargetId);
        Assert.NotNull(line.BeforeJson);
        Assert.Contains("ACME Sp. z o.o.", line.BeforeJson, StringComparison.Ordinal);

        // ⚠ There is no "after" — that is what removal means.
        Assert.Null(line.AfterJson);
    }

    /// <summary>⛔ A customer with a licence is REFUSED, and the refusal names the obstacle.</summary>
    [Fact]
    public void ACustomerWithALicenceIsRefused()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer("Beta S.A.");
        manager.SaveLicense(customer);

        var refused = Assert.Throws<RegisterIntegrityException>(
            () => manager.Register.DeleteCustomer(customer.CustomerId));

        Assert.Equal(StatusCatalog.CustomerStillHasLicences, refused.Key);

        // ⭐ The count travels with it, so the operator is told what stands in the way.
        Assert.Contains("1", refused.Arguments.Select(a => a?.ToString()));

        // ⛔ AND NOTHING HAPPENED. A refusal that half-executed would be worse than none.
        Assert.NotNull(manager.Register.GetCustomer(customer.CustomerId));
        Assert.Empty(manager.Register.GetAudit(new AuditQuery { Action = "customer.deleted" }));
    }

    /// <summary>⚠ Removing something that is not there is a refusal, not a silent success.</summary>
    [Fact]
    public void AnUnknownCustomerIsRefused()
    {
        using var manager = new ManagerFixture();

        var refused = Assert.Throws<RegisterIntegrityException>(
            () => manager.Register.DeleteCustomer("c-9999"));

        Assert.Equal(StatusCatalog.CustomerNotInRegister, refused.Key);
    }

    /// <summary>⭐ The count the view model asks for is the count the register would refuse over.</summary>
    [Fact]
    public void TheLicenceCountIsWhatTheRefusalRestsOn()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();

        Assert.Equal(0, manager.Register.CountLicenses(customer.CustomerId));

        manager.SaveLicense(customer);
        manager.SaveLicense(customer);

        Assert.Equal(2, manager.Register.CountLicenses(customer.CustomerId));
    }

    // ── The view model ──────────────────────────────────────────────────────────────────────────────

    /// <summary>⛔⛔ Cancelling the confirmation removes NOTHING.</summary>
    [Fact]
    public async Task CancellingTheConfirmationRemovesNothing()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();

        var shell = Shell(manager, confirmed: false);
        shell.SelectedCustomer = shell.Customers.Single();

        await shell.RemoveCustomerCommand.ExecuteAsync(null);

        Assert.NotNull(manager.Register.GetCustomer(customer.CustomerId));
        Assert.Single(shell.Customers);
    }

    /// <summary>
    /// ⛔⛔ <b>With no confirmer wired the command REFUSES rather than proceeding.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ The rule L6.1a's <c>Forget settings</c> established: a destructive act must not lose its guard
    /// because a view forgot to attach one, with every test still green.
    /// </remarks>
    [Fact]
    public async Task WithNoConfirmerNothingIsRemoved()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();

        var shell = Shell(manager, confirmed: true);
        shell.Confirm = null;
        shell.SelectedCustomer = shell.Customers.Single();

        await shell.RemoveCustomerCommand.ExecuteAsync(null);

        Assert.NotNull(manager.Register.GetCustomer(customer.CustomerId));
        Assert.Equal(StatusCatalog.ConfirmationUnavailableNothingSent, shell.Message?.Key);
    }

    /// <summary>⭐ Confirmed, and the customer is gone from the register AND from the list.</summary>
    [Fact]
    public async Task ConfirmingRemovesTheCustomerAndTheList()
    {
        using var manager = new ManagerFixture();
        var doomed = manager.SaveCustomer("Gamma Sp.j.");
        manager.SaveCustomer("Delta Sp. z o.o.");

        var shell = Shell(manager, confirmed: true);
        shell.SelectedCustomer = shell.Customers.Single(c => c.CustomerId == doomed.CustomerId);

        await shell.RemoveCustomerCommand.ExecuteAsync(null);

        Assert.Null(manager.Register.GetCustomer(doomed.CustomerId));
        Assert.DoesNotContain(shell.Customers, c => c.CustomerId == doomed.CustomerId);
        Assert.Single(shell.Customers);
        Assert.Equal(StatusCatalog.CustomerRemoved, shell.Message?.Key);

        // ⚠ And the FORM is cleared: the removed customer's fields left on screen would invite a Save
        //   that recreates them under the same identifier.
        Assert.Null(shell.SelectedCustomer);
        Assert.Empty(shell.CustomerName);
    }

    /// <summary>
    /// ⛔⛔ <b>A customer with licences never even reaches a confirmation.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ The sharp half is <c>asked</c>: a confirmation offered for something that cannot happen teaches
    /// the operator that confirmations are noise, and the operator would answer "yes" to a dialog whose
    /// only possible outcome was a refusal.
    /// </remarks>
    [Fact]
    public async Task ACustomerWithLicencesIsRefusedBeforeAnyConfirmation()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer("Epsilon S.A.");
        manager.SaveLicense(customer);

        var asked = false;
        var shell = Shell(manager, confirmed: true);
        shell.Confirm = _ =>
        {
            asked = true;
            return Task.FromResult(true);
        };

        shell.SelectedCustomer = shell.Customers.Single();

        await shell.RemoveCustomerCommand.ExecuteAsync(null);

        Assert.False(asked, "The operator was asked to confirm something that could not happen.");
        Assert.NotNull(manager.Register.GetCustomer(customer.CustomerId));
        Assert.Equal(StatusCatalog.CustomerStillHasLicences, shell.Message?.Key);

        // ⭐ And the sentence carries the customer and the count, so it can be acted on.
        Assert.Contains("Epsilon S.A.", shell.Message!.Arguments.Select(a => a?.ToString()));
        Assert.Contains("1", shell.Message.Arguments.Select(a => a?.ToString()));
    }

    /// <summary>⚠ Nothing selected is a nudge, not a refusal — and it removes nothing either.</summary>
    [Fact]
    public async Task WithNothingSelectedItSaysSoAndRemovesNothing()
    {
        using var manager = new ManagerFixture();
        manager.SaveCustomer();

        var shell = Shell(manager, confirmed: true);
        shell.SelectedCustomer = null;

        await shell.RemoveCustomerCommand.ExecuteAsync(null);

        Assert.Equal(StatusCatalog.SelectCustomerToRemove, shell.Message?.Key);
        Assert.Single(manager.Register.GetCustomers());
    }

    private static ShellViewModel Shell(ManagerFixture manager, bool confirmed)
    {
        var shell = new ShellViewModel(
            manager.Register, manager.Session, manager.Paths, () => manager.Now);

        shell.Confirm = _ => Task.FromResult(confirmed);
        return shell;
    }
}
