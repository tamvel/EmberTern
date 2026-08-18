using System;
using System.Linq;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// Extending many licences to one date, end to end through the real register and the real issuer.
///
/// <para>⭐ Every assertion here is about an EFFECT the operator or the register would see — an artifact
/// that exists, an audit line that says something, a selection that is still there to retry from. ⛔ None
/// of them asserts that a method was called.</para>
/// </summary>
public sealed class BatchRenewalTests
{
    [Fact]
    public void AWholeBatchIsRecordedAsOneAct()
    {
        using var manager = new ManagerFixture();
        var (shell, batch) = Shell(manager, licences: 3);

        TickEverything(shell);
        batch.TargetDate = new DateTime(2029, 6, 30);
        batch.ExtendCommand.Execute(null);

        foreach (var licence in manager.Register.QueryLicenses())
        {
            // ⭐ Both halves committed: the artifact exists AND the licence row carries the new expiry.
            //    A batch that moved one without the other would leave the register disagreeing with the
            //    file the customer is holding.
            Assert.Single(manager.Register.GetArtifacts(licence.License.LicenseId));
            Assert.Equal(
                LicenseDay.EndOf(new DateTime(2029, 6, 30)), licence.License.ExpiresAt);
        }

        var batches = manager.Register.GetAudit(new AuditQuery { Action = "licence.batch-issued" });
        Assert.Single(batches);
    }

    [Fact]
    public void EachLicencesOwnAuditLineSaysOnWhatTermsItWasIssued()
    {
        // ⭐⭐ D‑5. Before this stage a batch wrote only "batch <id>" onto every licence's `licence.issued`
        //     line, so the one thing the summary exists for — letting the audit answer "on what terms?"
        //     without joining anything — was exactly what twenty licences at a time lost.
        using var manager = new ManagerFixture();
        var (shell, batch) = Shell(manager, licences: 2, customerName: "ACME Sp. z o.o.");

        TickEverything(shell);
        batch.TargetDate = new DateTime(2029, 6, 30);
        batch.ExtendCommand.Execute(null);

        var issued = manager.Register.GetAudit(new AuditQuery { Action = "licence.issued" });
        Assert.Equal(2, issued.Count);

        foreach (var line in issued)
        {
            Assert.Contains("Licensed to ACME Sp. z o.o.", line.Note);
            Assert.Contains("5 seat(s)", line.Note);
            Assert.Contains("until 2029-06-30", line.Note);

            // ⚠ Appended, never instead of: the correlation marker has to survive alongside the terms.
            Assert.Contains("batch ", line.Note);
        }
    }

    [Fact]
    public void TheBatchAndTheSingleIssueWriteTheSameSentence()
    {
        // ⭐ One owner. Two spellings of "what was issued" is how an audit stops being comparable across
        //   the two paths that write it.
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer("ACME Sp. z o.o.");
        var licence = manager.SaveLicense(customer);

        manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);

        var single = Assert.Single(manager.Register.GetAudit(new AuditQuery { Action = "licence.issued" }));
        Assert.Equal(IssuingWorkflow.Summarise(customer, licence), single.Note);
    }

    [Fact]
    public void NothingIsIssuedWhenTheOPERATIONChangedSinceThePreviewWasBuilt()
    {
        // ⭐⭐ SEMANTIC ATOMICITY. The operator approved a specific operation, not a moment in time. Here
        //    one of the ticked licences is issued by another path between the preview being read and the
        //    button being pressed — so its reason would silently change from "Initial issue" to "Renewal",
        //    and the register would record something the preview never showed. The batch refuses instead.
        using var manager = new ManagerFixture();
        var (shell, batch) = Shell(manager, licences: 3);

        TickEverything(shell);
        batch.TargetDate = new DateTime(2029, 6, 30);

        Assert.True(batch.CanExtend);
        Assert.All(batch.Rows, row => Assert.Equal("Initial issue", row.ReasonLabel));

        // ⚠ Behind the preview's back: the customers view, or a second operator, issuing one of them.
        var customer = manager.Register.GetCustomers()[0];
        var issued = manager.Register.GetLicenses(customer.CustomerId)[0];
        manager.Workflow.Issue(manager.Session, issued, customer, IssueReasons.Initial);
        manager.Now = manager.Now.AddMinutes(5);

        batch.ExtendCommand.Execute(null);

        // Nothing ran as a batch, and the two untouched licences gained nothing.
        Assert.Empty(manager.Register.GetAudit(new AuditQuery { Action = "licence.batch-issued" }));
        Assert.Single(manager.Register.GetAudit(new AuditQuery { Action = "licence.issued" }));
        Assert.Contains("changed since this preview", shell.MessageText);

        // ⭐ And the preview the operator is now looking at is the CURRENT one, so a second press acts on
        //   what they can read rather than repeating the refusal.
        Assert.Contains(batch.Rows, row => row.ReasonLabel == "Renewal");
    }

    [Fact]
    public void AHeldOperationIssuesNothingAtAll()
    {
        // ⭐ D‑3. One licence that cannot be extended holds the other two, and pressing the action anyway
        //   must leave the register untouched — not extend the two that were fine.
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        manager.SaveLicense(customer);
        manager.SaveLicense(customer);
        var far = manager.Register.SaveLicense(
            manager.SaveLicense(customer) with { ExpiresAt = LicenseDay.EndOf(new DateTime(2035, 1, 1)) });

        var (shell, batch) = Attach(manager);
        TickEverything(shell);
        batch.TargetDate = new DateTime(2029, 6, 30);

        Assert.False(batch.CanExtend);
        Assert.False(batch.ExtendCommand.CanExecute(null));

        batch.ExtendCommand.Execute(null);

        Assert.Empty(manager.Register.GetAudit(new AuditQuery { Action = "licence.issued" }));
        Assert.Empty(manager.Register.GetArtifacts(far.LicenseId));

        // ⚠ And the selection is still there: an operator whose batch was held must be able to fix the
        //   one licence rather than start again from an empty list.
        Assert.Equal(3, shell.Browser.CheckedCount);
    }

    [Fact]
    public void EveryTickedLicenceIsCoveredEvenWhenTheFiltersHideIt()
    {
        // ⭐⭐ "20 selected, 19 done" in its most likely disguise: the operator ticks twenty, then types
        //    into the search box. The ticks are held by ID, so the operation still covers all twenty —
        //    and the preview lists all twenty by name, so nothing runs that was not read.
        using var manager = new ManagerFixture();
        var (shell, batch) = Shell(manager, licences: 3);

        TickEverything(shell);
        Assert.Equal(3, shell.Browser.CheckedCount);

        shell.Browser.SearchText = "nothing matches this";
        Assert.Empty(shell.Browser.Results);
        Assert.Equal(3, shell.Browser.CheckedCount);
        Assert.Equal(3, shell.Browser.CheckedNotShown);

        batch.TargetDate = new DateTime(2029, 6, 30);
        Assert.Equal(3, batch.Rows.Count);

        batch.ExtendCommand.Execute(null);

        Assert.Equal(3, manager.Register.GetAudit(new AuditQuery { Action = "licence.issued" }).Count);
    }

    [Fact]
    public void ABatchWritesNoFileAndSaysSo()
    {
        // ⭐ D‑4. The batch ends at a committed register. `licence.exported` is the audit action every
        //   file-writing path records, so its absence is the observable form of "nothing left the
        //   process".
        using var manager = new ManagerFixture();
        var (shell, batch) = Shell(manager, licences: 2);

        TickEverything(shell);
        batch.TargetDate = new DateTime(2029, 6, 30);
        batch.ExtendCommand.Execute(null);

        Assert.Empty(manager.Register.GetAudit(new AuditQuery { Action = "licence.exported" }));
        Assert.Contains("Nothing was written to disk", batch.LastResult);
    }

    [Fact]
    public void TheResultNamesTheBatchTheCountAndTheFirstIssues()
    {
        // ⭐ The operator must be able to answer "did the whole thing go through, and what did it do?"
        //   from what is on screen, without opening the register.
        using var manager = new ManagerFixture();
        var (shell, batch) = Shell(manager, licences: 2);

        TickEverything(shell);
        batch.TargetDate = new DateTime(2029, 6, 30);
        batch.ExtendCommand.Execute(null);

        var batchId = Assert.Single(
            manager.Register.GetAudit(new AuditQuery { Action = "licence.batch-issued" })).TargetId;

        Assert.Contains("2 licences extended to 2029-06-30", batch.LastResult);
        Assert.Contains(batchId, batch.LastResult);
        Assert.Contains("2 of them received a first artifact", batch.LastResult);
        Assert.True(shell.IsSuccess);
    }

    [Fact]
    public void TheTicksAreDroppedOnlyOnceTheBatchIsRecorded()
    {
        using var manager = new ManagerFixture();
        var (shell, batch) = Shell(manager, licences: 2);

        TickEverything(shell);
        batch.TargetDate = new DateTime(2029, 6, 30);
        batch.ExtendCommand.Execute(null);

        Assert.Equal(0, shell.Browser.CheckedCount);
        Assert.Empty(batch.Rows);
        Assert.False(batch.CanExtend);
    }

    [Fact]
    public void EveryArtifactABatchProducesIsRecordedWithTheReasonThePreviewShowed()
    {
        // ⚠ `issued_artifacts.reason` is append-only, so a value written here can never be corrected. The
        //   preview said "Initial issue" for licences that had never been issued (D‑2) — this asserts the
        //   register agrees with what was on screen.
        using var manager = new ManagerFixture();
        var (shell, batch) = Shell(manager, licences: 2);

        TickEverything(shell);
        batch.TargetDate = new DateTime(2029, 6, 30);

        Assert.All(batch.Rows, row => Assert.Equal("Initial issue", row.ReasonLabel));

        batch.ExtendCommand.Execute(null);

        foreach (var licence in manager.Register.QueryLicenses())
        {
            var artifact = Assert.Single(manager.Register.GetArtifacts(licence.License.LicenseId));
            Assert.Equal(IssueReasons.Initial, artifact.Reason);
        }
    }

    [Fact]
    public void ASecondRoundOfTheSameBatchIsRecordedAsARenewal()
    {
        // ⭐ The whole point of D‑1/D‑2 together: the FIRST pass over never-issued licences is initial,
        //   and the second is a renewal, without the operator choosing differently.
        using var manager = new ManagerFixture();
        var (shell, batch) = Shell(manager, licences: 2);

        TickEverything(shell);
        batch.TargetDate = new DateTime(2029, 6, 30);
        batch.ExtendCommand.Execute(null);

        // ⚠ The clock must move: the issuer truncates `iat` to whole seconds, and an artifact that does
        //   not come after the current one is refused (§39.3).
        manager.Now = manager.Now.AddMinutes(5);

        TickEverything(shell);
        batch.TargetDate = new DateTime(2030, 6, 30);

        Assert.All(batch.Rows, row => Assert.Equal("Renewal", row.ReasonLabel));

        batch.ExtendCommand.Execute(null);

        foreach (var licence in manager.Register.QueryLicenses())
        {
            var artifacts = manager.Register.GetArtifacts(licence.License.LicenseId);
            Assert.Equal(2, artifacts.Count);

            // ⛔ The earlier artifact is untouched and still says what it always said.
            Assert.Equal(IssueReasons.Renewal, artifacts[0].Reason);
            Assert.Equal(IssueReasons.Initial, artifacts[1].Reason);
            Assert.Equal(ArtifactStatuses.Current, artifacts[0].Status);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static (ShellViewModel Shell, BatchRenewalViewModel Batch) Shell(
        ManagerFixture manager, int licences, string customerName = "ACME Sp. z o.o.")
    {
        var customer = manager.SaveCustomer(customerName);
        for (var i = 0; i < licences; i++)
        {
            manager.SaveLicense(customer);
        }

        return Attach(manager);
    }

    private static (ShellViewModel Shell, BatchRenewalViewModel Batch) Attach(ManagerFixture manager)
    {
        var shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
        return (shell, shell.BatchRenewal);
    }

    // Ticking through the collection the LIST writes into — the same path a click takes.
    private static void TickEverything(ShellViewModel shell) =>
        shell.Browser.CheckAllShownCommand.Execute(null);
}
