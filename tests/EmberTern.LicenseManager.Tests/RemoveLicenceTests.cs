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
/// ⭐⭐ <b>Removing a licence — and the measurement that decides what "removing" can even mean.</b>
///
/// <para>⚠⚠ <b>THE SCHEMA CHOOSES BETWEEN TWO OUTCOMES, not a policy of ours.</b>
/// <see cref="TheSchemaItselfIsWhatForcesTwoOutcomes"/> reaches past this application's API and measures it
/// on a real register: <c>DELETE FROM licenses</c> succeeds for a licence that was never issued, and fails
/// with <c>SQLITE_CONSTRAINT_FOREIGNKEY</c> for one that was. ⛔ And it cannot be worked around —
/// <c>issued_artifacts</c> refuses deletion by trigger, so there is no order of operations that removes an
/// issued licence.</para>
///
/// <list type="bullet">
///   <item><b>Never issued</b> → the row is DELETED.</item>
///   <item><b>Ever issued</b> → the row is RETIRED: it leaves every active read, and its artifacts,
///   its current-artifact pointer and its whole audit trail are untouched.</item>
/// </list>
///
/// <para>⭐ In both cases the audit log GAINS a line and loses nothing: it is append-only at the database,
/// which is why "the history survives" is a property of the schema rather than a promise of this code.</para>
/// </summary>
public sealed class RemoveLicenceTests
{
    // ── The measurement the whole design rests on ───────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Measured, not assumed: an issued licence CANNOT be deleted, and a never-issued one can.</b>
    /// </summary>
    /// <remarks>
    /// <para>⚠ It reaches past <see cref="LicenseRegister"/>'s own API and talks to the connection, which is
    /// exactly the shape L3 used to prove the append-only triggers. The claim is about the SCHEMA, and a
    /// test that went through our own method would only prove what our own method does.</para>
    /// <para>⛔ If this ever goes green in the other direction, the two-outcome design below has lost its
    /// reason and should be reconsidered rather than kept out of habit.</para>
    /// </remarks>
    [Fact]
    public void TheSchemaItselfIsWhatForcesTwoOutcomes()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();

        var bare = manager.SaveLicense(customer);
        var issued = manager.SaveLicense(customer);
        manager.Workflow.Issue(manager.Session, issued, customer, IssueReasons.Initial);

        var connection = (SqliteConnection)typeof(LicenseRegister)
            .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager.Register)!;

        // ⭐ Foreign keys are ON, which is what makes the refusal below happen at all.
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys;";
            Assert.Equal(1L, Convert.ToInt64(pragma.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
        }

        // ── never issued: the row goes ──
        using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM licenses WHERE lid = $lid;";
            delete.Parameters.AddWithValue("$lid", bare.LicenseId);
            Assert.Equal(1, delete.ExecuteNonQuery());
        }

        // ── issued: the row cannot ──
        using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM licenses WHERE lid = $lid;";
            delete.Parameters.AddWithValue("$lid", issued.LicenseId);

            var refused = Assert.Throws<SqliteException>(() => delete.ExecuteNonQuery());
            Assert.Equal(19, refused.SqliteErrorCode);          // SQLITE_CONSTRAINT
            Assert.Equal(787, refused.SqliteExtendedErrorCode); // …_FOREIGNKEY
        }

        // ⭐ And nothing was orphaned by the delete that DID work.
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA foreign_key_check;";
        using var reader = check.ExecuteReader();
        Assert.False(reader.Read(), "The register was left with a dangling reference.");
    }

    /// <summary>
    /// ⭐⭐ <b>A register written by the PREVIOUS build opens, migrates, and keeps everything.</b>
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ <b>The one failure in this change that would reach a real operator.</b> Every other test
    /// here builds a register at the current schema, so all of them would stay green over a migration step
    /// that threw — and the person who found out would be somebody whose existing <c>licenses.db</c>
    /// suddenly refused to open. ⭐ So this makes a genuine v2 file: it drops the column and winds
    /// <c>schema_meta</c> back, exactly as a file from before this change looks.</para>
    /// <para>⭐ <c>retired_at</c> is nullable with no backfill, so every existing licence reads as active —
    /// which is the property that makes the migration a single <c>ALTER TABLE</c> rather than a data step.</para>
    /// </remarks>
    [Fact]
    public void ARegisterFromThePreviousSchemaOpensAndKeepsEverything()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer("ACME Sp. z o.o.");
        var licence = manager.SaveLicense(customer);
        manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);

        var path = manager.Paths.Register;
        manager.Register.Dispose();

        // ── wind the file back to schema 2 ──
        using (var raw = new SqliteConnection($"Data Source={path}"))
        {
            raw.Open();

            // ⚠ BOTH columns, or this is not a v2 file — schema 3 added the licence's and schema 4 the
            //   customer's. Dropping one and claiming version 2 would send the migration into an ALTER
            //   TABLE for a column that already exists, and the test would fail for its own reason rather
            //   than for the register's.
            using var down = raw.CreateCommand();
            down.CommandText =
                "ALTER TABLE licenses DROP COLUMN retired_at;" +
                "ALTER TABLE customers DROP COLUMN retired_at;" +
                "UPDATE schema_meta SET value = '2' WHERE key = 'version';";
            down.ExecuteNonQuery();
        }

        // ── and open it with this build ──
        using var upgraded = LicenseRegister.Open(path, () => manager.Now, actor: "tester");

        Assert.Equal(LicenseRegister.CurrentSchemaVersion, upgraded.SchemaVersion);

        // ⭐ Everything is still there, and every existing row reads as active.
        var carriedCustomer = Assert.Single(upgraded.GetCustomers());
        Assert.False(carriedCustomer.IsRetired);

        var carried = Assert.Single(upgraded.GetLicenses(customer.CustomerId));
        Assert.Equal(licence.LicenseId, carried.LicenseId);
        Assert.False(carried.IsRetired);
        Assert.Null(carried.RetiredAt);
        Assert.Single(upgraded.GetArtifacts(licence.LicenseId));

        // ⭐ And the new operation works on the upgraded file.
        Assert.Equal(RemovalOutcome.Retired, upgraded.RemoveLicense(licence.LicenseId));
        Assert.Empty(upgraded.GetLicenses(customer.CustomerId));
        Assert.True(upgraded.GetLicense(licence.LicenseId)!.IsRetired);
    }

    // ── The register ────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ A licence that was never issued is DELETED, and the history still records it existed.</summary>
    [Fact]
    public void ANeverIssuedLicenceIsDeleted()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        var licence = manager.SaveLicense(customer);

        Assert.Equal(RemovalOutcome.Deleted, manager.Register.RemoveLicense(licence.LicenseId));

        Assert.Null(manager.Register.GetLicense(licence.LicenseId));
        Assert.Empty(manager.Register.GetLicenses(customer.CustomerId));
        Assert.Empty(manager.Register.GetAllLicenses());

        var line = Assert.Single(
            manager.Register.GetAudit(new AuditQuery { Action = "licence.deleted" }));

        Assert.Equal(licence.LicenseId, line.TargetId);
        Assert.NotNull(line.BeforeJson);
        Assert.Null(line.AfterJson);

        // ⭐ THE CUSTOMER SURVIVES. Removing a licence is not removing whose it was.
        Assert.NotNull(manager.Register.GetCustomer(customer.CustomerId));
    }

    /// <summary>
    /// ⭐⭐ <b>An issued licence is RETIRED — and every artifact, the pointer and the audit trail stay.</b>
    /// </summary>
    [Fact]
    public void AnIssuedLicenceIsRetiredAndKeepsEverything()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        var licence = manager.SaveLicense(customer);

        manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);
        manager.Now = manager.Now.AddDays(1);
        manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.ReissueLost);

        var artifactsBefore = manager.Register.GetArtifacts(licence.LicenseId).Count;
        var pointerBefore = manager.Register.GetCurrentArtifact(licence.LicenseId);
        var auditBefore = manager.Register.GetAudit(new AuditQuery { Limit = 10_000 }).Count;

        Assert.Equal(2, artifactsBefore);
        Assert.NotNull(pointerBefore);

        Assert.Equal(RemovalOutcome.Retired, manager.Register.RemoveLicense(licence.LicenseId));

        // ⭐⭐ EVERYTHING THAT WAS EVER ISSUED IS STILL THERE, byte for byte.
        var artifactsAfter = manager.Register.GetArtifacts(licence.LicenseId);
        Assert.Equal(artifactsBefore, artifactsAfter.Count);

        var pointerAfter = manager.Register.GetCurrentArtifact(licence.LicenseId);
        Assert.NotNull(pointerAfter);
        Assert.Equal(pointerBefore!.ArtifactId, pointerAfter!.ArtifactId);
        Assert.Equal(pointerBefore.Token, pointerAfter.Token);

        // ⭐ The audit log only GREW — it is append-only at the database and cannot do anything else.
        var auditAfter = manager.Register.GetAudit(new AuditQuery { Limit = 10_000 });
        Assert.True(auditAfter.Count > auditBefore);

        var line = Assert.Single(
            manager.Register.GetAudit(new AuditQuery { Action = "licence.retired" }));
        Assert.Equal(licence.LicenseId, line.TargetId);
        Assert.NotNull(line.BeforeJson);
        Assert.NotNull(line.AfterJson);

        // ⭐ The row is still there, and it is marked.
        var row = manager.Register.GetLicense(licence.LicenseId);
        Assert.NotNull(row);
        Assert.True(row!.IsRetired);
        Assert.NotNull(row.RetiredAt);

        // ⛔⛔ AND IT IS GONE FROM EVERY ACTIVE READ.
        Assert.Empty(manager.Register.GetLicenses(customer.CustomerId));
        Assert.DoesNotContain(
            manager.Register.QueryLicenses(), s => s.License.LicenseId == licence.LicenseId);

        // ⭐ The customer survives, and so does the register's integrity.
        Assert.NotNull(manager.Register.GetCustomer(customer.CustomerId));
    }

    /// <summary>⛔ A retired licence cannot be edited, and cannot be retired twice.</summary>
    /// <remarks>
    /// ⚠ The first half is the sharp one: the upsert does not name <c>retired_at</c>, so without the guard
    /// a save would quietly change a row the operator cannot see and leave it retired.
    /// </remarks>
    [Fact]
    public void ARetiredLicenceIsClosedToEverything()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        var licence = manager.SaveLicense(customer);
        manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);

        manager.Register.RemoveLicense(licence.LicenseId);

        var edit = Assert.Throws<RegisterIntegrityException>(
            () => manager.Register.SaveLicense(licence with { Seats = 99 }));
        Assert.Equal(StatusCatalog.LicenceIsRetired, edit.Key);

        var again = Assert.Throws<RegisterIntegrityException>(
            () => manager.Register.RemoveLicense(licence.LicenseId));
        Assert.Equal(StatusCatalog.LicenceIsRetired, again.Key);

        // ⛔ And the refused edit really did nothing.
        Assert.Equal(licence.Seats, manager.Register.GetLicense(licence.LicenseId)!.Seats);
    }

    /// <summary>⚠ Removing something that is not there is a refusal, not a silent success.</summary>
    [Fact]
    public void AnUnknownLicenceIsRefused()
    {
        using var manager = new ManagerFixture();

        var refused = Assert.Throws<RegisterIntegrityException>(
            () => manager.Register.RemoveLicense("no-such-licence"));

        Assert.Equal(StatusCatalog.LicenceNotInRegister, refused.Key);
    }

    /// <summary>
    /// ⭐⭐ <b>Retirement survives the JSONL escape hatch.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ That export is the file somebody reads when the application will not open, and a licence that
    /// came back from it without this field would silently return to the ACTIVE register — rule #11: the
    /// export may not know less than the database.
    /// </remarks>
    [Fact]
    public void RetirementTravelsInTheJsonlExport()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();

        var kept = manager.SaveLicense(customer);
        var retired = manager.SaveLicense(customer);
        manager.Workflow.Issue(manager.Session, retired, customer, IssueReasons.Initial);
        manager.Register.RemoveLicense(retired.LicenseId);

        var lines = RegisterJsonl.Export(manager.Register);
        var text = string.Join("\n", lines);

        Assert.Contains("retiredAt", text, StringComparison.Ordinal);

        // ⭐ And only on the one that IS retired — a field written unconditionally would say nothing.
        // ⚠ The export writes a `lid` on artifacts and pointers too, so the licence LINE is the one whose
        //   type says so — `RegisterJsonl.LicenseType`, never a typed-out word.
        var retiredLine = lines.Single(l => l.Contains(retired.LicenseId, StringComparison.Ordinal)
            && l.Contains($"\"type\":\"{RegisterJsonl.LicenseType}\"", StringComparison.Ordinal));
        var keptLine = lines.Single(l => l.Contains(kept.LicenseId, StringComparison.Ordinal)
            && l.Contains($"\"type\":\"{RegisterJsonl.LicenseType}\"", StringComparison.Ordinal));

        // ⚠ The field is ALWAYS written — `WriteOptional` emits an explicit `null` rather than omitting the
        //   property — so the claim is about the VALUE, not about the key being present. ⭐ Which is the
        //   better shape anyway: a reader of this file always sees the field and never has to know whether
        //   its absence means "not retired" or "written by an older build".
        Assert.DoesNotContain("\"retiredAt\":null", retiredLine, StringComparison.Ordinal);
        Assert.Contains("\"retiredAt\":\"", retiredLine, StringComparison.Ordinal);
        Assert.Contains("\"retiredAt\":null", keptLine, StringComparison.Ordinal);
    }

    // ── The customer rule, which retirement does NOT change ─────────────────────────────────────────

    /// <summary>
    /// ⭐ A customer whose only licence was DELETED can then be removed. (The user's cases 1, 2 and 7.)
    /// </summary>
    [Fact]
    public void RemovingTheLastNeverIssuedLicenceMakesTheCustomerRemovable()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        var licence = manager.SaveLicense(customer);

        Assert.Equal(1, manager.Register.CountLicenses(customer.CustomerId));

        manager.Register.RemoveLicense(licence.LicenseId);

        Assert.Equal(0, manager.Register.CountLicenses(customer.CustomerId));
        manager.Register.RemoveCustomer(customer.CustomerId);
        Assert.Null(manager.Register.GetCustomer(customer.CustomerId));
    }

    /// <summary>
    /// ⚠⚠ <b>Retiring a licence makes its customer RETIREABLE, not DELETABLE — and the difference is the
    /// foreign key rather than a rule of ours.</b>
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ <b>This test asserted the opposite until the customer side was repaired, and the change is
    /// the whole repair.</b> It used to demand a REFUSAL here, which is exactly the dead end a real
    /// operator hit: every licence removed, the list empty on screen, and the customer still unremovable
    /// with a message naming licences they could no longer see.</para>
    /// <para>⭐ What has NOT changed is why: the retired licence is still a row pointing at the customer,
    /// so <c>DELETE FROM customers</c> still fails on it. <c>CountLicenses</c> therefore still counts
    /// retired rows — it is what decides that this must be a retirement rather than a delete.
    /// <c>CountActiveLicenses</c> is the separate number that decides whether the operation is allowed at
    /// all. ⛔ Collapsing the two is what would put the failure back in the database's hands.</para>
    /// </remarks>
    [Fact]
    public void RetiringALicenceMakesItsCustomerRetireableNotDeletable()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        var licence = manager.SaveLicense(customer);
        manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);

        manager.Register.RemoveLicense(licence.LicenseId);

        // ⭐ The row the foreign key sees is still there; the licence the operator sees is not.
        Assert.Equal(1, manager.Register.CountLicenses(customer.CustomerId));
        Assert.Equal(0, manager.Register.CountActiveLicenses(customer.CustomerId));

        // ⭐⭐ So the customer CAN now be removed — as a retirement, which is the only thing the schema
        //    allows. ⛔ Never as a DELETE that the database would refuse a moment later.
        Assert.Equal(RemovalOutcome.Retired, manager.Register.RemoveCustomer(customer.CustomerId));

        Assert.True(manager.Register.GetCustomer(customer.CustomerId)!.IsRetired);
        Assert.Empty(manager.Register.GetCustomers());

        // ⭐ And the licence history is untouched.
        Assert.Single(manager.Register.GetAllLicenses());
        Assert.Single(manager.Register.GetArtifacts(licence.LicenseId));
    }

    /// <summary>⭐ A customer with OTHER licences still cannot be removed. (The user's case 8.)</summary>
    [Fact]
    public void ACustomerWithOtherLicencesStillCannotBeRemoved()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        var first = manager.SaveLicense(customer);
        manager.SaveLicense(customer);

        manager.Register.RemoveLicense(first.LicenseId);

        Assert.Equal(1, manager.Register.CountLicenses(customer.CustomerId));
        Assert.Throws<RegisterIntegrityException>(
            () => manager.Register.RemoveCustomer(customer.CustomerId));
    }

    // ── The view model ──────────────────────────────────────────────────────────────────────────────

    /// <summary>⛔⛔ Cancelling the confirmation removes NOTHING. (The user's case 6.)</summary>
    [Fact]
    public async Task CancellingTheConfirmationRemovesNothing()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        var licence = manager.SaveLicense(customer);

        var shell = Shell(manager, confirmed: false);
        shell.SelectedCustomer = shell.Customers.Single();
        shell.SelectedLicense = shell.Licenses.Single();

        await shell.RemoveLicenceCommand.ExecuteAsync(null);

        Assert.NotNull(manager.Register.GetLicense(licence.LicenseId));
        Assert.Single(shell.Licenses);
    }

    /// <summary>⛔ With no confirmer wired the command REFUSES rather than proceeding.</summary>
    [Fact]
    public async Task WithNoConfirmerNothingIsRemoved()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        var licence = manager.SaveLicense(customer);

        var shell = Shell(manager, confirmed: true);
        shell.Confirm = null;
        shell.SelectedCustomer = shell.Customers.Single();
        shell.SelectedLicense = shell.Licenses.Single();

        await shell.RemoveLicenceCommand.ExecuteAsync(null);

        Assert.NotNull(manager.Register.GetLicense(licence.LicenseId));
        Assert.Equal(StatusCatalog.ConfirmationUnavailableNothingSent, shell.Message?.Key);
    }

    /// <summary>
    /// ⭐⭐ <b>The operator is told WHICH of the two acts they are confirming, before it happens.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ Two different sentences, because they are two different acts: one erases a row that never
    /// produced anything, the other retires a row whose artifacts are permanent. A single hedged sentence
    /// would be vague about exactly the part that matters.
    /// </remarks>
    [Fact]
    public async Task TheConfirmationSaysWhichOfTheTwoWillHappen()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        var bare = manager.SaveLicense(customer);
        var issued = manager.SaveLicense(customer);
        manager.Workflow.Issue(manager.Session, issued, customer, IssueReasons.Initial);

        ConfirmRequest? asked = null;
        var shell = Shell(manager, confirmed: true);
        shell.Confirm = request =>
        {
            asked = request;
            return Task.FromResult(true);
        };

        shell.SelectedCustomer = shell.Customers.Single();

        shell.SelectedLicense = shell.Licenses.Single(l => l.LicenseId == bare.LicenseId);
        await shell.RemoveLicenceCommand.ExecuteAsync(null);
        Assert.Equal(ConfirmCatalog.RemoveLicenceNeverIssuedMessage, asked?.Message);
        Assert.Equal(StatusCatalog.LicenceDeleted, shell.Message?.Key);

        shell.SelectedLicense = shell.Licenses.Single(l => l.LicenseId == issued.LicenseId);
        await shell.RemoveLicenceCommand.ExecuteAsync(null);
        Assert.Equal(ConfirmCatalog.RemoveLicenceIssuedMessage, asked?.Message);
        Assert.Equal(StatusCatalog.LicenceRetired, shell.Message?.Key);

        // ⭐ The count of artifacts travels into the sentence, so "the history is kept" is specific.
        Assert.Contains("1", asked!.MessageArguments.Select(a => a?.ToString()));
    }

    /// <summary>
    /// ⭐⭐ <b>The removed licence is gone from the ACTIVE view. (The user's case 5.)</b>
    /// </summary>
    /// <remarks>
    /// ⚠ Asserted on BOTH surfaces that list licences — the customer's own panel and the licences view —
    /// because they read the register through two different methods, and a retirement that only one of
    /// them honoured would be a licence that is gone from one screen and tickable on the other.
    /// </remarks>
    [Fact]
    public async Task ARemovedLicenceLeavesEveryActiveList()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        var licence = manager.SaveLicense(customer);
        manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);

        var shell = Shell(manager, confirmed: true);
        shell.SelectedCustomer = shell.Customers.Single();
        shell.SelectedLicense = shell.Licenses.Single();

        Assert.Single(shell.Browser.Results);

        await shell.RemoveLicenceCommand.ExecuteAsync(null);

        Assert.Empty(shell.Licenses);
        Assert.Null(shell.SelectedLicense);

        shell.Browser.Refresh();
        Assert.Empty(shell.Browser.Results);

        // ⛔ And it is therefore unreachable to both bulk operations, which read exactly that query.
        Assert.Empty(shell.Browser.CheckedIds);
    }

    private static ShellViewModel Shell(ManagerFixture manager, bool confirmed)
    {
        var shell = new ShellViewModel(
            manager.Register, manager.Session, manager.Paths, () => manager.Now);

        shell.Confirm = _ => Task.FromResult(confirmed);
        return shell;
    }
}
