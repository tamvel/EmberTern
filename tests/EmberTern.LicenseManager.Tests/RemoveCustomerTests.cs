using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.ViewModels;
using Microsoft.Data.Sqlite;
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

        manager.Register.RemoveCustomer(customer.CustomerId);

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

    /// <summary>
    /// ⚠⚠ <b>THE MEASUREMENT THIS WHOLE REPAIR RESTS ON: a customer whose every licence is RETIRED still
    /// cannot be deleted.</b>
    /// </summary>
    /// <remarks>
    /// <para>⭐ A retired licence is still a ROW in <c>licenses</c> pointing at the customer, so the foreign
    /// key holds exactly as it does for an active one. That is why "just stop counting retired licences"
    /// is the wrong fix: the counter would read zero, the application would attempt a <c>DELETE</c>, and
    /// SQLite would refuse it on a row the operator was never shown.</para>
    /// <para>⛔ It talks to the connection rather than through our own API, because the claim is about the
    /// SCHEMA. ⭐ If this ever stops throwing, the retirement branch has lost its reason.</para>
    /// </remarks>
    [Fact]
    public void ACustomerWhoseLicencesAreAllRetiredStillCannotBeDeleted()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        var licence = manager.SaveLicense(customer);
        manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);

        manager.Register.RemoveLicense(licence.LicenseId);

        // ⭐ The customer's list is empty on every surface…
        Assert.Empty(manager.Register.GetLicenses(customer.CustomerId));
        Assert.Equal(0, manager.Register.CountActiveLicenses(customer.CustomerId));

        // …and the row is still there, which is the whole point.
        Assert.Equal(1, manager.Register.CountLicenses(customer.CustomerId));

        var connection = (SqliteConnection)typeof(LicenseRegister)
            .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager.Register)!;

        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM customers WHERE customer_id = $id;";
        delete.Parameters.AddWithValue("$id", customer.CustomerId);

        var refused = Assert.Throws<SqliteException>(() => delete.ExecuteNonQuery());
        Assert.Equal(19, refused.SqliteErrorCode);
        Assert.Equal(787, refused.SqliteExtendedErrorCode);
    }

    /// <summary>⭐ Only retired licences → the customer is RETIRED, and everything is kept. (Case 3.)</summary>
    [Fact]
    public void ACustomerWithOnlyRetiredLicencesIsRetired()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer("Zeta Sp. z o.o.");
        var licence = manager.SaveLicense(customer);
        manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);
        manager.Register.RemoveLicense(licence.LicenseId);

        var auditBefore = manager.Register.GetAudit(new AuditQuery { Limit = 10_000 }).Count;

        Assert.Equal(RemovalOutcome.Retired, manager.Register.RemoveCustomer(customer.CustomerId));

        // ⛔ Gone from the active list… (case 6)
        Assert.DoesNotContain(manager.Register.GetCustomers(), c => c.CustomerId == customer.CustomerId);

        // …and still in the register, marked.
        var row = manager.Register.GetCustomer(customer.CustomerId);
        Assert.NotNull(row);
        Assert.True(row!.IsRetired);
        Assert.NotNull(row.RetiredAt);

        // ⭐ The historical licence and its artifact are untouched. (Case 8.)
        Assert.Single(manager.Register.GetAllLicenses());
        Assert.Single(manager.Register.GetArtifacts(licence.LicenseId));

        // ⭐ And the audit only GREW. (Case 9.)
        Assert.True(manager.Register.GetAudit(new AuditQuery { Limit = 10_000 }).Count > auditBefore);

        var line = Assert.Single(
            manager.Register.GetAudit(new AuditQuery { Action = "customer.retired" }));
        Assert.Equal(customer.CustomerId, line.TargetId);
        Assert.NotNull(line.BeforeJson);
        Assert.NotNull(line.AfterJson);
    }

    /// <summary>⭐ Several retired licences → still a retirement, not a delete. (Case 4.)</summary>
    [Fact]
    public void ACustomerWithSeveralRetiredLicencesIsRetired()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();

        foreach (var _ in new[] { 1, 2, 3 })
        {
            var licence = manager.SaveLicense(customer);
            manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);
            manager.Register.RemoveLicense(licence.LicenseId);
            manager.Now = manager.Now.AddMinutes(1);
        }

        Assert.Equal(3, manager.Register.CountLicenses(customer.CustomerId));
        Assert.Equal(0, manager.Register.CountActiveLicenses(customer.CustomerId));

        Assert.Equal(RemovalOutcome.Retired, manager.Register.RemoveCustomer(customer.CustomerId));
        Assert.Equal(3, manager.Register.GetAllLicenses().Count);
    }

    /// <summary>⛔ One active licence beside a retired one is still a refusal. (Case 5.)</summary>
    /// <remarks>
    /// ⚠ And the refusal names ONE, not two: the operator can only act on what they can see, so a count
    /// that included the retired licence would send them looking for a licence nothing lists.
    /// </remarks>
    [Fact]
    public void AnActiveLicenceBesideARetiredOneIsStillARefusal()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer("Eta S.A.");

        var retired = manager.SaveLicense(customer);
        manager.Workflow.Issue(manager.Session, retired, customer, IssueReasons.Initial);
        manager.Register.RemoveLicense(retired.LicenseId);

        manager.SaveLicense(customer);

        var refused = Assert.Throws<RegisterIntegrityException>(
            () => manager.Register.RemoveCustomer(customer.CustomerId));

        Assert.Equal(StatusCatalog.CustomerStillHasLicences, refused.Key);
        Assert.Contains("1", refused.Arguments.Select(a => a?.ToString()));
        Assert.DoesNotContain("2", refused.Arguments.Select(a => a?.ToString()));

        Assert.False(manager.Register.GetCustomer(customer.CustomerId)!.IsRetired);
    }

    /// <summary>
    /// ⭐ A retired customer stays retired across a restart, and is closed to writes. (Cases 7 and 10.)
    /// </summary>
    /// <remarks>
    /// ⚠ Reopened from the FILE, which is what "after a restart" means — re-reading through the instance
    /// that wrote it would prove nothing about what was persisted.
    /// </remarks>
    [Fact]
    public void ARetiredCustomerSurvivesARestartAndIsClosedToWrites()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer("Theta Sp.j.");
        var licence = manager.SaveLicense(customer);
        manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);
        manager.Register.RemoveLicense(licence.LicenseId);
        manager.Register.RemoveCustomer(customer.CustomerId);

        var path = manager.Paths.Register;
        manager.Register.Dispose();

        using var reopened = LicenseRegister.Open(path, () => manager.Now, actor: "tester");

        // ⛔ Still gone from the active list. (Case 7.)
        Assert.Empty(reopened.GetCustomers());
        Assert.True(reopened.GetCustomer(customer.CustomerId)!.IsRetired);

        // ⭐ And still there for anyone reading everything.
        Assert.Single(reopened.GetAllCustomers());

        // ⛔ Not editable… (case 10)
        var edit = Assert.Throws<RegisterIntegrityException>(
            () => reopened.SaveCustomer(customer with { Name = "Renamed" }));
        Assert.Equal(StatusCatalog.CustomerIsRetired, edit.Key);

        // ⛔ …and no new licence can be written for them.
        var licenceForGhost = Assert.Throws<RegisterIntegrityException>(
            () => reopened.SaveLicense(new LicenseRecord
            {
                LicenseId = EmberTern.Licensing.Issuing.LicenseIssuer.NewLicenseId(),
                CustomerId = customer.CustomerId,
                Product = EmberTern.Licensing.LicenseConstants.ProductId,
                Seats = 1,
                NotBefore = manager.Now,
                ExpiresAt = manager.Now.AddYears(1),
                Status = LicenseStatuses.Active,
            }));
        Assert.Equal(StatusCatalog.CustomerIsRetired, licenceForGhost.Key);

        // ⛔ And not retired twice.
        var again = Assert.Throws<RegisterIntegrityException>(
            () => reopened.RemoveCustomer(customer.CustomerId));
        Assert.Equal(StatusCatalog.CustomerIsRetired, again.Key);
    }

    /// <summary>⭐⭐ Retirement travels in the JSONL escape hatch, exactly as a licence's does.</summary>
    [Fact]
    public void RetirementTravelsInTheJsonlExport()
    {
        using var manager = new ManagerFixture();
        var kept = manager.SaveCustomer("Iota S.A.");
        var retired = manager.SaveCustomer("Kappa Sp. z o.o.");

        var licence = manager.SaveLicense(retired);
        manager.Workflow.Issue(manager.Session, licence, retired, IssueReasons.Initial);
        manager.Register.RemoveLicense(licence.LicenseId);
        manager.Register.RemoveCustomer(retired.CustomerId);

        var lines = RegisterJsonl.Export(manager.Register);

        // ⛔⛔ THE RETIRED CUSTOMER IS IN THE FILE. An export that used the filtered read would leave them
        //    out of the one document that exists for when this application will not open.
        var retiredLine = lines.Single(l => l.Contains(retired.CustomerId, StringComparison.Ordinal)
            && l.Contains($"\"type\":\"{RegisterJsonl.CustomerType}\"", StringComparison.Ordinal));
        var keptLine = lines.Single(l => l.Contains(kept.CustomerId, StringComparison.Ordinal)
            && l.Contains($"\"type\":\"{RegisterJsonl.CustomerType}\"", StringComparison.Ordinal));

        Assert.Contains("\"retiredAt\":\"", retiredLine, StringComparison.Ordinal);
        Assert.Contains("\"retiredAt\":null", keptLine, StringComparison.Ordinal);
    }

    /// <summary>⛔ A customer with a licence is REFUSED, and the refusal names the obstacle.</summary>
    [Fact]
    public void ACustomerWithALicenceIsRefused()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer("Beta S.A.");
        manager.SaveLicense(customer);

        var refused = Assert.Throws<RegisterIntegrityException>(
            () => manager.Register.RemoveCustomer(customer.CustomerId));

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
            () => manager.Register.RemoveCustomer("c-9999"));

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

    /// <summary>
    /// ⭐⭐ <b>The operator is told WHICH of the two acts they are confirming — and the retirement branch
    /// reaches the register.</b> (Cases 3, 6 and 11 at the view-model level.)
    /// </summary>
    [Fact]
    public async Task TheConfirmationSaysWhichOfTheTwoWillHappen()
    {
        using var manager = new ManagerFixture();

        var bare = manager.SaveCustomer("Lambda Sp. z o.o.");
        var withHistory = manager.SaveCustomer("Mu S.A.");
        var licence = manager.SaveLicense(withHistory);
        manager.Workflow.Issue(manager.Session, licence, withHistory, IssueReasons.Initial);
        manager.Register.RemoveLicense(licence.LicenseId);

        ConfirmRequest? asked = null;
        var shell = Shell(manager, confirmed: true);
        shell.Confirm = request =>
        {
            asked = request;
            return Task.FromResult(true);
        };

        shell.SelectedCustomer = shell.Customers.Single(c => c.CustomerId == bare.CustomerId);
        await shell.RemoveCustomerCommand.ExecuteAsync(null);
        Assert.Equal(ConfirmCatalog.RemoveCustomerMessage, asked?.Message);
        Assert.Equal(StatusCatalog.CustomerRemoved, shell.Message?.Key);

        shell.SelectedCustomer = shell.Customers.Single(c => c.CustomerId == withHistory.CustomerId);
        await shell.RemoveCustomerCommand.ExecuteAsync(null);
        Assert.Equal(ConfirmCatalog.RetireCustomerMessage, asked?.Message);
        Assert.Equal(StatusCatalog.CustomerRetired, shell.Message?.Key);

        // ⛔ Gone from the list the operator reads…
        Assert.Empty(shell.Customers);

        // …and still in the register, with its licence history.
        Assert.True(manager.Register.GetCustomer(withHistory.CustomerId)!.IsRetired);
        Assert.Single(manager.Register.GetAllLicenses());
    }

    /// <summary>
    /// ⛔⛔ <b>Cancelling the RETIREMENT changes nothing either.</b> (Case 11, on the branch that was added.)
    /// </summary>
    [Fact]
    public async Task CancellingTheRetirementChangesNothing()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        var licence = manager.SaveLicense(customer);
        manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);
        manager.Register.RemoveLicense(licence.LicenseId);

        var shell = Shell(manager, confirmed: false);
        shell.SelectedCustomer = shell.Customers.Single();

        await shell.RemoveCustomerCommand.ExecuteAsync(null);

        Assert.False(manager.Register.GetCustomer(customer.CustomerId)!.IsRetired);
        Assert.Single(shell.Customers);
        Assert.Empty(manager.Register.GetAudit(new AuditQuery { Action = "customer.retired" }));
    }

    private static ShellViewModel Shell(ManagerFixture manager, bool confirmed)
    {
        var shell = new ShellViewModel(
            manager.Register, manager.Session, manager.Paths, () => manager.Now);

        shell.Confirm = _ => Task.FromResult(confirmed);
        return shell;
    }
}
